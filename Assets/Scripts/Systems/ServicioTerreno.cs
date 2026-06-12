// Assets/Scripts/Systems/ServicioTerreno.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SERVICIO TERRENO — único dueño del ciclo de vida del suelo jugable
//
//  Problema que sustituye (jun 2026):
//    · 4 sistemas tocaban el terreno sin contrato: SceneBootstrapper lo creaba,
//      CesiumFondoLejano / SistemaNavMesh / AlsasuaTreeStreamer hacían polling
//      ciego de hasta 30 s sobre Terrain.activeTerrain.
//    · EnsureTerrain() aceptaba CUALQUIER Terrain de la escena como válido
//      (p.ej. el demo de un asset pack en (0,0,0)) → no se generaba el DEM,
//      el jugador flotaba y Cesium calibraba contra un terreno equivocado.
//    · El parseo del DEM (1025² uint16) corría en el hilo principal.
//
//  Diseño:
//    · Contrato ITerrainService (Core) + ServiceLocator + TerrenoListoEvent.
//    · Máquina de estados: Inicializando → Generando → Listo | Fallido.
//    · Cadena de proveedores (en orden): 1. terreno existente VALIDADO
//      (marcador o geometría compatible con Alsasua; reconoce un mosaico ya
//      bakeado en escena por sus marcadores) → 2. MOSAICO V2 desde
//      manifest_v2.json (48 tiles, CargadorMosaicoTerreno; TerrenoListoEvent
//      al completar el ANILLO 0 y MosaicoCompletoEvent al terminar todos) →
//      3. DEM 6×6 km legacy → 4. plano de emergencia. Nunca espera infinita.
//    · Los Terrain AJENOS (demos de asset packs) se DESACTIVAN con aviso:
//      eran la causa silenciosa del mundo vacío.
//
//  Capa: WORLD (Systems). Publica TerrenoListoEvent; no referencia capas
//  superiores.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[DefaultExecutionOrder(-250)] // antes que SceneBootstrapper (-200)
public class ServicioTerreno : MonoBehaviour, ITerrainService
{
    // ── Geometría del mundo (única fuente: GeoDataAlsasua + estas consts) ───
    public const string NOMBRE_TERRENO = "Terrain_Alsasua_Runtime";
    const string DEM_REL_PATH   = "AlsasuaData/dem_unity_1025.raw";
    const int    DEM_RES        = 1025;
    const float  TER_W          = 6000f;
    const float  TER_L          = 6000f;
    const float  TER_H          = 900f;
    const float  TIMEOUT_PARSE  = 15f;   // s máximos del parseo en fondo
    const int    CAPA_TERRENO   = 8;

    static float TER_OX => GeoDataAlsasua.OX - TER_W * 0.5f;
    static float TER_OZ => GeoDataAlsasua.OZ - TER_L * 0.5f;
    // Altura de emergencia: cota de la plaza en el espacio local del mundo
    static float AlturaPlanoEmergencia => GeoDataAlsasua.COTA_PLAZA - GeoDataAlsasua.Z_MIN; // ≈ 20.61

    // ── ITerrainService ──────────────────────────────────────────────────────
    public EstadoTerreno Estado  { get; private set; } = EstadoTerreno.Inicializando;
    public FuenteTerreno Fuente  { get; private set; } = FuenteTerreno.Ninguna;
    public Terrain       Terreno { get; private set; } // con mosaico: tile ancla
    public bool          EstaListo => Estado == EstadoTerreno.Listo;
    public bool          EsMosaico => Fuente == FuenteTerreno.Mosaico;

    static readonly List<Terrain> _sinTiles = new();
    List<Terrain> _tiles;
    CargadorMosaicoTerreno _cargadorMosaico;

    public IReadOnlyList<Terrain> Tiles
        => _cargadorMosaico != null ? _cargadorMosaico.Tiles
         : _tiles ?? (IReadOnlyList<Terrain>)_sinTiles;

    public Terrain TerrainEn(Vector3 pos)
    {
        if (_cargadorMosaico != null) return _cargadorMosaico.TerrainEn(pos);
        if (_tiles != null) // mosaico bakeado adoptado: el tile más fino que contiene pos
        {
            Terrain mejor = null; int mejorAnillo = int.MaxValue;
            foreach (var t in _tiles)
            {
                if (t == null || t.terrainData == null) continue;
                Vector3 p = t.transform.position; Vector3 s = t.terrainData.size;
                if (pos.x < p.x || pos.x > p.x + s.x || pos.z < p.z || pos.z > p.z + s.z) continue;
                var m = t.GetComponent<MarcadorTerrenoAltsasua>();
                int a = m != null ? m.anillo : int.MaxValue - 1;
                if (a < mejorAnillo) { mejor = t; mejorAnillo = a; }
            }
            return mejor;
        }
        return Terreno; // terreno único o null (plano)
    }

