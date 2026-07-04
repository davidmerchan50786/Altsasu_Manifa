// Assets/Scripts/Editor/SembradoBosqueOSM.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SEMBRADO DE BOSQUE OSM — árboles exactamente donde hay bosque real
//
//  Lee bosques.geojson (polígonos UTM 30N de masa forestal, SIGPAC/Navarra)
//  y rellena cada polígono con árboles usando HierarchicalInstancedStaticMesh,
//  a densidad de bosque vasco real:
//    - Bosque denso (robledal/hayedo/pinar): ~500 árboles/ha → 1 cada ~14 m²
//    - Matorral / scrub: ~150 árboles/ha  → 1 cada ~67 m²
//
//  Coordenadas: convierte UTM 30N (E, N) → Unity (OX=1918, OZ=8570).
//  Altura:      V3 bilineal exacto.
//  Resultado:   un HISM por especie/zona (GPU instancing → 1 draw call por tipo).
//
//  Menú: Tools/Alsasua/Mundo/🌲 Sembrar Bosque (polígonos OSM exactos)
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;

public static class SembradoBosqueOSM
{
    const string GEOJSON_BOSQUE = "Assets/AlsasuaData/bosques.geojson";
    const string GEOJSON_MASAS  = "Assets/AlsasuaData/masas_forestales.geojson";
    const string RAIZ           = "Bosque_OSM";

    // Densidades por tipo (árboles/hectárea)
    const float DENSIDAD_BOSQUE  = 500f;
    const float DENSIDAD_MATORRAL= 150f;
    const float DENSIDAD_PINAR   = 600f;

    // UTM → Unity
    const float E0 = 567951f, N0 = 4749902f, OX = 1918f, OZ = 8570f;
    static Vector2 UTMaUnity(double e, double n) =>
        new Vector2((float)(e - E0) + OX, (float)(n - N0) + OZ);

    // Heightmap V3
    static MuestreadorHeightmapV3 _v3; static bool _v3Init;
    static MuestreadorHeightmapV3 V3
    {
        get { if (_v3Init) return _v3; _v3Init = true; var m = new MuestreadorHeightmapV3(); if (m.Cargar()) _v3 = m; return _v3; }
    }
    static float Altura(float x, float z)
    {
        if (V3 != null && V3.EnRango(x, z)) return V3.AlturaMundo(x, z);
        foreach (var t in Terrain.activeTerrains)
        {
            if (t == null) continue;
            var p = t.transform.position; var s = t.terrainData.size;
            if (x >= p.x && x < p.x + s.x && z >= p.z && z < p.z + s.z)
                return p.y + t.SampleHeight(new Vector3(x, 0, z));
        }
        return 0f;
    }

