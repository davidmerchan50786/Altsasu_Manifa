#if UNITY_EDITOR
// Assets/Scripts/Editor/GeneradorInfraestructura.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GENERADOR DE INFRAESTRUCTURA — pone TODO sobre el mapa
//
//  Genera sobre el terrain:
//   • Autovía A-10 / N-1 (asfalto oscuro, 4 carriles)
//   • Carreteras secundarias (asfalto gris)
//   • Vías del tren con balasto y traviesas
//   • Río Arakil (azul)
//   • Estación de tren
//   • Puentes sobre el río
//
//  MENÚ: Altsasu GTA → Territorio Real → ★ Generar Infraestructura Completa
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class GeneradorInfraestructura
{
    const float ALTURA_OFFSET = 0.15f; // levanta carreteras 15cm sobre el suelo

    static Terrain _terrain;

    // MENÚ-LEGACY: [MenuItem("Altsasu GTA/Territorio Real/★ Generar Infraestructura Completa", false, 2)]
    public static void GenerarTodo()
    {
        _terrain = Object.FindFirstObjectByType<Terrain>();
        if (_terrain == null)
        {
            EditorUtility.DisplayDialog("Sin terrain",
                "Primero crea el terrain:\nAltsasu GTA → Territorio Real → ★ Crear Terrain + Ortofoto",
                "OK");
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Infraestructura", "Limpiando antiguos...", 0.05f);
            LimpiarInfraestructuraAntigua();

            EditorUtility.DisplayProgressBar("Infraestructura", "Generando autovía N-1/A-10...", 0.2f);
            GenerarAutovia();

            EditorUtility.DisplayProgressBar("Infraestructura", "Generando carreteras OSM...", 0.4f);
            GenerarCarreterasOSM();

            EditorUtility.DisplayProgressBar("Infraestructura", "Generando vías del tren...", 0.6f);
            GenerarViasTren();

            EditorUtility.DisplayProgressBar("Infraestructura", "Generando río Arakil...", 0.75f);
            GenerarRio();

            EditorUtility.DisplayProgressBar("Infraestructura", "Estación de tren...", 0.85f);
            GenerarEstacion();

            EditorUtility.DisplayProgressBar("Infraestructura", "Guardando...", 0.95f);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorUtility.DisplayDialog("✅ Infraestructura completa",
            "Generado sobre el terrain:\n\n" +
            "• Autovía N-1/A-10 (asfalto)\n" +
            "• Carreteras OSM reales\n" +
            "• Vías de tren con traviesas\n" +
            "• Río Arakil\n" +
            "• Estación de tren\n\n" +
            "Pulsa ▶ Play.", "OK");
    }

    static void LimpiarInfraestructuraAntigua()
    {
        var nombres = new[] { "Infraestructura_Altsasu", "Autovia_N1",
                              "Carreteras_OSM", "Vias_Tren", "Rio_Arakil", "Estacion_Tren" };
        foreach (var n in nombres)
        {
            var go = GameObject.Find(n);
            if (go != null) Undo.DestroyObjectImmediate(go);
        }
    }

    // =========================================================================
    //  AUTOVÍA N-1 / A-10
    // =========================================================================

    static void GenerarAutovia()
    {
        var padre = new GameObject("Autovia_N1");
        Undo.RegisterCreatedObjectUndo(padre, "Autovia");

        // Trazado real aproximado de la N-1 / A-10 atravesando Alsasua (norte-sur)
        Vector3[] puntos = {
            new Vector3(2100f, 0f,  500f),   // sur (hacia Pamplona)
            new Vector3(2080f, 0f, 2000f),
            new Vector3(2050f, 0f, 4000f),
            new Vector3(2020f, 0f, 6000f),
            new Vector3(2000f, 0f, 7500f),
            new Vector3(2010f, 0f, 8500f),   // pasa al este del pueblo
            new Vector3(2030f, 0f, 9500f),
            new Vector3(2060f, 0f, 11000f),
            new Vector3(2100f, 0f, 13000f),
            new Vector3(2150f, 0f, 15000f),
            new Vector3(2200f, 0f, 17000f),  // norte (hacia Vitoria/Bilbao)
        };

        // Ajustar Y al terreno y crear strip de asfalto
        CrearStripCarretera(puntos, 16f, new Color(0.15f, 0.15f, 0.15f), padre.transform, "AutoviaN1");
    }

    // =========================================================================
    //  CARRETERAS OSM
    // =========================================================================

    static void GenerarCarreterasOSM()
    {
        var padre = new GameObject("Carreteras_OSM");
        Undo.RegisterCreatedObjectUndo(padre, "Carreteras");

        string roadsPath = "Assets/AlsasuaData/roads_unity.json";
        var jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(roadsPath);

        if (jsonAsset == null)
        {
            // Fallback: usar CallesPrincipales de GeoDataCalles
            Debug.LogWarning("[Infra] roads_unity.json no encontrado. Usando GeoDataCalles.CallesPrincipales.");
            foreach (var calle in GeoDataCalles.CallesPrincipales)
                CrearStripCarretera(calle.Puntos, 6f, new Color(0.25f, 0.25f, 0.25f),
                                    padre.transform, calle.Nombre);
            return;
        }

        // Parsear JSON — TODAS las carreteras, sin límite
        try
        {
            var root = JArray.Parse(jsonAsset.text);
            int total = root.Count;
            int count = 0;
            int omitidas = 0;

            for (int idx = 0; idx < total; idx++)
            {
                if (idx % 50 == 0)
                    EditorUtility.DisplayProgressBar("Carreteras OSM",
                        $"Procesando {idx}/{total}...", (float)idx / total);

                var road = root[idx];
                var pts = road["pts"] as JArray;
                if (pts == null || pts.Count < 2) { omitidas++; continue; }

                // Tipo de carretera (highway tag de OSM)
                string tipo = road["type"]?.Value<string>()
                           ?? road["highway"]?.Value<string>()
                           ?? "residential";

                // Determinar ancho según el tipo OSM
                float ancho;
                Color color;
                switch (tipo.ToLower())
                {
                    case "motorway":     case "motorway_link":
                        ancho = 14f; color = new Color(0.15f, 0.15f, 0.15f); break; // autopista
                    case "trunk":        case "trunk_link":
                        ancho = 12f; color = new Color(0.17f, 0.17f, 0.17f); break;
                    case "primary":      case "primary_link":
                        ancho = 9f;  color = new Color(0.22f, 0.22f, 0.22f); break;
                    case "secondary":    case "secondary_link":
                        ancho = 7f;  color = new Color(0.26f, 0.26f, 0.26f); break;
                    case "tertiary":
                        ancho = 6f;  color = new Color(0.28f, 0.28f, 0.28f); break;
                    case "residential":
                        ancho = 5f;  color = new Color(0.30f, 0.30f, 0.30f); break;
                    case "service":
                        ancho = 3.5f; color = new Color(0.32f, 0.32f, 0.32f); break;
                    case "footway":      case "path": case "pedestrian":
                        ancho = 2f;  color = new Color(0.45f, 0.40f, 0.32f); break; // tono tierra
                    default:
                        ancho = 4f;  color = new Color(0.30f, 0.30f, 0.30f); break;
                }

                // Detectar formato — pts puede venir como:
                //   A) [[x,z],[x,z],...]       — array de pares
                //   B) [{"x":..,"z":..},...]   — array de objetos
                //   C) [x1,z1,x2,z2,...]       — flat (formato roads_unity.json real)
                Vector3[] puntos;
                if (pts.Count > 0 && pts[0] is Newtonsoft.Json.Linq.JObject)
                {
                    // Formato B
                    puntos = new Vector3[pts.Count];
                    for (int i = 0; i < pts.Count; i++)
                    {
                        float x = pts[i]["x"]?.Value<float>() ?? 0f;
                        float z = pts[i]["z"]?.Value<float>() ?? 0f;
                        puntos[i] = new Vector3(x, 0f, z);
                    }
                }
                else if (pts.Count > 0 && pts[0] is Newtonsoft.Json.Linq.JArray)
                {
                    // Formato A
                    puntos = new Vector3[pts.Count];
                    for (int i = 0; i < pts.Count; i++)
                    {
                        var par = pts[i] as Newtonsoft.Json.Linq.JArray;
                        float x = par[0].Value<float>();
                        float z = par[1].Value<float>();
                        puntos[i] = new Vector3(x, 0f, z);
                    }
                }
                else
                {
                    // Formato C — flat [x1,z1,x2,z2,...]
                    int n = pts.Count / 2;
                    if (n < 2) { omitidas++; continue; }
                    puntos = new Vector3[n];
                    for (int i = 0; i < n; i++)
                    {
                        float x = pts[i * 2].Value<float>();
                        float z = pts[i * 2 + 1].Value<float>();
                        puntos[i] = new Vector3(x, 0f, z);
                    }
                }
                CrearStripCarretera(puntos, ancho, color, padre.transform, $"{tipo}_{count++}");

                // Aceras + línea central blanca para carreteras principales
                if (ancho >= 6f)
                {
                    AñadirAceras(puntos, ancho, padre.transform, $"Acera_{count}");
                }
                if (ancho >= 7f)
                {
                    AñadirLineaCentral(puntos, padre.transform, $"Linea_{count}");
                }
            }

            EditorUtility.ClearProgressBar();
            Debug.Log($"[Infra] ✓ {count} carreteras OSM generadas (omitidas {omitidas}).");
        }
        catch (System.Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError("[Infra] Error parseando roads_unity.json: " + e.Message);
        }
    }

    // =========================================================================
    //  VÍAS DEL TREN
    // =========================================================================

    static void GenerarViasTren()
    {
        var padre = new GameObject("Vias_Tren");
        Undo.RegisterCreatedObjectUndo(padre, "Vias");

        string railPath = "Assets/AlsasuaData/railways_unity.json";
        var jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(railPath);

        Vector3[] puntos;
        if (jsonAsset != null)
        {
            try
            {
                var root = JArray.Parse(jsonAsset.text);
                var primera = root[0];
                var pts = primera["pts"] as JArray;

                if (pts.Count > 0 && pts[0] is Newtonsoft.Json.Linq.JObject)
                {
                    puntos = new Vector3[pts.Count];
                    for (int i = 0; i < pts.Count; i++)
                        puntos[i] = new Vector3(
                            pts[i]["x"]?.Value<float>() ?? 0f, 0f,
                            pts[i]["z"]?.Value<float>() ?? 0f);
                }
                else if (pts.Count > 0 && pts[0] is Newtonsoft.Json.Linq.JArray)
                {
                    puntos = new Vector3[pts.Count];
                    for (int i = 0; i < pts.Count; i++)
                    {
                        var par = pts[i] as Newtonsoft.Json.Linq.JArray;
                        puntos[i] = new Vector3(par[0].Value<float>(), 0f, par[1].Value<float>());
                    }
                }
                else
                {
                    int n = pts.Count / 2;
                    puntos = new Vector3[n];
                    for (int i = 0; i < n; i++)
                        puntos[i] = new Vector3(pts[i*2].Value<float>(), 0f, pts[i*2+1].Value<float>());
                }
            }
            catch
            {
                puntos = TrazadoTrenFallback();
            }
        }
        else puntos = TrazadoTrenFallback();

        // Balasto (gris claro, 4m ancho)
        CrearStripCarretera(puntos, 4f, new Color(0.55f, 0.50f, 0.45f), padre.transform, "Balasto");

        // Carriles (2 líneas paralelas finas)
        float separacion = 0.834f; // 1668mm / 2 (ibérico)
        var rail1 = new Vector3[puntos.Length];
        var rail2 = new Vector3[puntos.Length];
        for (int i = 0; i < puntos.Length; i++)
        {
            Vector3 p   = puntos[i];
            Vector3 perp = (i < puntos.Length - 1) ?
                Vector3.Cross((puntos[i+1] - p).normalized, Vector3.up) : Vector3.right;
            rail1[i] = p + perp * separacion;
            rail2[i] = p - perp * separacion;
        }
        CrearStripCarretera(rail1, 0.12f, new Color(0.4f, 0.35f, 0.30f), padre.transform, "Rail_1");
        CrearStripCarretera(rail2, 0.12f, new Color(0.4f, 0.35f, 0.30f), padre.transform, "Rail_2");

        // Traviesas cada 0.6m
        GenerarTraviesas(puntos, padre.transform);

        Debug.Log($"[Infra] ✓ Vías del tren generadas ({puntos.Length} waypoints).");
    }

    static Vector3[] TrazadoTrenFallback()
    {
        // Trazado aproximado del tren atravesando Alsasua (este-oeste, paralelo al N-1)
        return new Vector3[] {
            new Vector3(0f,    0f, 8300f),
            new Vector3(1000f, 0f, 8330f),
            new Vector3(2000f, 0f, 8360f),
            new Vector3(2100f, 0f, 8350f),    // estación
            new Vector3(3000f, 0f, 8380f),
            new Vector3(4000f, 0f, 8400f),
            new Vector3(5000f, 0f, 8420f),
        };
    }

    static void GenerarTraviesas(Vector3[] puntos, Transform padre)
    {
        var traviesas = new GameObject("Traviesas");
        traviesas.transform.SetParent(padre);

        float distTotal = 0f;
        for (int i = 0; i < puntos.Length - 1; i++)
            distTotal += Vector3.Distance(puntos[i], puntos[i+1]);

        int numTraviesas = Mathf.Min(500, Mathf.RoundToInt(distTotal / 0.6f));
        if (numTraviesas == 0) return;

        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader);
        mat.SetColor("_BaseColor", new Color(0.25f, 0.18f, 0.10f));
        mat.SetColor("_Color",     new Color(0.25f, 0.18f, 0.10f));

        for (int t = 0; t < numTraviesas; t++)
        {
            float f = (float)t / numTraviesas;
            Vector3 pos = InterpolarPuntos(puntos, f);
            pos.y = SamplearAltura(pos.x, pos.z) + 0.05f;

            // Dirección de la vía en este punto
            Vector3 dir = (InterpolarPuntos(puntos, Mathf.Min(1f, f + 0.001f)) - pos).normalized;
            float rotY = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

            var tr = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tr.name = $"Traviesa_{t}";
            tr.transform.SetParent(traviesas.transform);
            tr.transform.position = pos;
            tr.transform.rotation = Quaternion.Euler(0, rotY, 0);
            tr.transform.localScale = new Vector3(2.6f, 0.15f, 0.25f);
            tr.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(tr.GetComponent<Collider>());
        }
    }

    static Vector3 InterpolarPuntos(Vector3[] pts, float f)
    {
        f = Mathf.Clamp01(f);
        float idx = f * (pts.Length - 1);
        int i = Mathf.FloorToInt(idx);
        float frac = idx - i;
        if (i >= pts.Length - 1) return pts[pts.Length - 1];
        return Vector3.Lerp(pts[i], pts[i + 1], frac);
    }

    // =========================================================================
    //  RÍO ARAKIL
    // =========================================================================

    static void GenerarRio()
    {
        var padre = new GameObject("Rio_Arakil");
        Undo.RegisterCreatedObjectUndo(padre, "Rio");

        // Trazado del Arakil atravesando Alsasua
        Vector3[] puntos = {
            new Vector3(0f,    0f, 8100f),
            new Vector3(800f,  0f, 8150f),
            new Vector3(1500f, 0f, 8200f),
            new Vector3(1963f, 0f, 8215f),  // bajo Herriko Plaza
            new Vector3(2500f, 0f, 8250f),
            new Vector3(3200f, 0f, 8300f),
            new Vector3(4000f, 0f, 8350f),
            new Vector3(5000f, 0f, 8400f),
        };

        var matAgua = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        matAgua.SetColor("_BaseColor", new Color(0.18f, 0.42f, 0.65f, 0.85f));
        matAgua.SetColor("_Color",     new Color(0.18f, 0.42f, 0.65f, 0.85f));
        matAgua.SetFloat("_Smoothness", 0.95f);
        matAgua.SetFloat("_Metallic",   0.1f);

        CrearStripCarreteraConMaterial(puntos, 12f, matAgua, padre.transform, "Cauce_Arakil", -0.3f);

        Debug.Log("[Infra] ✓ Río Arakil generado.");
    }

    // =========================================================================
    //  ESTACIÓN DE TREN
    // =========================================================================

    static void GenerarEstacion()
    {
        var padre = new GameObject("Estacion_Tren");
        Undo.RegisterCreatedObjectUndo(padre, "Estacion");

        Vector3 pos = new Vector3(2100f, SamplearAltura(2100f, 8350f), 8350f);

        // Edificio principal
        var edif = GameObject.CreatePrimitive(PrimitiveType.Cube);
        edif.name = "Edificio_Estacion";
        edif.transform.SetParent(padre.transform);
        edif.transform.position = pos + Vector3.up * 4f;
        edif.transform.localScale = new Vector3(60f, 8f, 18f);
        var matE = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        matE.SetColor("_BaseColor", new Color(0.85f, 0.82f, 0.72f));
        matE.SetColor("_Color",     new Color(0.85f, 0.82f, 0.72f));
        edif.GetComponent<Renderer>().sharedMaterial = matE;

        // Andén
        var anden = GameObject.CreatePrimitive(PrimitiveType.Cube);
        anden.name = "Anden";
        anden.transform.SetParent(padre.transform);
        anden.transform.position = pos + new Vector3(0, 0.4f, -12f);
        anden.transform.localScale = new Vector3(80f, 0.8f, 6f);
        var matA = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        matA.SetColor("_BaseColor", new Color(0.55f, 0.55f, 0.55f));
        anden.GetComponent<Renderer>().sharedMaterial = matA;

        Debug.Log("[Infra] ✓ Estación de tren generada.");
    }

    // =========================================================================
    //  HELPERS
    // =========================================================================

    static float SamplearAltura(float x, float z)
    {
        return _terrain != null ? _terrain.SampleHeight(new Vector3(x, 0, z)) : 240f;
    }

    static void AñadirAceras(Vector3[] puntos, float anchoCalzada, Transform padre, string nombre)
    {
        // Crear dos strips paralelos al lado de la calzada
        var izq = new Vector3[puntos.Length];
        var der = new Vector3[puntos.Length];
        float offset = anchoCalzada * 0.5f + 0.6f;
        for (int i = 0; i < puntos.Length; i++)
        {
            Vector3 dir;
            if (i == 0)                      dir = (puntos[i+1] - puntos[i]).normalized;
            else if (i == puntos.Length - 1) dir = (puntos[i] - puntos[i-1]).normalized;
            else                             dir = (puntos[i+1] - puntos[i-1]).normalized;
            dir.y = 0; dir.Normalize();
            Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized;
            izq[i] = puntos[i] - perp * offset;
            der[i] = puntos[i] + perp * offset;
        }
        CrearStripCarretera(izq, 1.2f, new Color(0.62f, 0.62f, 0.60f), padre, nombre + "_Izq", 0.22f);
        CrearStripCarretera(der, 1.2f, new Color(0.62f, 0.62f, 0.60f), padre, nombre + "_Der", 0.22f);
    }

    static void AñadirLineaCentral(Vector3[] puntos, Transform padre, string nombre)
    {
        var mat = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetColor("_Color",     Color.white);
        CrearStripCarreteraConMaterial(puntos, 0.15f, mat, padre, nombre, ALTURA_OFFSET + 0.001f);
    }

    static void CrearStripCarretera(Vector3[] puntos, float ancho, Color color, Transform padre, string nombre, float yOffset = ALTURA_OFFSET)
    {
        var mat = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        mat.SetColor("_BaseColor", color);
        mat.SetColor("_Color",     color);
        mat.SetFloat("_Smoothness", 0.1f);
        CrearStripCarreteraConMaterial(puntos, ancho, mat, padre, nombre, yOffset);
    }

    static void CrearStripCarreteraConMaterial(Vector3[] puntos, float ancho, Material mat,
                                                Transform padre, string nombre, float yOffset)
    {
        if (puntos.Length < 2) return;

        // Crear vértices del strip
        var verts = new List<Vector3>();
        var tris  = new List<int>();
        var uvs   = new List<Vector2>();

        for (int i = 0; i < puntos.Length; i++)
        {
            Vector3 p = puntos[i];
            p.y = SamplearAltura(p.x, p.z) + yOffset;

            Vector3 dir;
            if (i == 0)                          dir = (puntos[i+1] - puntos[i]).normalized;
            else if (i == puntos.Length - 1)     dir = (puntos[i] - puntos[i-1]).normalized;
            else                                 dir = (puntos[i+1] - puntos[i-1]).normalized;
            dir.y = 0; dir.Normalize();
            Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized;

            verts.Add(p - perp * ancho * 0.5f);
            verts.Add(p + perp * ancho * 0.5f);
            uvs.Add(new Vector2(0, (float)i / puntos.Length * 10f));
            uvs.Add(new Vector2(1, (float)i / puntos.Length * 10f));

            if (i < puntos.Length - 1)
            {
                int v = i * 2;
                tris.Add(v);     tris.Add(v + 2); tris.Add(v + 1);
                tris.Add(v + 1); tris.Add(v + 2); tris.Add(v + 3);
            }
        }

        var mesh = new Mesh();
        mesh.name = nombre;
        if (verts.Count >= 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject(nombre);
        go.transform.SetParent(padre);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
    }
}
#endif