    public float AlturaMundo(Vector3 pos)
    {
        var t = TerrainEn(pos) ?? Terreno;
        if (t != null)
            return t.SampleHeight(pos) + t.transform.position.y;
        return AlturaPlanoEmergencia;
    }

    // ── Singleton + auto-arranque ────────────────────────────────────────────
    public static ServicioTerreno Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<ServicioTerreno>() != null) return;
        new GameObject("ServicioTerreno").AddComponent<ServicioTerreno>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        ServiceLocator.Registrar<ITerrainService>(this);
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        ServiceLocator.Desregistrar<ITerrainService>();
        Instance = null;
    }

    // ── Pipeline de proveedores ──────────────────────────────────────────────
    IEnumerator Start()
    {
        float t0 = Time.realtimeSinceStartup;
        Estado = EstadoTerreno.Generando;

        // 1. ¿Hay ya un terreno de Alsasua VÁLIDO en la escena?
        //    (incluye un mosaico V2 bakeado: tiles con marcador fuente=Mosaico)
        if (AdoptarMosaicoExistente())
        {
            Fuente = FuenteTerreno.Mosaico;
            DesactivarTerrenosAjenos(null);
            Estado = EstadoTerreno.Listo;
            PublicarListo(t0);
            EventBus.Publish(new MosaicoCompletoEvent
            {
                tiles = _tiles.Count,
                segundosHastaCompleto = Time.realtimeSinceStartup - t0
            });
            yield break;
        }

        Terrain propio = BuscarTerrenoValidado();
        if (propio != null)
        {
            Fuente = FuenteTerreno.ExistenteValidado;
        }
        else if (CargadorMosaicoTerreno.RutaManifest() != null)
        {
            // 2. MOSAICO V2 desde manifest (48 tiles, anillo 0 primero)
            yield return CargarMosaico(t0);
            yield break; // CargarMosaico publica los eventos
        }
        else
        {
            // 3. Generar desde el DEM legacy (parseo en hilo de fondo)
            yield return GenerarDesdeDEM(t => propio = t);
            if (propio != null) Fuente = FuenteTerreno.DEM;
        }

        if (propio != null)
        {
            Terreno = propio;
            MarcarComoPropio(propio.gameObject, Fuente);
            DesactivarTerrenosAjenos(propio);
            Estado = EstadoTerreno.Listo;
        }
        else
        {
            // 4. Plano de emergencia — jugable, nunca mundo vacío
            CrearPlanoEmergencia();
            Fuente = FuenteTerreno.Plano;
            Estado = EstadoTerreno.Listo; // jugable aunque feo; Terreno queda null
        }

        PublicarListo(t0);
    }

    void PublicarListo(float t0)
    {
        float dur = Time.realtimeSinceStartup - t0;
        AlsasuaLogger.Info("Terreno",
            $"✅ Suelo listo en {dur:F2}s — fuente: {Fuente}" +
            (Terreno != null ? $" ('{Terreno.name}', size={Terreno.terrainData.size})" : " (plano)") +
            (EsMosaico ? $" [{Tiles.Count} tiles]" : ""));

        EventBus.Publish(new TerrenoListoEvent
        {
            terreno            = Terreno,
            fuente             = Fuente,
            segundosHastaListo = dur
        });
    }

    // ── Proveedor 2: mosaico V2 ──────────────────────────────────────────────
    IEnumerator CargarMosaico(float t0)
    {
        _cargadorMosaico = gameObject.AddComponent<CargadorMosaicoTerreno>();
        bool anillo0 = false, completo = false;
        StartCoroutine(_cargadorMosaico.Cargar(() => anillo0 = true, () => completo = true));

        // jugable al completar el anillo 0
        while (!anillo0 && !completo) yield return null;

        if (_cargadorMosaico.TileAncla == null)
        {
            // el manifest existía pero no se pudo instanciar nada → DEM/plano
            AlsasuaLogger.Warn("Terreno", "Mosaico V2 falló — pruebo DEM legacy.");
            Destroy(_cargadorMosaico); _cargadorMosaico = null;
            Terrain propio = null;
            yield return GenerarDesdeDEM(t => propio = t);
            if (propio != null)
            {
                Fuente = FuenteTerreno.DEM; Terreno = propio;
                MarcarComoPropio(propio.gameObject, Fuente);
                DesactivarTerrenosAjenos(propio);
            }
            else
            {
                CrearPlanoEmergencia();
                Fuente = FuenteTerreno.Plano;
            }
            Estado = EstadoTerreno.Listo;
            PublicarListo(t0);
            yield break;
        }

        Fuente = FuenteTerreno.Mosaico;
        Terreno = _cargadorMosaico.TileAncla;
        DesactivarTerrenosAjenos(null);
        Estado = EstadoTerreno.Listo;
        PublicarListo(t0); // anillo 0 listo: el mundo ya es jugable

        while (!completo) yield return null;
        AlsasuaLogger.Info("Terreno",
            $"✅ Mosaico completo: {Tiles.Count} tiles en " +
            $"{Time.realtimeSinceStartup - t0:F1}s.");
        EventBus.Publish(new MosaicoCompletoEvent
        {
            tiles = Tiles.Count,
            segundosHastaCompleto = Time.realtimeSinceStartup - t0
        });
    }

    // Un mosaico bakeado en escena (ConstructorMosaicoEditor) se reconoce por
    // sus marcadores; debe estar completo según el manifest para adoptarse.
    bool AdoptarMosaicoExistente()
    {
        var marcas = FindObjectsByType<MarcadorTerrenoAltsasua>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var tiles = new List<Terrain>();
        foreach (var m in marcas)
        {
            if (m.fuente != FuenteTerreno.Mosaico) continue;
            var t = m.GetComponent<Terrain>();
            if (t != null && t.terrainData != null) tiles.Add(t);
        }
        if (tiles.Count == 0) return false;

        string ruta = CargadorMosaicoTerreno.RutaManifest();
        int esperados = 48;
        MosaicoManifest man = null;
        if (ruta != null)
        {
            try { man = MosaicoManifest.Cargar(ruta); esperados = man.tiles.Count; }
            catch { /* sin manifest legible: aceptar el recuento de escena */ }
        }
        if (tiles.Count < esperados)
        {
            AlsasuaLogger.Warn("Terreno",
                $"Mosaico en escena INCOMPLETO ({tiles.Count}/{esperados}) — se regenera.");
            return false;
        }

        _tiles = tiles;
        // tile ancla = el del anillo más fino que contiene la plaza
        Terrain ancla = null; int mejorAnillo = int.MaxValue;
        var plaza = new Vector3(GeoDataAlsasua.OX, 0, GeoDataAlsasua.OZ);
        foreach (var t in tiles)
        {
            var m = t.GetComponent<MarcadorTerrenoAltsasua>();
            Vector3 p = t.transform.position; Vector3 s = t.terrainData.size;
            if (plaza.x >= p.x && plaza.x <= p.x + s.x &&
                plaza.z >= p.z && plaza.z <= p.z + s.z && m.anillo < mejorAnillo)
            { ancla = t; mejorAnillo = m.anillo; }
        }
        Terreno = ancla != null ? ancla : tiles[0];
        AlsasuaLogger.Info("Terreno",
            $"Mosaico bakeado adoptado: {tiles.Count} tiles (ancla '{Terreno.name}').");
        return true;
    }

    // ── Proveedor 1: terreno existente validado ──────────────────────────────
    // Válido = lleva MarcadorTerrenoAltsasua, o su geometría es compatible con
    // el mundo de Alsasua (≥2 km de lado y centrado a <500 m de Herriko Plaza).
    Terrain BuscarTerrenoValidado()
    {
        foreach (var t in FindObjectsByType<Terrain>(FindObjectsInactive.Include,
                                                     FindObjectsSortMode.None))
        {
            if (t.GetComponent<MarcadorTerrenoAltsasua>() != null) return t;

            if (t.terrainData == null) continue;
            Vector3 size   = t.terrainData.size;
            Vector3 centro = t.transform.position + new Vector3(size.x * 0.5f, 0, size.z * 0.5f);
            float distPlaza = Vector2.Distance(
                new Vector2(centro.x, centro.z),
                new Vector2(GeoDataAlsasua.OX, GeoDataAlsasua.OZ));

            if (size.x >= 2000f && distPlaza < 500f)
            {
                AlsasuaLogger.Info("Terreno",
                    $"Terrain existente '{t.name}' validado por geometría " +
                    $"(lado {size.x:F0} m, centro a {distPlaza:F0} m de la plaza).");
                return t;
            }
        }
        return null;
    }

    // Los Terrain que NO son el nuestro (demos de asset packs, pruebas) se
    // desactivan: si quedan activos se convierten en Terrain.activeTerrain
    // para cualquier consumidor y rompen alturas, NavMesh y Cesium.
    void DesactivarTerrenosAjenos(Terrain propio)
    {
        foreach (var t in FindObjectsByType<Terrain>(FindObjectsSortMode.None))
        {
            if (t == propio || !t.isActiveAndEnabled) continue;
            // los tiles del mosaico son propios (marcador con fuente=Mosaico)
            var m = t.GetComponent<MarcadorTerrenoAltsasua>();
            if (m != null && m.fuente == FuenteTerreno.Mosaico) continue;
            t.enabled = false;
            AlsasuaLogger.Warn("Terreno",
                $"⚠ Terrain AJENO desactivado: '{t.name}' en {t.transform.position} " +
                "(venía de un asset pack/demo y rompía el mundo — bórralo de la escena).");
        }
    }

    // ── Proveedor 2: DEM 6×6 km ──────────────────────────────────────────────
    IEnumerator GenerarDesdeDEM(System.Action<Terrain> onDone)
    {
        string demAbs = Path.Combine(Application.dataPath, DEM_REL_PATH);
        if (!File.Exists(demAbs))
        {
            AlsasuaLogger.Warn("Terreno", $"DEM no encontrado: {DEM_REL_PATH} — paso al siguiente proveedor.");
            yield break;
        }

        // Parseo en hilo de fondo: leer 2.1 MB y convertir 1025² uint16→float
        var tarea = System.Threading.Tasks.Task.Run(() =>
        {
            byte[] raw = File.ReadAllBytes(demAbs);
            int esperado = DEM_RES * DEM_RES * 2;
            if (raw.Length < esperado)
                throw new IOException($"DEM corrupto: {raw.Length} bytes (esperados {esperado}).");

            var h = new float[DEM_RES, DEM_RES];
            for (int r = 0; r < DEM_RES; r++)
            {
                int fila = r * DEM_RES * 2;
                for (int c = 0; c < DEM_RES; c++)
                {
                    int i = fila + c * 2;
                    h[r, c] = (ushort)(raw[i] | (raw[i + 1] << 8)) / 65535f; // uint16 LE
                }
            }
            return h;
        });

        float t = 0f;
        while (!tarea.IsCompleted && t < TIMEOUT_PARSE)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!tarea.IsCompletedSuccessfully)
        {
            AlsasuaLogger.Warn("Terreno", "Parseo DEM falló o superó el timeout: " +
                (tarea.Exception?.GetBaseException().Message ?? "timeout") +
                " — paso al siguiente proveedor.");
            yield break;
        }

        // Construcción en main thread (API Unity). SetHeights es UN hitch
        // medido y logueado, no un freeze indefinido.
        float tSet = Time.realtimeSinceStartup;
        var td = new TerrainData { heightmapResolution = DEM_RES };
        td.size = new Vector3(TER_W, TER_H, TER_L);
        td.SetHeights(0, 0, tarea.Result);

        var go = Terrain.CreateTerrainGameObject(td);
        go.name = NOMBRE_TERRENO;
        go.transform.position = new Vector3(TER_OX, 0f, TER_OZ);
        go.layer = CAPA_TERRENO;

        var terreno = go.GetComponent<Terrain>();
        SceneBootstrapper.AplicarCapaBaseTerreno(terreno); // textura base/ortofoto

        AlsasuaLogger.Info("Terreno",
            $"DEM aplicado: SetHeights+GO en {(Time.realtimeSinceStartup - tSet) * 1000f:F0} ms.");
        onDone(terreno);
    }

    // ── Proveedor 3: plano de emergencia ─────────────────────────────────────
    void CrearPlanoEmergencia()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
        go.name = "Terrain_PlanoEmergencia";
        go.transform.position   = new Vector3(GeoDataAlsasua.OX, AlturaPlanoEmergencia, GeoDataAlsasua.OZ);
        go.transform.localScale = new Vector3(500f, 1f, 500f); // 5×5 km
        go.layer = CAPA_TERRENO;
        go.GetComponent<MeshRenderer>().material.color = new Color(0.38f, 0.42f, 0.30f);
        MarcarComoPropio(go, FuenteTerreno.Plano);
        AlsasuaLogger.Warn("Terreno",
            "⚠ PLANO DE EMERGENCIA: ningún proveedor dio terreno real. " +
            "Revisa el DEM en AlsasuaData/ o regenera con 'Territorio Real → GENERAR TODO'.");
    }

    static void MarcarComoPropio(GameObject go, FuenteTerreno fuente)
    {
        var m = go.GetComponent<MarcadorTerrenoAltsasua>()
             ?? go.AddComponent<MarcadorTerrenoAltsasua>();
        m.fuente = fuente;
    }
}

/// <summary>
/// Marca el suelo jugable oficial de Alsasua. ServicioTerreno lo añade al
/// generar/adoptar; su presencia valida el terreno en arranques posteriores
/// (también sirve para terrenos guardados en escena desde el editor).
/// </summary>
public class MarcadorTerrenoAltsasua : MonoBehaviour
{
    [Tooltip("Proveedor que creó este suelo.")]
    public FuenteTerreno fuente = FuenteTerreno.Ninguna;

    [Tooltip("Solo mosaico V2: anillo del tile (0 urbano, 1 valle, 2 sierras).")]
    public int anillo = -1;

    [Tooltip("Solo mosaico V2: índices fila/columna del tile dentro de su anillo.")]
    public int fila = -1;
    public int columna = -1;
}