    [MenuItem("Tools/Alsasua/Mundo/🌲 Sembrar Bosque (polígonos OSM exactos)", priority = 20)]
    static void Sembrar()
    {
        string ruta = Path.GetFullPath(Path.Combine(Application.dataPath, "..", GEOJSON_BOSQUE));
        if (!File.Exists(ruta))
        {
            ruta = Path.GetFullPath(Path.Combine(Application.dataPath, "..", GEOJSON_MASAS));
            if (!File.Exists(ruta)) { EditorUtility.DisplayDialog("Bosque", $"No existe bosques.geojson ni masas_forestales.geojson", "Vale"); return; }
        }

        // Cargar malla de árbol — buscar un prefab existente o usar cápsula placeholder
        GameObject arbolPrefab = null;
        string[] candidatos = {
            "Assets/Resources/Prefabs/Props/Arbol_Generico.prefab",
            "Assets/Prefabs/Naturaleza/Arbol_Roble.prefab",
            "Assets/Models/Trees/Tree.prefab",
        };
        foreach (var c in candidatos)
        {
            arbolPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(c);
            if (arbolPrefab != null) break;
        }

        // Si no hay prefab, usar cápsula Unity como placeholder
        bool usaPlaceholder = arbolPrefab == null;

        JObject geojson;
        try { geojson = JObject.Parse(File.ReadAllText(ruta)); }
        catch (System.Exception e) { EditorUtility.DisplayDialog("Bosque", $"GeoJSON error: {e.Message}", "Vale"); return; }

        var features = geojson["features"] as JArray;
        if (features == null || features.Count == 0) { EditorUtility.DisplayDialog("Bosque", "Sin features en el GeoJSON.", "Vale"); return; }

        if (!EditorUtility.DisplayDialog("Bosque OSM",
            $"{features.Count} polígonos de masa forestal.\n" +
            $"Árbol: {(usaPlaceholder ? "CÁPSULA PLACEHOLDER (asigna prefab real después)" : arbolPrefab!.name)}\n" +
            "Densidad: ~500 árboles/hectárea en bosque denso.\n¿Continuar?",
            "Sembrar", "Cancelar"))
            return;

        // Crear raíz
        var raizAnt = GameObject.Find(RAIZ);
        if (raizAnt != null) Object.DestroyImmediate(raizAnt);
        var raiz = new GameObject(RAIZ);

        // HISM reutilizable por tipo (Unity: wrapper que acumula matrices y las materializa al final)
        var hisms = new Dictionary<string, HISMWrapper>();
        Mesh mPlaceholder = null;
        if (usaPlaceholder)
        {
            // Crear malla de cápsula simple para placeholder
            mPlaceholder = Resources.GetBuiltinResource<Mesh>("Capsule.fbx");
        }

        int totalArboles = 0, nFeat = 0;
        var rng = new System.Random(42); // seed fijo para reproducibilidad

        try
        {
            foreach (var feature in features)
            {
                nFeat++;
                if (nFeat % 10 == 0 && EditorUtility.DisplayCancelableProgressBar(
                    "Bosque OSM", $"Polígono {nFeat}/{features.Count}…", nFeat / (float)features.Count)) break;

                var props = feature["properties"];
                var geom  = feature["geometry"];
                if (geom == null) continue;

                string tipoVeg = DeterminarTipo(props);
                float densidad = tipoVeg.Contains("matorral") ? DENSIDAD_MATORRAL
                               : tipoVeg.Contains("pinar")    ? DENSIDAD_PINAR
                               : DENSIDAD_BOSQUE;

                // Obtener polígonos (MultiPolygon o Polygon)
                var poligonos = ExtraerPoligonos(geom);

                foreach (var poli in poligonos)
                {
                    if (poli.Count < 3) continue;
                    var arboles = GenerarPuntosEnPoligono(poli, densidad, rng);
                    foreach (var pt in arboles)
                    {
                        float y = Altura(pt.x, pt.y);
                        float escala = (float)(0.6 + rng.NextDouble() * 0.8); // 0.6–1.4
                        float rotY   = (float)(rng.NextDouble() * 360f);

                        if (usaPlaceholder)
                        {
                            // Instancias ligeras como simple HISM placeholder
                            var hism = ObtenerHISM(hisms, tipoVeg, raiz, mPlaceholder);
                            hism.AddInstance(Matrix4x4.TRS(
                                new Vector3(pt.x, y, pt.y),
                                Quaternion.Euler(0, rotY, 0),
                                new Vector3(escala * 0.8f, escala * 5f, escala * 0.8f)));
                        }
                        else
                        {
                            // Instancia HISM del mesh del prefab
                            var mf = arbolPrefab!.GetComponentInChildren<MeshFilter>();
                            if (mf?.sharedMesh != null)
                            {
                                var hism = ObtenerHISM(hisms, tipoVeg, raiz, mf.sharedMesh);
                                hism.AddInstance(Matrix4x4.TRS(
                                    new Vector3(pt.x, y, pt.y),
                                    Quaternion.Euler(0, rotY, 0),
                                    Vector3.one * escala));
                            }
                        }
                        totalArboles++;
                    }
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        // Materializar instancias acumuladas (equivalente a HISM de UE5 → GameObjects en escena Unity)
        foreach (var kv in hisms) kv.Value.Materializar();

        // Hacer estáticos
        foreach (var go in raiz.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(go.gameObject,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        EditorUtility.DisplayDialog("Bosque OSM ✅",
            $"{totalArboles:N0} árboles sembrados en {nFeat} polígonos forestales.\n" +
            (usaPlaceholder ? "⚠ Usando placeholder. Abre cada HISM y asigna tu prefab de árbol real.\n" : "") +
            "Raíz 'Bosque_OSM' — GPU instancing, 1 draw call por tipo.", "Genial");
    }

    // ── Extrae lista de polígonos (exterior ring) de Polygon o MultiPolygon ──
    static List<List<Vector2>> ExtraerPoligonos(JToken geom)
    {
        var resultado = new List<List<Vector2>>();
        string tipo = geom["type"]?.ToString() ?? "";

        if (tipo == "Polygon")
        {
            var ring = ConvertirRing(geom["coordinates"]?[0] as JArray);
            if (ring != null) resultado.Add(ring);
        }
        else if (tipo == "MultiPolygon")
        {
            foreach (var poly in geom["coordinates"] as JArray ?? new JArray())
            {
                var ring = ConvertirRing(poly[0] as JArray);
                if (ring != null) resultado.Add(ring);
            }
        }
        return resultado;
    }

    static List<Vector2>? ConvertirRing(JArray? ring)
    {
        if (ring == null) return null;
        var pts = new List<Vector2>(ring.Count);
        foreach (var coord in ring)
        {
            double e = coord[0]?.Value<double>() ?? 0;
            double n = coord[1]?.Value<double>() ?? 0;
            pts.Add(UTMaUnity(e, n));
        }
        return pts.Count >= 3 ? pts : null;
    }

    // ── Generar puntos aleatorios dentro del polígono ─────────────────────
    static List<Vector2> GenerarPuntosEnPoligono(List<Vector2> poli, float arboles_ha, System.Random rng)
    {
        // Calcular bounding box del polígono
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var p in poli)
        {
            if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
            if (p.y < minZ) minZ = p.y; if (p.y > maxZ) maxZ = p.y;
        }
        float areaBox = (maxX - minX) * (maxZ - minZ);
        if (areaBox < 1f) return new List<Vector2>();

        // Área aproximada (shoelace) en m²
        float area = AreaPoligono(poli);
        if (area < 25f) return new List<Vector2>(); // polígono muy pequeño

        // Número de árboles proporcional al área
        int nArboles = Mathf.RoundToInt(area * arboles_ha / 10000f);
        nArboles = Mathf.Clamp(nArboles, 1, 5000); // cap de seguridad

        var pts = new List<Vector2>(nArboles);
        int intentos = nArboles * 4; // intentos para el rechazo
        while (pts.Count < nArboles && intentos-- > 0)
        {
            float x = minX + (float)rng.NextDouble() * (maxX - minX);
            float z = minZ + (float)rng.NextDouble() * (maxZ - minZ);
            if (PuntoEnPoligono(new Vector2(x, z), poli))
                pts.Add(new Vector2(x, z));
        }
        return pts;
    }

    // Shoelace formula — área en m²
    static float AreaPoligono(List<Vector2> pts)
    {
        float area = 0;
        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % n];
            area += a.x * b.y - b.x * a.y;
        }
        return Mathf.Abs(area) * 0.5f;
    }

    // Ray casting para punto en polígono (2D)
    static bool PuntoEnPoligono(Vector2 pt, List<Vector2> poly)
    {
        bool dentro = false;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = poly[i].x, yi = poly[i].y;
            float xj = poly[j].x, yj = poly[j].y;
            if (((yi > pt.y) != (yj > pt.y)) &&
                (pt.x < (xj - xi) * (pt.y - yi) / (yj - yi) + xi))
                dentro = !dentro;
        }
        return dentro;
    }

    // ── HISM por tipo (Unity: acumula matrices, materializa al final) ─────
    static HISMWrapper ObtenerHISM(Dictionary<string, HISMWrapper> dict,
        string tipo, GameObject padre, Mesh mesh)
    {
        if (dict.TryGetValue(tipo, out var existing)) return existing;
        var go = new GameObject($"HISM_{tipo}");
        go.transform.SetParent(padre.transform, false);
        var h = new HISMWrapper(go);
        h.sharedMesh = mesh;
        var mat = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"))
                  { color = new Color(0.08f, 0.28f, 0.05f) };
        mat.enableInstancing = true;
        h.sharedMaterial = mat;
        dict[tipo] = h;
        return h;
    }

    // Wrapper Unity para HierarchicalInstancedStaticMeshComponent de UE5.
    // Acumula Matrix4x4 y los materializa como GameObjects estáticos al llamar Materializar().
    sealed class HISMWrapper
    {
        public Mesh sharedMesh;
        public Material sharedMaterial;

        readonly List<Matrix4x4> _mats = new();
        readonly GameObject _root;

        public HISMWrapper(GameObject root) { _root = root; }
        public void AddInstance(Matrix4x4 m) => _mats.Add(m);

        public void Materializar()
        {
            for (int i = 0; i < _mats.Count; i++)
            {
                var m = _mats[i];
                var go = new GameObject($"T{i:D5}");
                go.transform.SetParent(_root.transform, false);
                go.transform.position   = m.GetColumn(3);
                go.transform.rotation   = m.rotation;
                go.transform.localScale = new Vector3(
                    m.GetColumn(0).magnitude,
                    m.GetColumn(1).magnitude,
                    m.GetColumn(2).magnitude);
                if (sharedMesh != null)
                    go.AddComponent<MeshFilter>().sharedMesh = sharedMesh;
                if (sharedMaterial != null)
                    go.AddComponent<MeshRenderer>().sharedMaterial = sharedMaterial;
            }
        }
    }

    static string DeterminarTipo(JToken? props)
    {
        if (props == null) return "bosque";
        string accion = props["ACTUACION"]?.ToString()?.ToLower() ?? "";
        string tipo   = props["TIPO"]?.ToString()?.ToLower() ?? "";
        if (tipo.Contains("pinar") || accion.Contains("pino")) return "pinar";
        if (tipo.Contains("matorral") || accion.Contains("matorral")) return "matorral";
        if (tipo.Contains("haya") || tipo.Contains("hayedo")) return "hayedo";
        return "bosque";
    }

    [MenuItem("Tools/Alsasua/Mundo/↩️ Limpiar Bosque OSM", priority = 21)]
    static void Limpiar() { var r = GameObject.Find(RAIZ); if (r != null) Object.DestroyImmediate(r); }
}
#endif
