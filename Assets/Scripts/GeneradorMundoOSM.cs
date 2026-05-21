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
    // Herriko Plaza en coordenadas OSM: lat=42.9006, lon=-2.1667
    // En coordenadas Unity: X=1918, Z=8570
    const float OFFSET_X = 1918f;
    const float OFFSET_Z = 8570f;

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

        // Esperar al terreno y a SistemaZonas
        float t = 0;
        while ((Terrain.activeTerrain == null || SistemaZonas.Instance == null) && t < 8f)
            { t += 0.5f; yield return new WaitForSeconds(0.5f); }

        CargarMateriales();

        // Parent para árboles (los únicos que se construyen directamente aquí)
        if (_parentArboles == null) _parentArboles = new GameObject("Arboles_OSM").transform;

        // ── FASE 1: Indexar edificios en zonas (sin construir meshes) ─────
        yield return StartCoroutine(IndexarEdificios());
        yield return null;

        // ── FASE 2: Indexar calles en zonas ──────────────────────────────
        yield return StartCoroutine(IndexarCalles());
        yield return null;

        // ── FASE 3: Árboles (pocos, construir directamente) ──────────────
        yield return StartCoroutine(GenerarArboles());

        // Avisar a SistemaZonas que el índice está completo
        SistemaZonas.Instance?.MarcarIndexadoListo();

        MundoListo = true;
        OnMundoGenerado?.Invoke();
        AlsasuaLogger.Info("OSM",
            $"✅ Índice OSM listo: {_edificiosCreados} edificios, {_callesCreadas} tramos indexados");
    }

    IEnumerator IndexarEdificios()
    {
        string path = Path.Combine(Application.dataPath, "AlsasuaData", "buildings_unity.json");
        if (!File.Exists(path)) { AlsasuaLogger.Warn("OSM", "buildings_unity.json no encontrado"); yield break; }

        string json = File.ReadAllText(path);
        var edificios = JsonHelper.ParseArray<EdificioData>(json);
        if (edificios == null) yield break;

        int lote = 0;
        foreach (var e in edificios)
        {
            if (e.vertices == null || e.vertices.Length < 3) continue;

            // Calcular centro del edificio para asignarlo a la zona correcta
            float cx = 0, cz = 0;
            foreach (var v in e.vertices) { cx += v.x + OFFSET_X; cz += v.z + OFFSET_Z; }
            cx /= e.vertices.Length; cz /= e.vertices.Length;

            SistemaZonas.Instance?.RegistrarEdificio(e, new Vector3(cx, 0, cz));
            _edificiosCreados++;
            if (++lote >= 60) { lote = 0; yield return null; }
        }
    }

    IEnumerator IndexarCalles()
    {
        string path = Path.Combine(Application.dataPath, "AlsasuaData", "roads_unity.json");
        if (!File.Exists(path)) yield break;

        string json = File.ReadAllText(path);
        var roads = JsonHelper.ParseArray<RoadData>(json);
        if (roads == null) yield break;

        int lote = 0;
        foreach (var r in roads)
        {
            if (r.points == null || r.points.Length < 2) continue;

            // Centro del tramo
            float cx = (r.points[0].x + r.points[^1].x) * 0.5f + OFFSET_X;
            float cz = (r.points[0].z + r.points[^1].z) * 0.5f + OFFSET_Z;

            SistemaZonas.Instance?.RegistrarCalle(r, new Vector3(cx, 0, cz));
            _callesCreadas++;
            if (++lote >= 40) { lote = 0; yield return null; }
        }
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

        float suelo  = AlturaTerreno(cx, cz);
        float altura = Mathf.Max(3.2f, e.height > 0 ? e.height : e.levels * 3.2f);

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

    /// <summary>Construye el mesh de un tramo de calle y lo añade bajo <paramref name="parent"/>.</summary>
    public void ConstruirCalle(RoadData r, Transform parent)
    {
        float ancho = Mathf.Max(2f, r.width);
        var pts = Array.ConvertAll(r.points, p =>
            new Vector3(p.x + OFFSET_X, 0, p.z + OFFSET_Z));
        for (int i = 0; i < pts.Length; i++)
            pts[i].y = AlturaTerreno(pts[i].x, pts[i].z) + 0.03f;

        var mesh = GenerarMeshCalle(pts, ancho);
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

    Mesh GenerarMeshEdificio(List<Vector2> planta, float suelo, float altura)
    {
        int n = planta.Count;
        if (n < 3) return null;

        var mesh  = new Mesh { name = "Edificio" };
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        var uvs   = new List<Vector2>();

        // Paredes laterales
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            var p0 = new Vector3(planta[i].x, suelo,          planta[i].y);
            var p1 = new Vector3(planta[j].x, suelo,          planta[j].y);
            var p2 = new Vector3(planta[j].x, suelo + altura, planta[j].y);
            var p3 = new Vector3(planta[i].x, suelo + altura, planta[i].y);

            float u = Vector2.Distance(planta[i], planta[j]) / 4f;
            int b = verts.Count;
            verts.AddRange(new[]{p0,p1,p2,p3});
            uvs.AddRange(new[]{new Vector2(0,0),new Vector2(u,0),new Vector2(u,1),new Vector2(0,1)});
            tris.AddRange(new[]{b,b+2,b+1, b,b+3,b+2});
        }

        // Techo (triangulación simple fan)
        int techoBase = verts.Count;
        foreach (var v in planta)
            verts.Add(new Vector3(v.x, suelo + altura, v.y));
        uvs.AddRange(planta.ConvertAll(v => new Vector2(v.x * 0.1f, v.y * 0.1f)));
        for (int i = 1; i < n - 1; i++)
            tris.AddRange(new[]{techoBase, techoBase + i, techoBase + i + 1});

        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CALLES
    // ════════════════════════════════════════════════════════════════════════


    Mesh GenerarMeshCalle(Vector3[] pts, float ancho)
    {
        int n = pts.Length;
        if (n < 2) return null;

        var verts = new List<Vector3>();
        var tris  = new List<int>();
        var uvs   = new List<Vector2>();
        float uAcum = 0f;

        for (int i = 0; i < n; i++)
        {
            Vector3 dir;
            if (i == 0)         dir = (pts[1] - pts[0]).normalized;
            else if (i == n-1)  dir = (pts[i] - pts[i-1]).normalized;
            else                dir = ((pts[i+1] - pts[i-1]) * 0.5f).normalized;

            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized * (ancho * 0.5f);
            verts.Add(pts[i] - right);
            verts.Add(pts[i] + right);

            if (i > 0) uAcum += Vector3.Distance(pts[i], pts[i-1]);
            uvs.Add(new Vector2(0, uAcum / ancho));
            uvs.Add(new Vector2(1, uAcum / ancho));

            if (i > 0)
            {
                int b = (i-1)*2;
                tris.AddRange(new[]{b, b+2, b+1,  b+1, b+2, b+3});
            }
        }

        var mesh = new Mesh { name = "Calle" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

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
        foreach (var p in palabras)
        {
            var mats = Resources.FindObjectsOfTypeAll<Material>();
            foreach (var m in mats)
                if (m != null && m.name.ToLower().Contains(p) && m.shader.name.Contains("HDRP"))
                    return m;
        }
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

    float AlturaTerreno(float x, float z)
    {
        var t = Terrain.activeTerrain;
        if (t != null) return t.SampleHeight(new Vector3(x, 0, z)) + t.transform.position.y;
        if (Physics.Raycast(new Vector3(x, 1000, z), Vector3.down, out var h, 2000))
            return h.point.y;
        return 240f;
    }

    // ── Data types ────────────────────────────────────────────────────────

    [Serializable] public class EdificioData
    {
        public int    id;
        public string type;
        public string name;
        public int    levels;
        public float  height;
        public Vert[] vertices;
    }
    [Serializable] public class Vert { public float x; public float z; }

    [Serializable] public class RoadData
    {
        public int    id;
        public string type;
        public string name;
        public float  width;
        public bool   oneway;
        public Vert[] points;
    }

    [Serializable] public class ArbolData
    {
        public float  x, z;
        public string especie;
        public float  radio;
    }
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
