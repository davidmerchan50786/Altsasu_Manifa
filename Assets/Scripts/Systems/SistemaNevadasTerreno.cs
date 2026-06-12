// Assets/Scripts/SistemaNevadasTerreno.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE NEVADAS — acumulación de nieve en terreno
//
//  El Arquitecto: falta un sistema que conecte SistemaClima.NieveLigera
//  con el splatmap del terreno. Sin esto la nieve visual solo son partículas
//  pero el suelo queda igual de marrón/verde.
//
//  Funciona en tres fases:
//    1. Acumulación: cuando nieva, aumenta la capa capaNieve (TerrainLayer)
//       solo en zonas planas (pendiente < umbralPendienteNieve).
//    2. Mantenimiento: mientras sigue nevando, mantiene la cobertura.
//    3. Fusión: al dejar de nevar, la nieve desaparece gradualmente
//       (más rápido en pendientes y zonas de sol).
//
//  MOSAICO V2: itera TODOS los tiles vía ITerrainService.Tiles. El umbral de
//  cota usa la altitud REAL (posY del tile + altura local + Z_MIN): con el
//  datum único del mosaico la línea de nieve es uniforme entre tiles, y por
//  encima de cotaNieveAlta (sierras) la nieve cuaja más y funde más despacio.
//
//  Shader global: _GlobalSnowLevel (0-1) para que materiales PBR
//  de edificios y vegetación muestren nieve sobre sus superficies.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-62)]
public class SistemaNevadasTerreno : MonoBehaviour
{
    public static SistemaNevadasTerreno Instance { get; private set; }

    [Header("Configuración")]
    [Tooltip("TerrainLayer de nieve. Si es null, se usa un tint blanco sobre capaPrado.")]
    public TerrainLayer capaNieve;
    [Tooltip("Pendiente máxima (°) en la que se acumula nieve.")]
    [Range(0f, 45f)] public float umbralPendienteNieve = 28f;
    [Tooltip("Velocidad de acumulación (0-1 por segundo).")]
    public float velocidadAcumulacion = 0.02f;
    [Tooltip("Velocidad de fusión (0-1 por segundo).")]
    public float velocidadFusion = 0.008f;
    [Tooltip("Cobertura máxima de nieve (0-1). 1.0 = todo blanco.")]
    [Range(0f, 1f)] public float coberturaMaxima = 0.75f;
    [Tooltip("Cota real (m s.n.m.) a partir de la cual la nieve cuaja con más " +
             "fuerza y funde más despacio (sierras).")]
    public float cotaNieveAlta = 950f;

    static readonly int ID_SnowLevel = Shader.PropertyToID("_GlobalSnowLevel");

    SistemaClima _clima;

    struct TileNieve
    {
        public Terrain terrain;
        public TerrainData td;
        public int idxNieve;
    }
    readonly List<TileNieve> _tiles = new();

    float _nivelNieve;         // 0 seco … coberturaMaxima
    bool  _listo;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start() => StartCoroutine(InicializarTras(5f));

    IEnumerator InicializarTras(float d)
    {
        yield return new WaitForSeconds(d);
        _clima = FindFirstObjectByType<SistemaClima>();

        // esperar al servicio de terreno (mosaico o único); timeout explícito
        float t = 0f;
        ITerrainService svc = null;
        while (t < 30f)
        {
            svc = ServiceLocator.Get<ITerrainService>();
            if (svc != null && svc.EstaListo) break;
            t += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }

        if (svc != null && svc.Tiles.Count > 0)
        {
            foreach (var terr in svc.Tiles) RegistrarTile(terr);
        }
        else if (Terrain.activeTerrain != null)
        {
            RegistrarTile(Terrain.activeTerrain); // escena legacy sin servicio
        }
        if (_tiles.Count == 0) yield break;

        _listo = true;
        StartCoroutine(CicloNieve());
        AlsasuaLogger.Info("Nieve", $"Sistema de nevadas listo ({_tiles.Count} tiles).");
    }

