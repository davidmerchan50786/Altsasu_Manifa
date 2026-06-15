// Assets/Scripts/Systems/MuestreadorAlturaMosaico.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MUESTREADOR DE ALTURA — Mosaico V2 con decode lattice 1/64 (Mosaico V3 Fase 0)
//
//  Diseño: Docs/arquitectura_mosaico_v3.md §1
//
//  Carga los RAW uint16 del Mosaico V2 en RAM (uno por tile) y resuelve
//  IMuestreadorAlturaPrecisa.AlturaMundo() vía muestreo BILINEAL sobre el
//  lattice → alturas bit-exactas con el bake y con el gate Python
//  (ValidarMosaicoV2.py).
//
//  Es ADITIVO: NO se autoarranca; se registra en ServiceLocator solo si un
//  BootstrapMuestreadorAltura (o un script de boot del usuario) lo activa
//  explícitamente. Coste de memoria ~126 MB (todos los RAWs) → opt-in.
//
//  Pensado para consumidores que necesitan precisión bit-exacta:
//    · Foot IK avanzado (sin raycast, evita TerrainCollider — clave para
//      Mosaico V3 GPU-driven donde no hay TerrainCollider).
//    · Spawn de árboles/edificios reproducible.
//    · NavMesh local bake.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed class MuestreadorAlturaMosaico : MonoBehaviour, IMuestreadorAlturaPrecisa
{
    [Tooltip("Si está OFF, el componente no carga nada y no se registra como servicio.")]
    [SerializeField] bool activarEnStart = true;

    [Tooltip("Vuelca log con MB cargados y ms de carga.")]
    [SerializeField] bool logarCarga = true;

    [Tooltip("Paso (m) usado por NormalMundo para diferencias finitas.")]
    [SerializeField, Range(0.1f, 5f)] float pasoNormalM = 1f;

    // ── Estado ──────────────────────────────────────────────────────────────
    public bool Listo { get; private set; }

    MosaicoManifest _man;
    // RAW por tile: índice plano (mismo orden que _man.tiles).
    ushort[][] _rawPorTile;
    // Lookup O(1): (anillo, fila, col) → índice plano en _rawPorTile.
    readonly Dictionary<(int, int, int), int> _porIndice = new();

    // ── Bootstrap (no se autoarranca: hay que activarEnStart o registrar a mano) ──
    void Start()
    {
        if (!activarEnStart) return;
        StartCoroutine(CargarYRegistrar());
    }

    System.Collections.IEnumerator CargarYRegistrar()
    {
        string ruta = CargadorMosaicoTerreno.RutaManifest();
        if (ruta == null)
        {
            AlsasuaLogger.Warn("MuestreadorAltura",
                "manifest_v2.json no existe — el muestreador NO se activa.");
            yield break;
        }

        try { _man = MosaicoManifest.Cargar(ruta); }
        catch (System.Exception ex)
        {
            AlsasuaLogger.Warn("MuestreadorAltura", $"Manifest ilegible: {ex.Message}");
            yield break;
        }

        string dirAbs = Path.Combine(Application.dataPath, CargadorMosaicoTerreno.DIR_TILES_REL);

        // Carga en hilo de fondo (uno por tile) y vuelco a RAM con presupuesto
        // por frame para no bloquear al jugador en arranque.
        _rawPorTile = new ushort[_man.tiles.Count][];

        long bytesTotales = 0;
        float t0 = Time.realtimeSinceStartup;

        for (int i = 0; i < _man.tiles.Count; i++)
        {
            var d = _man.tiles[i];
            string ruteAbs = Path.Combine(dirAbs, d.file);
            var tarea = System.Threading.Tasks.Task.Run(() => LeerRaw(ruteAbs, d.res));
            while (!tarea.IsCompleted) yield return null;

            if (!tarea.IsCompletedSuccessfully)
            {
                AlsasuaLogger.Warn("MuestreadorAltura",
                    $"Tile {d.file} ilegible: {tarea.Exception?.GetBaseException().Message} — se omite.");
                continue;
            }

            _rawPorTile[i] = tarea.Result;
            bytesTotales += (long)tarea.Result.Length * sizeof(ushort);

            DescomponerIndice(d, out int fila, out int colIdx);
            _porIndice[(d.anillo, fila, colIdx)] = i;

            // Reparte la carga: cede el frame cada ~8 ms acumulados.
            if ((Time.realtimeSinceStartup - t0) * 1000f > 8f) { yield return null; t0 = Time.realtimeSinceStartup; }
        }

        Listo = true;
        ServiceLocator.Registrar<IMuestreadorAlturaPrecisa>(this);

        if (logarCarga)
            AlsasuaLogger.Info("MuestreadorAltura",
                $"Muestreador listo: {_man.tiles.Count} tiles, " +
                $"{bytesTotales / (1024f * 1024f):F1} MB en RAM.");
    }

    void OnDestroy()
    {
        if (ReferenceEquals(ServiceLocator.Get<IMuestreadorAlturaPrecisa>(), this))
            ServiceLocator.Desregistrar<IMuestreadorAlturaPrecisa>();
        _rawPorTile = null;
        _porIndice.Clear();
        Listo = false;
    }

    static ushort[] LeerRaw(string ruta, int res)
    {
        byte[] raw = File.ReadAllBytes(ruta);
        int esperado = res * res * 2;
        if (raw.Length != esperado)
            throw new IOException($"{Path.GetFileName(ruta)}: {raw.Length} B (esperados {esperado})");
        var u = new ushort[res * res];
        for (int i = 0, j = 0; i < raw.Length; i += 2, j++)
            u[j] = (ushort)(raw[i] | (raw[i + 1] << 8));
        return u;
    }

    void DescomponerIndice(MosaicoManifest.TileDef def, out int fila, out int col)
    {
        var a = _man.anillos[0];
        // primero busca el anillo correcto
        for (int k = 0; k < _man.anillos.Count; k++)
            if (_man.anillos[k].id == def.anillo) { a = _man.anillos[k]; break; }

        var ch = _man.convencionHorizontal;
        col  = Mathf.RoundToInt((def.x - ((float)ch.OX - a.halfExtent)) / a.tileM);
        fila = Mathf.RoundToInt((def.z - ((float)ch.OZ - a.halfExtent)) / a.tileM);
    }

    // ── IMuestreadorAlturaPrecisa ───────────────────────────────────────────
    public float AlturaMundo(Vector3 p) => AlturaMundo(p.x, p.z);

    public float AlturaMundo(float x, float z)
    {
        if (!Listo || _man == null) return 0f;

        if (!TileEn(x, z, out int idxTile, out var def))
            return _man.datumYBase;       // fuera del mosaico

        int res = def.res;
        var raw = _rawPorTile[idxTile];
        if (raw == null) return def.y;

        // Coordenadas locales [0..res-1] en el tile.
        float u = (x - def.x) / def.ancho * (res - 1);
        float v = (z - def.z) / def.ancho * (res - 1);
        u = Mathf.Clamp(u, 0f, res - 1.001f);
        v = Mathf.Clamp(v, 0f, res - 1.001f);

        int u0 = (int)u, v0 = (int)v;
        int u1 = u0 + 1, v1 = v0 + 1;
        float tu = u - u0, tv = v - v0;

        // Recuerda: fila 0 = sur → indexado [v * res + u]. v ∈ [0..res-1].
        float h00 = raw[v0 * res + u0];
        float h10 = raw[v0 * res + u1];
        float h01 = raw[v1 * res + u0];
        float h11 = raw[v1 * res + u1];

        float a = h00 * (1 - tu) + h10 * tu;
        float b = h01 * (1 - tu) + h11 * tu;
        float q = a * (1 - tv)  + b * tv;       // q ∈ [0..65535]

        // alturaUnity = tile.y + q/64 (decode lattice 1/64).
        return def.y + q * (1f / 64f);
    }

    public Vector3 NormalMundo(float x, float z)
    {
        if (!Listo) return Vector3.up;
        float h = pasoNormalM;
        float yL = AlturaMundo(x - h, z);
        float yR = AlturaMundo(x + h, z);
        float yD = AlturaMundo(x, z - h);
        float yU = AlturaMundo(x, z + h);
        Vector3 n = new Vector3(yL - yR, 2f * h, yD - yU);
        return n.normalized;
    }

    // ── Lookup tile O(1) ────────────────────────────────────────────────────
    bool TileEn(float x, float z, out int idxTile, out MosaicoManifest.TileDef def)
    {
        idxTile = -1; def = null;
        var ch = _man.convencionHorizontal;
        float dx = x - (float)ch.OX, dz = z - (float)ch.OZ;
        float adx = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));

        for (int k = 0; k < _man.anillos.Count; k++)
        {
            var a = _man.anillos[k];     // orden fino → grueso (anillo 0 primero)
            if (adx > a.halfExtent) continue;
            int n = Mathf.RoundToInt(2f * a.halfExtent / a.tileM);
            int col = Mathf.Clamp(Mathf.FloorToInt((dx + a.halfExtent) / a.tileM), 0, n - 1);
            int fil = Mathf.Clamp(Mathf.FloorToInt((dz + a.halfExtent) / a.tileM), 0, n - 1);
            if (_porIndice.TryGetValue((a.id, fil, col), out int idx) && _rawPorTile[idx] != null)
            {
                idxTile = idx; def = _man.tiles[idx];
                return true;
            }
        }
        return false;
    }
}
