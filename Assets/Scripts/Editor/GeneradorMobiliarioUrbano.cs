#if UNITY_EDITOR
// Assets/Scripts/Editor/GeneradorMobiliarioUrbano.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MOBILIARIO URBANO — farolas, árboles, papeleras, bancos, señales
//
//  Coloca mobiliario urbano realista a lo largo de las carreteras OSM.
//  MENÚ: Altsasu GTA → Territorio Real → ★ Mobiliario Urbano (farolas, árboles)
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class GeneradorMobiliarioUrbano
{
    const string ROADS_PATH = "Assets/AlsasuaData/roads_unity.json";

    static Terrain _terrain;
    static Material _matFarola, _matLampara, _matTronco, _matCopa, _matBanco;

    // MENÚ-LEGACY: [MenuItem("Altsasu GTA/Territorio Real/★ Mobiliario Urbano (farolas, árboles)", false, 4)]
    public static void Generar()
    {
        _terrain = Object.FindFirstObjectByType<Terrain>();
        if (_terrain == null)
        {
            EditorUtility.DisplayDialog("Sin terrain", "Crea primero el terrain.", "OK");
            return;
        }

        var antiguo = GameObject.Find("MobiliarioUrbano");
        if (antiguo != null) Undo.DestroyObjectImmediate(antiguo);

        CrearMateriales();

        var padre = new GameObject("MobiliarioUrbano");
        Undo.RegisterCreatedObjectUndo(padre, "Mobiliario");

        try
        {
            int farolas = ColocarFarolasEnCarreteras(padre.transform);
            int arboles = ColocarArbolesUrbanos(padre.transform);
            ColocarBancosEnPlaza(padre.transform);

            Debug.Log($"[Mobiliario] ✓ {farolas} farolas + {arboles} árboles colocados.");
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        EditorUtility.DisplayDialog("✅ Mobiliario urbano",
            "Generado en Altsasua:\n\n" +
            "• Farolas en todas las carreteras principales\n" +
            "• Árboles a lo largo de calles residenciales\n" +
            "• Bancos en Herriko Plaza", "OK");
    }

    // =========================================================================
    //  FAROLAS
    // =========================================================================

    // Cap global para no explotar Unity con miles de farolas (cada una = 4 GameObjects)
    const int MAX_FAROLAS = 2000;

    static int ColocarFarolasEnCarreteras(Transform padre)
    {
        var jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(ROADS_PATH);
        if (jsonAsset == null) return 0;

        var carpFarolas = new GameObject("Farolas");
        carpFarolas.transform.SetParent(padre);

        int count = 0;
        try
        {
            var root = JArray.Parse(jsonAsset.text);
            int total = root.Count;

            for (int idx = 0; idx < total; idx++)
            {
                // Cap global — paramos cuando hayamos colocado MAX_FAROLAS
                if (count >= MAX_FAROLAS)
                {
                    Debug.LogWarning($"[Mobiliario] Tope de {MAX_FAROLAS} farolas alcanzado — omitidas {total - idx} carreteras restantes.");
                    break;
                }

                // Progress + cancelación cada 10 carreteras
                if (idx % 10 == 0)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Farolas",
                            $"{idx}/{total} carreteras · {count} farolas", (float)idx / total))
                    {
                        Debug.LogWarning("[Mobiliario] Cancelado por el usuario.");
                        break;
                    }
                }

                var road = root[idx];
                string tipo = road["type"]?.Value<string>()
                           ?? road["highway"]?.Value<string>() ?? "residential";

                float separacion;
                bool ambosLados;
                switch (tipo.ToLower())
                {
                    // Motorway/trunk son intercity, no llevan farolas urbanas
                    case "motorway": case "motorway_link":
                    case "trunk":    case "trunk_link":
                        continue;
                    case "primary":
                        separacion = 50f; ambosLados = true; break;
                    case "secondary": case "tertiary":
                        separacion = 40f; ambosLados = false; break;
                    case "residential":
                        separacion = 45f; ambosLados = false; break;
                    default: continue; // sin farolas en footways/service
                }

                var pts = road["pts"] as JArray;
                if (pts == null || pts.Count < 2) continue;

                // Detectar formato (flat / objeto / par) — igual que GeneradorInfraestructura
                Vector3[] puntos;
                if (pts[0] is Newtonsoft.Json.Linq.JObject)
                {
                    puntos = new Vector3[pts.Count];
                    for (int i = 0; i < pts.Count; i++)
                        puntos[i] = new Vector3(
                            pts[i]["x"]?.Value<float>() ?? 0, 0,
                            pts[i]["z"]?.Value<float>() ?? 0);
                }
                else if (pts[0] is Newtonsoft.Json.Linq.JArray)
                {
                    puntos = new Vector3[pts.Count];
                    for (int i = 0; i < pts.Count; i++)
                    {
                        var par = pts[i] as Newtonsoft.Json.Linq.JArray;
                        puntos[i] = new Vector3(par[0].Value<float>(), 0, par[1].Value<float>());
                    }
                }
                else
                {
                    int n = pts.Count / 2;
                    if (n < 2) continue;
                    puntos = new Vector3[n];
                    for (int i = 0; i < n; i++)
                        puntos[i] = new Vector3(
                            pts[i*2].Value<float>(), 0, pts[i*2+1].Value<float>());
                }

                count += ColocarFarolasEnLinea(puntos, separacion, ambosLados, carpFarolas.transform);
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        return count;
    }

    static int ColocarFarolasEnLinea(Vector3[] puntos, float separacion, bool ambosLados, Transform padre)
    {
        int count = 0;
        float acumulado = separacion * 0.5f;

        for (int i = 0; i < puntos.Length - 1; i++)
        {
            Vector3 a = puntos[i];
            Vector3 b = puntos[i + 1];
            float dist = Vector3.Distance(a, b);
            Vector3 dir = (b - a).normalized;
            Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized;

            while (acumulado < dist)
            {
                Vector3 p = Vector3.Lerp(a, b, acumulado / dist);
                p += perp * 3f; // 3m al lado de la calzada
                p.y = _terrain.SampleHeight(p);
                CrearFarola(p, padre);
                count++;

                if (ambosLados)
                {
                    Vector3 p2 = p - perp * 6f;
                    p2.y = _terrain.SampleHeight(p2);
                    CrearFarola(p2, padre);
                    count++;
                }

                acumulado += separacion;
            }
            acumulado -= dist;
        }
        return count;
    }

    // Meshes compartidas — se cachean una vez, se reusan para todas las farolas.
    // Crear con GameObject.CreatePrimitive() + DestroyImmediate(Collider) era ~30× más lento.
    static Mesh _meshCilindro;
    static Mesh _meshEsfera;

    static Mesh ObtenerMeshCompartida(PrimitiveType tipo, ref Mesh cache)
    {
        if (cache != null) return cache;
        var tmp = GameObject.CreatePrimitive(tipo);
        cache = tmp.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(tmp);
        return cache;
    }

    static void CrearFarola(Vector3 pos, Transform padre)
    {
        var meshCil = ObtenerMeshCompartida(PrimitiveType.Cylinder, ref _meshCilindro);
        var meshEsf = ObtenerMeshCompartida(PrimitiveType.Sphere,   ref _meshEsfera);

        var root = new GameObject("Farola");
        root.transform.SetParent(padre);
        root.transform.position = pos;

        AñadirMesh(root.transform, "Poste",   meshCil, _matFarola,
                   new Vector3(0, 3f, 0),     Quaternion.identity,
                   new Vector3(0.15f, 3f, 0.15f));
        AñadirMesh(root.transform, "Brazo",   meshCil, _matFarola,
                   new Vector3(0.5f, 6f, 0),  Quaternion.Euler(0, 0, 90f),
                   new Vector3(0.08f, 0.5f, 0.08f));
        AñadirMesh(root.transform, "Lampara", meshEsf, _matLampara,
                   new Vector3(1f, 6f, 0),    Quaternion.identity,
                   new Vector3(0.4f, 0.4f, 0.4f));

        // NOTA: ya no creamos Light component aquí — 2000 Point Lights son una losa para HDRP.
        // Un sistema runtime debe activarlas solo en las cercanas al jugador (LOD lights).
    }

    static void AñadirMesh(Transform padre, string nombre, Mesh mesh, Material mat,
                           Vector3 lp, Quaternion lr, Vector3 ls)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre);
        go.transform.localPosition = lp;
        go.transform.localRotation = lr;
        go.transform.localScale    = ls;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    // =========================================================================
    //  ÁRBOLES URBANOS
    // =========================================================================

    static int ColocarArbolesUrbanos(Transform padre)
    {
        var jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(ROADS_PATH);
        if (jsonAsset == null) return 0;

        var carpArboles = new GameObject("Arboles");
        carpArboles.transform.SetParent(padre);

        const int MAX_ARBOLES = 1500;
        int count = 0;
        try
        {
            var root = JArray.Parse(jsonAsset.text);
            int total = root.Count;

            for (int idx = 0; idx < total; idx++)
            {
                if (count >= MAX_ARBOLES) break;
                if (idx % 10 == 0 && EditorUtility.DisplayCancelableProgressBar(
                        "Árboles urbanos", $"{idx}/{total} · {count} árboles",
                        (float)idx / total))
                    break;

                var road = root[idx];
                string tipo = road["type"]?.Value<string>()
                           ?? road["highway"]?.Value<string>() ?? "";
                if (tipo != "residential" && tipo != "secondary" && tipo != "tertiary") continue;

                var pts = road["pts"] as JArray;
                if (pts == null || pts.Count < 2) continue;

                Vector3[] puntos = ConvertirPts(pts);
                if (puntos == null || puntos.Length < 2) continue;

                for (int i = 1; i < puntos.Length && count < MAX_ARBOLES; i++)
                {
                    Vector3 pAnt = puntos[i - 1];
                    Vector3 pAct = puntos[i];
                    float dist = Vector3.Distance(pAnt, pAct);
                    if (dist < 1f) continue;
                    Vector3 dir  = (pAct - pAnt).normalized;
                    Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized;

                    int num = Mathf.FloorToInt(dist / 18f);
                    for (int t = 0; t < num && count < MAX_ARBOLES; t++)
                    {
                        float f = (t + 0.5f) / num;
                        Vector3 p = Vector3.Lerp(pAnt, pAct, f) + perp * 4f;
                        p.y = _terrain.SampleHeight(p);
                        CrearArbol(p, carpArboles.transform);
                        count++;
                    }
                }
            }
        }
        catch (System.Exception e) { Debug.LogWarning("[Mobiliario] Árboles: " + e.Message); }

        return count;
    }

    // Convierte pts JSON (3 formatos) a Vector3[]. Devuelve null si no hay puntos válidos.
    static Vector3[] ConvertirPts(JArray pts)
    {
        if (pts == null || pts.Count == 0) return null;
        Vector3[] res;
        if (pts[0] is Newtonsoft.Json.Linq.JObject)
        {
            res = new Vector3[pts.Count];
            for (int i = 0; i < pts.Count; i++)
                res[i] = new Vector3(
                    pts[i]["x"]?.Value<float>() ?? 0, 0,
                    pts[i]["z"]?.Value<float>() ?? 0);
        }
        else if (pts[0] is Newtonsoft.Json.Linq.JArray)
        {
            res = new Vector3[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                var par = pts[i] as Newtonsoft.Json.Linq.JArray;
                res[i] = new Vector3(par[0].Value<float>(), 0, par[1].Value<float>());
            }
        }
        else
        {
            int n = pts.Count / 2;
            if (n < 2) return null;
            res = new Vector3[n];
            for (int i = 0; i < n; i++)
                res[i] = new Vector3(pts[i*2].Value<float>(), 0, pts[i*2+1].Value<float>());
        }
        return res;
    }

    static void CrearArbol(Vector3 pos, Transform padre)
    {
        var meshCil = ObtenerMeshCompartida(PrimitiveType.Cylinder, ref _meshCilindro);
        var meshEsf = ObtenerMeshCompartida(PrimitiveType.Sphere,   ref _meshEsfera);

        var root = new GameObject("Arbol");
        root.transform.SetParent(padre);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
        root.transform.localScale = Vector3.one * Random.Range(0.85f, 1.15f);

        AñadirMesh(root.transform, "Tronco", meshCil, _matTronco,
                   new Vector3(0, 1.5f, 0), Quaternion.identity,
                   new Vector3(0.3f, 1.5f, 0.3f));
        AñadirMesh(root.transform, "Copa",   meshEsf, _matCopa,
                   new Vector3(0, 4.5f, 0), Quaternion.identity,
                   new Vector3(2.5f, 2.8f, 2.5f));
    }

    // =========================================================================
    //  BANCOS EN PLAZA
    // =========================================================================

    static void ColocarBancosEnPlaza(Transform padre)
    {
        var carpBancos = new GameObject("Bancos");
        carpBancos.transform.SetParent(padre);

        Vector3 plaza = new Vector3(1918f, 0, 8570f);
        for (int i = 0; i < 8; i++)
        {
            float angle = i / 8f * Mathf.PI * 2f;
            Vector3 p = plaza + new Vector3(
                Mathf.Cos(angle) * 18f, 0, Mathf.Sin(angle) * 18f);
            p.y = _terrain.SampleHeight(p);
            CrearBanco(p, angle * Mathf.Rad2Deg + 90f, carpBancos.transform);
        }
    }

    static void CrearBanco(Vector3 pos, float rotY, Transform padre)
    {
        var root = new GameObject("Banco");
        root.transform.SetParent(padre);
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0, rotY, 0);

        // Asiento
        var asiento = GameObject.CreatePrimitive(PrimitiveType.Cube);
        asiento.name = "Asiento";
        asiento.transform.SetParent(root.transform);
        asiento.transform.localPosition = new Vector3(0, 0.45f, 0);
        asiento.transform.localScale    = new Vector3(1.8f, 0.1f, 0.45f);
        asiento.GetComponent<Renderer>().sharedMaterial = _matBanco;

        // Respaldo
        var resp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        resp.name = "Respaldo";
        resp.transform.SetParent(root.transform);
        resp.transform.localPosition = new Vector3(0, 0.85f, -0.2f);
        resp.transform.localScale    = new Vector3(1.8f, 0.7f, 0.08f);
        resp.GetComponent<Renderer>().sharedMaterial = _matBanco;
    }

    // =========================================================================
    //  MATERIALES
    // =========================================================================

    static void CrearMateriales()
    {
        var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");

        _matFarola  = Mat(sh, new Color(0.18f, 0.18f, 0.22f), "Mat_Farola", 0.4f, 0.6f);
        _matLampara = Mat(sh, new Color(1.0f,  0.92f, 0.65f), "Mat_Lampara", 0.6f, 0f);
        _matLampara.SetColor("_EmissiveColor", new Color(1.5f, 1.3f, 0.6f));
        _matLampara.EnableKeyword("_EMISSION");

        _matTronco = Mat(sh, new Color(0.32f, 0.22f, 0.15f), "Mat_Tronco", 0.05f, 0f);
        _matCopa   = Mat(sh, new Color(0.25f, 0.45f, 0.18f), "Mat_Copa",   0.05f, 0f);
        _matBanco  = Mat(sh, new Color(0.55f, 0.40f, 0.25f), "Mat_Banco",  0.15f, 0f);
    }

    static Material Mat(Shader sh, Color color, string nombre, float smooth, float metal)
    {
        var m = new Material(sh) { name = nombre };
        m.SetColor("_BaseColor", color);
        m.SetColor("_Color",     color);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic",   metal);
        return m;
    }
}
#endif