    void RegistrarTile(Terrain terr)
    {
        if (terr == null || terr.terrainData == null) return;
        var td = terr.terrainData;
        int idx = -1;
        if (capaNieve != null)
        {
            var capas = td.terrainLayers;
            for (int i = 0; i < capas.Length; i++)
                if (capas[i] == capaNieve) { idx = i; break; }
            if (idx < 0)
            {
                var nuevas = new TerrainLayer[capas.Length + 1];
                capas.CopyTo(nuevas, 0);
                nuevas[capas.Length] = capaNieve;
                td.terrainLayers = nuevas;
                idx = capas.Length;
            }
        }
        _tiles.Add(new TileNieve { terrain = terr, td = td, idxNieve = idx });
    }

    IEnumerator CicloNieve()
    {
        while (true)
        {
            yield return new WaitForSeconds(15f);
            if (!_listo) continue;

            bool nieva = _clima != null &&
                (_clima.climaActual == SistemaClima.EstadoClima.NieveLigera);

            // Objetivo: coberturaMaxima si nieva, 0 si no
            float objetivo = nieva ? coberturaMaxima : 0f;
            float vel = nieva ? velocidadAcumulacion : velocidadFusion;

            float nuevo = Mathf.MoveTowards(_nivelNieve, objetivo, vel * 15f); // 15s de ciclo
            if (Mathf.Abs(nuevo - _nivelNieve) < 0.005f) continue;

            _nivelNieve = nuevo;
            Shader.SetGlobalFloat(ID_SnowLevel, _nivelNieve);

            foreach (var tile in _tiles)
                if (tile.idxNieve >= 0 && tile.td != null)
                    yield return StartCoroutine(AplicarNieveSplatmap(tile, _nivelNieve));
        }
    }

    IEnumerator AplicarNieveSplatmap(TileNieve tile, float nivel)
    {
        var td = tile.td;
        int idxNieve = tile.idxNieve;
        int res   = td.alphamapResolution;
        int capas = td.alphamapLayers;
        if (capas <= idxNieve) yield break;

        float baseY = tile.terrain.transform.position.y;
        var alpha = td.GetAlphamaps(0, 0, res, res);

        for (int ay = 0; ay < res; ay++)
        {
            for (int ax = 0; ax < res; ax++)
            {
                float nx = ax / (float)(res - 1);
                float nz = ay / (float)(res - 1);

                // Nieve solo en zonas planas
                float pendiente = td.GetSteepness(nx, nz);
                if (pendiente > umbralPendienteNieve) continue;

                // Cantidad según nivel global y pendiente
                float factorPlano = 1f - pendiente / umbralPendienteNieve;
                float cantNieve   = nivel * factorPlano;

                // Cota REAL del punto (datum único del mosaico):
                // por encima de cotaNieveAlta la nieve cuaja a plena cobertura
                float cotaReal = baseY + td.GetInterpolatedHeight(nx, nz) + GeoDataAlsasua.Z_MIN;
                if (cotaReal >= cotaNieveAlta && nivel > 0.01f)
                    cantNieve = Mathf.Max(cantNieve, nivel); // ignora la pendiente parcialmente

                // No cubrir roca ni asfalto completamente
                float cantBloq = capas > 2 ? alpha[ay, ax, 2] : 0f; // roca
                cantBloq      += capas > 5 ? alpha[ay, ax, 5] : 0f; // asfalto
                cantNieve      = Mathf.Min(cantNieve, 1f - cantBloq * 0.8f);

                // Distribuir: reducir proporcional todos los otros canales
                float pesoActualNieve = alpha[ay, ax, idxNieve];
                float delta           = cantNieve - pesoActualNieve;
                if (Mathf.Abs(delta) < 0.01f) continue;

                // Escalar el resto
                float totalOtros = 1f - pesoActualNieve;
                if (totalOtros > 0.001f)
                {
                    float escala = (1f - cantNieve) / totalOtros;
                    for (int c = 0; c < capas; c++)
                        if (c != idxNieve) alpha[ay, ax, c] *= escala;
                }
                alpha[ay, ax, idxNieve] = cantNieve;
            }
            if (ay % 32 == 0) yield return null;
        }

        td.SetAlphamaps(0, 0, alpha);
        SistemaDetalleTerreno.Instance?.InvalidarCacheAlpha();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }
}
