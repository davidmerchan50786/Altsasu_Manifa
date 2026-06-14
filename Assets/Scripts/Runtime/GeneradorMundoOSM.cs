// GeneradorMundoOSM.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GENERADOR DE MUNDO OSM — construye edificios y calles al arrancar el juego
//  Usa: buildings_unity.json + roads_unity.json + trees_unity.json
//  Asigna automáticamente materiales HDRP de Assets/Materials/
//  Se ejecuta en Start() durante el SceneBootstrap, antes de soltar el control
//  al jugador. No requiere Play corto — genera en ~2-3 segundos.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;

[DefaultExecutionOrder(-90)]
public class GeneradorMundoOSM : MonoBehaviour
{
    public static GeneradorMundoOSM Instance { get; private set; }
    public static bool MundoListo { get; private set; }
    public static event Action OnMundoGenerado;

    // ── Offsets de coordenadas (sistema terreno) ──────────────────────────
    // Constantes geográficas centralizadas en GeoDataAlsasua
    const float OFFSET_X = GeoDataAlsasua.OX;
    const float OFFSET_Z = GeoDataAlsasua.OZ;

    // ── Materiales por tipo ───────────────────────────────────────────────
    Material _matEdifResidencial, _matEdifComercial, _matEdifIndustrial;
    Material _matAsfalto, _matBordillo, _matAcera;
    Material _matArbol;

    // ── Parent objects ────────────────────────────────────────────────────
    Transform _parentEdificios, _parentCalles, _parentArboles;

    // ── Stats ─────────────────────────────────────────────────────────────
    int _edificiosCreados, _callesCreadas, _arbolesCreados;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start() => StartCoroutine(GenerarMundo());

    IEnumerator GenerarMundo()
    {
        AlsasuaLogger.Info("OSM", "Indexando datos OSM para zone streaming…");

        // Esperar al terreno y a SistemaZonas.
        // FIX (jun 2026): 8s era poco en el primer Play (compilación de shaders,
        // streaming Cesium...) y el aborto era PERMANENTE → "GeneradorMundoOSM
        // indexado" fallaba en F1 sin recuperación. Margen ampliado a 25s.
        float t = 0;
        while ((Terrain.activeTerrain == null || SistemaZonas.Instance == null) && t < 25f)
            { t += 0.5f; yield return new WaitForSeconds(0.5f); }

        if (SistemaZonas.Instance == null)
        {
            AlsasuaLogger.Error("OSM", "SistemaZonas no disponible tras 25s — abortando generación OSM.");
            yield break;
        }

        CargarMateriales();
        if (_parentArboles == null) _parentArboles = new GameObject("Arboles_OSM").transform;

        // ── FASE 1: Edificios ─────────────────────────────────────────────
        bool edificiosOk = false;
        yield return StartCoroutine(EjecutarFaseSegura(
            IndexarEdificios(), "edificios", r => edificiosOk = r));
        yield return null;

        // ── FASE 2: Calles ────────────────────────────────────────────────
        yield return StartCoroutine(EjecutarFaseSegura(
            IndexarCalles(), "calles", _ => { }));
        yield return null;

        // ── FASE 3: Árboles — DESACTIVADA ─────────────────────────────────
        // AlsasuaTreeStreamer ya hace streaming de los 3311 árboles LIDAR reales.
        // Generarlos también aquí (trees_unity.json) duplicaba ~3000 instancias
        // y saturaba el arranque (FPS a 0). El streamer es la fuente única.

        // Marcar listo aunque alguna fase haya fallado parcialmente
        SistemaZonas.Instance?.MarcarIndexadoListo();
        MundoListo = true;
        OnMundoGenerado?.Invoke();
        AlsasuaLogger.Info("OSM",
            $"✅ Índice OSM listo: {_edificiosCreados} edificios, {_callesCreadas} tramos indexados" +
            (edificiosOk ? "" : " ⚠ sin datos de edificios"));
    }

    /// <summary>
    /// Envuelve una corrutina en un bloque seguro: la ejecuta y captura cualquier excepción,
    /// logando el error sin detener el pipeline completo.
    /// </summary>
    IEnumerator EjecutarFaseSegura(IEnumerator fase, string nombreFase, System.Action<bool> resultado)
    {
        bool ok = true;
        Exception capturada = null;

        // Ejecutar paso a paso para poder capturar excepciones
        while (true)
        {
            object current;
            try   { if (!fase.MoveNext()) break; current = fase.Current; }
            catch (Exception ex) { capturada = ex; break; }
            yield return current;
        }

        if (capturada != null)
        {
            ok = false;
            AlsasuaLogger.Error("OSM",
                $"Error en fase '{nombreFase}': {capturada.GetType().Name} — {capturada.Message}");
        }

        resultado(ok);
    }

    IEnumerator IndexarEdificios()
    {
        string path = Path.Combine(Application.dataPath, "AlsasuaData", "buildings_unity.json");
        if (!File.Exists(path))
        {
            AlsasuaLogger.Warn("OSM", $"buildings_unity.json no encontrado en: {path}");
            yield break;
        }

        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception ex)
        {
            AlsasuaLogger.Error("OSM", $"No se pudo leer buildings_unity.json: {ex.Message}");
            yield break;
        }

        EdificioData[] edificios;
        try { edificios = JsonHelper.ParseArray<EdificioData>(json); }
        catch (Exception ex)
        {
            AlsasuaLogger.Error("OSM", $"JSON de edificios malformado: {ex.Message}");
            yield break;
        }

        if (edificios == null || edificios.Length == 0)
        {
            AlsasuaLogger.Warn("OSM", "buildings_unity.json vacío o sin edificios válidos.");
            yield break;
        }

        int lote = 0;
        foreach (var e in edificios)
        {
            if (e?.vertices == null || e.vertices.Length < 3) continue;

            float cx = 0, cz = 0;
            foreach (var v in e.vertices) { cx += v.x + OFFSET_X; cz += v.z + OFFSET_Z; }
            cx /= e.vertices.Length; cz /= e.vertices.Length;

            SistemaZonas.Instance?.RegistrarEdificio(e, new Vector3(cx, 0, cz));
            _edificiosCreados++;
            if (++lote >= 60) { lote = 0; yield return null; }
        }
        AlsasuaLogger.Info("OSM", $"Indexados {_edificiosCreados} edificios.");
    }

    IEnumerator IndexarCalles()
    {
        string path = Path.Combine(Application.dataPath, "AlsasuaData", "roads_unity.json");
        if (!File.Exists(path))
        {
            AlsasuaLogger.Warn("OSM", $"roads_unity.json no encontrado en: {path}");
            yield break;
        }

        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception ex)
        {
            AlsasuaLogger.Error("OSM", $"No se pudo leer roads_unity.json: {ex.Message}");
            yield break;
        }

        RoadData[] roads;
        try { roads = JsonHelper.ParseArray<RoadData>(json); }
        catch (Exception ex)
        {
            AlsasuaLogger.Error("OSM", $"JSON de calles malformado: {ex.Message}");
            yield break;
        }

        if (roads == null || roads.Length == 0) yield break;

        int lote = 0;
        foreach (var r in roads)
        {
            if (r?.points == null || r.points.Length < 2) continue;

            float cx = (r.points[0].x + r.points[^1].x) * 0.5f + OFFSET_X;
            float cz = (r.points[0].z + r.points[^1].z) * 0.5f + OFFSET_Z;

            SistemaZonas.Instance?.RegistrarCalle(r, new Vector3(cx, 0, cz));
            _callesCreadas++;
            if (++lote >= 40) { lote = 0; yield return null; }
        }
        AlsasuaLogger.Info("OSM", $"Indexadas {_callesCreadas} calles.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  API PÚBLICA — llamada por SistemaZonas al cargar cada zona
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Construye el mesh de un edificio y lo añade bajo <paramref name="parent"/>.</summary>
    public void ConstruirEdificio(EdificioData e, Transform parent)
    {
        if (e.vertices == null || e.vertices.Length < 3) return;

        var verts2D = new List<Vector2>();
        foreach (var v in e.vertices)
            verts2D.Add(new Vector2(v.x + OFFSET_X, v.z + OFFSET_Z));

        float cx = 0, cz = 0;
        foreach (var v in verts2D) { cx += v.x; cz += v.y; }
        cx /= verts2D.Count; cz /= verts2D.Count;

        // FIDELIDAD: en parcelas con pendiente, usar la altura MÍNIMA del
        // terreno bajo el footprint (no la del centroide). El edificio asienta
        // en el punto más bajo y la fachada cuesta arriba queda enterrada unos
        // cm — como los edificios reales — en vez de flotar cuesta abajo.
        float suelo = AlturaTerreno(cx, cz);
        float techoTerreno = suelo;
        foreach (var v in verts2D)
        {
            float h = AlturaTerreno(v.x, v.y);
            if (h < suelo)        suelo = h;
            if (h > techoTerreno) techoTerreno = h;
        }

        float altura = Mathf.Max(3.2f, e.height > 0 ? e.height : e.levels * 3.2f);
        // Compensar el desnivel para que la cota de cornisa no baje
        altura += Mathf.Min(techoTerreno - suelo, 4f);

        var mesh = GenerarMeshEdificio(verts2D, suelo, altura);
        if (mesh == null) return;

        var go = new GameObject(string.IsNullOrEmpty(e.name)
            ? $"Edif_{e.id}" : $"Edif_{e.name.Replace(" ", "_")}");
        go.transform.SetParent(parent);
        go.isStatic = true;

        var mf = go.AddComponent<MeshFilter>();  mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = MaterialParaTipo(e.type);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        // Collider solo para edificios cercanos al centro (<150m de Herriko Plaza)
        if (Vector2.Distance(new Vector2(cx, cz), new Vector2(OFFSET_X, OFFSET_Z)) < 150f)
            go.AddComponent<MeshCollider>().sharedMesh = mesh;

        AnadirLOD(go, mr, mesh);
    }

    // Buffer reutilizable para la subdivisión de ejes (cero allocs por calle)
    readonly List<Vector3> _bufSubdivision = new(256);

    /// <summary>Construye el mesh de un tramo de calle y lo añade bajo <paramref name="parent"/>.</summary>
    public void ConstruirCalle(RoadData r, Transform parent)
    {
        float ancho = Mathf.Max(2f, r.width);
        var pts = Array.ConvertAll(r.points, p =>
            new Vector3(p.x + OFFSET_X, 0, p.z + OFFSET_Z));
        for (int i = 0; i < pts.Length; i++)
            pts[i].y = AlturaTerreno(pts[i].x, pts[i].z) + 0.03f;

        // FIDELIDAD: los nodos OSM pueden distar 50–170m → subdividir a ≤8m
        // y conformar cada vértice (bordes incluidos) al terreno LIDAR real.
        // Sin esto las calles cortan curvas y flotan/se hunden en las cuestas.
        MeshBuilder.SubdividirPolilinea(pts, _bufSubdivision, 8f);
        var mesh = MeshBuilder.BandaConformada(
            _bufSubdivision, ancho, AlturaTerreno, 0.03f);
        if (mesh == null) return;

        string nombre = string.IsNullOrEmpty(r.name)
            ? $"Calle_{r.id}" : $"Calle_{r.name.Replace(" ", "_")}";
        var go = new GameObject(nombre);
        go.transform.SetParent(parent);
        go.isStatic = true;

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = r.type is "pedestrian" or "path" ? _matAcera : _matAsfalto;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MESH GENERATION — shared por ConstruirEdificio y ConstruirCalle
    // ════════════════════════════════════════════════════════════════════════

    static Mesh GenerarMeshEdificio(List<Vector2> planta, float suelo, float altura)
        => MeshBuilder.Edificio(planta, suelo, altura);

    // ════════════════════════════════════════════════════════════════════════
    //  CALLES
    // ════════════════════════════════════════════════════════════════════════


    static Mesh GenerarMeshCalle(Vector3[] pts, float ancho)
        => MeshBuilder.Banda(pts, ancho);

    // ════════════════════════════════════════════════════════════════════════
    //  ÁRBOLES
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator GenerarArboles()
    {
        string path = Path.Combine(Application.dataPath, "AlsasuaData", "trees_unity.json");
        if (!File.Exists(path)) yield break;

        string json = File.ReadAllText(path);
        var arboles = JsonHelper.ParseArray<ArbolData>(json);
        if (arboles == null) yield break;

        // Buscar prefab de árbol en Resources o _ExtractedAssets
        GameObject prefabArbol = Resources.Load<GameObject>("Prefabs/arbol") ??
            BuscarPrefabArbol();

        // BUG FIX 8: añadir yield cada N árboles para evitar congelar el main thread
        // un frame completo cuando hay cientos de árboles. Sin esto, la corrutina
        // instancia todos los árboles en un solo frame → spike de CPU visible.
        int lote = 0;
        foreach (var a in arboles)
        {
            float x = a.x + OFFSET_X, z = a.z + OFFSET_Z;
            float y = AlturaTerreno(x, z);
            Vector3 pos = new Vector3(x, y, z);

            GameObject arbolGO;
            if (prefabArbol != null)
                arbolGO = Instantiate(prefabArbol, pos, Quaternion.Euler(0, UnityEngine.Random.Range(0,360), 0), _parentArboles);
            else
                arbolGO = CrearArbolProcedural(pos, a.radio);

            arbolGO.isStatic = true;
            _arbolesCreados++;
            if (++lote >= 30) { lote = 0; yield return null; }
        }
        yield return null;
    }

    GameObject CrearArbolProcedural(Vector3 pos, float radio)
    {
        var go = new GameObject("Arbol");
        go.transform.SetParent(_parentArboles);
        go.transform.position = pos;

        // Tronco
        var tronco = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tronco.transform.SetParent(go.transform);
        tronco.transform.localPosition = new Vector3(0, 1f, 0);
        tronco.transform.localScale    = new Vector3(radio * 0.2f, 1f, radio * 0.2f);
        tronco.GetComponent<Renderer>().sharedMaterial = _matArbol;
        Destroy(tronco.GetComponent<Collider>());

        // Copa
        var copa = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        copa.transform.SetParent(go.transform);
        copa.transform.localPosition = new Vector3(0, 2.5f, 0);
        copa.transform.localScale    = Vector3.one * radio;
        var matCopa = new Material(_matArbol);
        matCopa.color = new Color(0.15f, 0.45f, 0.12f);
        copa.GetComponent<Renderer>().sharedMaterial = matCopa;
        Destroy(copa.GetComponent<Collider>());

        return go;
    }

    GameObject BuscarPrefabArbol()
    {
        // Intentar cargar de prefabs generados
        var guids = UnityEngine.Resources.FindObjectsOfTypeAll<GameObject>();
        return null; // fallback a procedural
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════════════

    void CrearParents()
    {
        _parentEdificios = new GameObject("Edificios_OSM").transform;
        _parentCalles    = new GameObject("Calles_OSM").transform;
        _parentArboles   = new GameObject("Arboles_OSM").transform;
    }

    void CargarMateriales()
    {
        // Buscar materiales HDRP existentes o crear fallbacks
        _matEdifResidencial = BuscarMaterial("beige_wall", "plaster", "concrete") ??
            CrearMatColor(new Color(0.85f, 0.82f, 0.75f), "M_Residencial");
        _matEdifComercial   = BuscarMaterial("brick", "red_brick") ??
            CrearMatColor(new Color(0.72f, 0.45f, 0.35f), "M_Comercial");
        _matEdifIndustrial  = BuscarMaterial("metal", "corrugated") ??
            CrearMatColor(new Color(0.55f, 0.58f, 0.60f), "M_Industrial");
        _matAsfalto  = BuscarMaterial("asphalt", "road") ??
            CrearMatColor(new Color(0.25f, 0.25f, 0.27f), "M_Asfalto");
        _matBordillo = BuscarMaterial("concrete", "pavement") ??
            CrearMatColor(new Color(0.70f, 0.70f, 0.68f), "M_Bordillo");
        _matAcera    = BuscarMaterial("cobblestone", "pavement", "tile") ??
            CrearMatColor(new Color(0.75f, 0.73f, 0.70f), "M_Acera");
        _matArbol    = BuscarMaterial("bark", "wood") ??
            CrearMatColor(new Color(0.38f, 0.25f, 0.12f), "M_Tronco");
    }

    Material BuscarMaterial(params string[] palabras)
    {
        // Obtener el array una sola vez — FindObjectsOfTypeAll es costoso
        var mats = Resources.FindObjectsOfTypeAll<Material>();
        foreach (var p in palabras)
            foreach (var m in mats)
                if (m != null && m.name.ToLower().Contains(p) && m.shader.name.Contains("HDRP"))
                    return m;
        return null;
    }

    Material CrearMatColor(Color color, string nombre)
    {
        var mat = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        mat.name = nombre;
        mat.color = color;
        return mat;
    }

    Material MaterialParaTipo(string tipo)
    {
        return tipo switch {
            "commercial" or "retail" or "office" => _matEdifComercial,
            "industrial" or "warehouse"           => _matEdifIndustrial,
            _                                     => _matEdifResidencial,
        };
    }

    // ── LOD Groups ────────────────────────────────────────────────────────
    void AnadirLOD(GameObject go, MeshRenderer mr, Mesh meshCompleto)
    {
        var lod = go.AddComponent<LODGroup>();
        lod.fadeMode = LODFadeMode.CrossFade;

        // LOD 0 — completo hasta 80m
        // LOD 1 — solo caja simplificada de 80-300m
        // LOD 2 — culled > 300m

        // Crear mesh simplificado (caja del AABB) para LOD 1
        var rendLOD1 = CrearRendererSimplificado(go, mr.sharedMaterial, meshCompleto.bounds);

        lod.SetLODs(new LOD[]
        {
            new LOD(0.04f, new Renderer[] { mr }),           // < 80m  (4% altura pantalla)
            new LOD(0.01f, new Renderer[] { rendLOD1 }),     // < 300m (1%)
            new LOD(0f,    new Renderer[0])                  // culled
        });
        lod.RecalculateBounds();

        // Desactivar el renderer LOD1 al principio (LODGroup lo gestiona)
        rendLOD1.enabled = false;
    }

    MeshRenderer CrearRendererSimplificado(GameObject parent, Material mat, Bounds b)
    {
        var go = new GameObject("LOD1_Box");
        go.transform.SetParent(parent.transform);
        go.transform.localPosition = b.center - parent.transform.position;
        go.transform.localScale    = b.size;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = CuboMeshUnitario();

        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return mr;
    }

    static Mesh _cuboCache;
    static Mesh CuboMeshUnitario()
    {
        if (_cuboCache != null) return _cuboCache;
        // Cubo 1×1×1 centrado en (0,0,0) — reutilizado para todos los LOD1
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cuboCache = go.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.Destroy(go);
        return _cuboCache;
    }

    static float AlturaTerreno(float x, float z) => GeoDataAlsasua.AlturaTerreno(x, z);

}

// ── Tipos de datos OSM — nivel superior para acceso desde cualquier script ──
[System.Serializable] public class EdificioData
{
    public int    id;
    public string type;
    public string name;
    public int    levels;
    public float  height;
    public Vert[] vertices;
    // Campos extendidos — presentes en buildings_final.json / buildings_completo.json
    public string material;         // building:material OSM
    public string roof_material;    // roof:material OSM
    public string roof_colour;      // roof:colour OSM (#RRGGBB o nombre)
    public string roof_shape;       // roof:shape OSM (gabled, flat, hipped…)
    public string colour;           // building:colour OSM
    public string amenity;          // amenity/uso
    public string addr_street;      // dirección
    // Campos del análisis fotogramétrico (buildings_rico.json / buildings_final.json)
    public int    roof_r_real, roof_g_real, roof_b_real;  // color real tejado (0-255)
    public string roof_tipo_real;   // cemento_gris_claro, pizarra_gris, etc.
    public float  mat_r, mat_g, mat_b;                    // color real pared (0-1)
}
[System.Serializable] public class Vert { public float x; public float z; }

[System.Serializable] public class RoadData
{
    public int    id;
    public string type;
    public string name;
    public float  width;
    public bool   oneway;
    public Vert[] points;
    // Campos extendidos — calles_superficie.json
    public string surface;     // surface OSM (asphalt, cobblestone, paving_stones…)
    public string sidewalk;    // sidewalk OSM
    public int    lanes;       // número de carriles
    public string maxspeed;    // velocidad máxima
}

[System.Serializable] public class ArbolData
{
    public float  x, z;
    public string especie;
    public float  radio;
}

// ── JSON Array helper ─────────────────────────────────────────────────────
public static class JsonHelper
{
    /// <summary>
    /// Parsea un array JSON raíz "[{...},{...}]" en T[].
    /// Usa el wrapper trick de JsonUtility (no requiere Newtonsoft).
    /// Los campos de T DEBEN estar anotados con [Serializable] y ser public.
    /// </summary>
    public static T[] ParseArray<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        // Normalizar: quitar BOM y espacios iniciales
        json = json.Trim().TrimStart('﻿');

        // Asegurarse de que es un array
        if (!json.StartsWith("["))
        {
            AlsasuaLogger.Warn("JsonHelper", "JSON no es un array raiz");
            return null;
        }

        try
        {
            // Wrapper trick compatible con JsonUtility
            string wrapped = "{\"items\":" + json + "}";
            var wrapper = JsonUtility.FromJson<Wrapper<T>>(wrapped);
            return wrapper?.items;
        }
        catch (Exception e)
        {
            AlsasuaLogger.Warn("JsonHelper", $"Error parseando JSON ({typeof(T).Name}): {e.Message}");
            return null;
        }
    }

    [Serializable] class Wrapper<T> { public T[] items; }
}
