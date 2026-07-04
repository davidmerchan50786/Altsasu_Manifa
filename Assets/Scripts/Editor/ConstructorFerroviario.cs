// Assets/Scripts/Editor/ConstructorFerroviario.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONSTRUCTOR FERROVIARIO — vías de tren, andenes y estaciones de Alsasua
//
//  Lee railways_unity.json (líneas Madrid-Hendaya y Alsasua-Castejón) y genera:
//  1. BALASTO  — franja gris 4 m de ancho (lecho de grava de la vía)
//  2. CARRIL   — dos cintas negras de 0.3 m dentro del balasto (las traviesas/carriles)
//  3. ANDENES  — los polígonos type="platform" como superficie elevada
//  4. MARCADORES de estación (Altsasu) y apeadero (Altsasu-Herria)
//
//  Altura corregida con heightmap V3 (bilineal exacto). 1 draw call por malla.
//  Menú: Tools/Alsasua/Mundo/🚆 Construir Ferroviario
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ConstructorFerroviario
{
    const string JSON         = "Assets/AlsasuaData/railways_unity.json";
    const string RAIZ         = "Ferroviario_Asset";
    const string MAT_BALASTO  = "Assets/Materials/Roads/M_Balasto_Via.mat";
    const string MAT_CARRIL   = "Assets/Materials/Roads/M_Carril_Tren.mat";
    const string MAT_ANDEN    = "Assets/Materials/Roads/M_Arcen_Hormigon.mat";

    const float ANCHO_BALASTO = 4.0f;
    const float ANCHO_CARRIL  = 0.3f;
    const float ANCHO_VIA     = 1.435f;  // ancho de vía internacional (m)
    const float Y_BALASTO     = 0.08f;
    const float Y_CARRIL      = 0.12f;
    const float Y_ANDEN       = 0.30f;   // andén elevado 30 cm sobre el balasto

    // ── Estructuras de datos ───────────────────────────────────────────────
    [System.Serializable] class Pt  { public float x, y, z; }
    [System.Serializable] class Rail { public long osm_id; public string type, name; public int tracks; public string electrified; public float[] pts; }
    [System.Serializable] class Station { public long osm_id; public string type, name; public float x, z; }
    [System.Serializable] class Wrap { public Rail[] rails; public Station[] stations; }

    // Heightmap V3 (cacheado para la sesión del constructor)
    static MuestreadorHeightmapV3 _v3;
    static bool _v3Init;
    static MuestreadorHeightmapV3 V3
    {
        get
        {
            if (_v3Init) return _v3;
            _v3Init = true;
            var m = new MuestreadorHeightmapV3();
            if (m.Cargar()) _v3 = m;
            return _v3;
        }
    }

    static float Altura(float x, float z, float offset)
    {
        if (V3 != null && V3.EnRango(x, z)) return V3.AlturaMundo(x, z) + offset;
        var ts = Terrain.activeTerrains;
        foreach (var t in ts)
        {
            if (t == null) continue;
            var p = t.transform.position; var s = t.terrainData.size;
            if (x >= p.x && x < p.x + s.x && z >= p.z && z < p.z + s.z)
                return p.y + t.SampleHeight(new Vector3(x, 0, z)) + offset;
        }
        return offset;
    }

    [MenuItem("Tools/Alsasua/Mundo/🚆 Construir Ferroviario", priority = 14)]
    static void Construir()
    {
        string ruta = Path.GetFullPath(Path.Combine(Application.dataPath, "..", JSON));
        if (!File.Exists(ruta)) { EditorUtility.DisplayDialog("Ferroviario", $"No existe {JSON}", "Vale"); return; }

        Wrap w;
        try { w = JsonUtility.FromJson<Wrap>(File.ReadAllText(ruta)); }
        catch (System.Exception e) { EditorUtility.DisplayDialog("Ferroviario", $"JSON error: {e.Message}", "Vale"); return; }
        if (w?.rails == null) { EditorUtility.DisplayDialog("Ferroviario", "Sin datos de vía.", "Vale"); return; }

        if (!EditorUtility.DisplayDialog("Ferroviario",
            $"{w.rails.Length} segmentos de vía + {w.stations?.Length ?? 0} estaciones.\n" +
            "Genera balasto, carriles, andenes y marcadores. ¿Continuar?", "Construir", "Cancelar"))
            return;

        var matBalasto = CargarMat(MAT_BALASTO, Color.gray);
        var matCarril  = CargarMat(MAT_CARRIL,  Color.black);
        var matAnden   = CargarMat(MAT_ANDEN,   new Color(0.7f, 0.7f, 0.65f));

        var (vB, tB, uB) = Buf(); // balasto
        var (vC, tC, uC) = Buf(); // carril izq
        var (vD, tD, uD) = Buf(); // carril der
        var (vA, tA, uA) = Buf(); // andenes

        int segs = 0;
        try
        {
            foreach (var rail in w.rails)
            {
                segs++;
                if (segs % 20 == 0 && EditorUtility.DisplayCancelableProgressBar(
                    "Ferroviario", $"Segmento {segs}/{w.rails.Length}…", segs / (float)w.rails.Length)) break;

                if (rail.pts == null || rail.pts.Length < 6) continue;

                if (rail.type == "platform")
                {
                    GenerarPoligonoVia(rail.pts, Y_ANDEN, vA, tA, uA);
                    continue;
                }

                for (int i = 0; i < rail.pts.Length - 3; i += 3)
                {
                    Vector2 a = new Vector2(rail.pts[i], rail.pts[i + 2]);
                    Vector2 b = new Vector2(rail.pts[i + 3], rail.pts[i + 5]);
                    Vector2 dir = b - a; float len = dir.magnitude;
                    if (len < 0.1f) continue;
                    dir /= len;
                    Vector2 perp = new Vector2(-dir.y, dir.x);

                    float ya = Altura(a.x, a.y, Y_BALASTO), yb = Altura(b.x, b.y, Y_BALASTO);
                    float vN = len / ANCHO_BALASTO;
                    // Balasto
                    AñadirQuad(vB, tB, uB, a - perp * (ANCHO_BALASTO * .5f), a + perp * (ANCHO_BALASTO * .5f),
                                            b - perp * (ANCHO_BALASTO * .5f), b + perp * (ANCHO_BALASTO * .5f), ya, yb, 0, vN);
                    // Carril izquierdo (UIC gauge −0.7175 m del eje)
                    float yaC = ya + (Y_CARRIL - Y_BALASTO), ybC = yb + (Y_CARRIL - Y_BALASTO);
                    float off = ANCHO_VIA * 0.5f;
                    AñadirQuad(vC, tC, uC, a - perp * (off + ANCHO_CARRIL * .5f), a - perp * (off - ANCHO_CARRIL * .5f),
                                            b - perp * (off + ANCHO_CARRIL * .5f), b - perp * (off - ANCHO_CARRIL * .5f), yaC, ybC, 0, vN);
                    // Carril derecho
                    AñadirQuad(vD, tD, uD, a + perp * (off - ANCHO_CARRIL * .5f), a + perp * (off + ANCHO_CARRIL * .5f),
                                            b + perp * (off - ANCHO_CARRIL * .5f), b + perp * (off + ANCHO_CARRIL * .5f), yaC, ybC, 0, vN);
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        // Crear raíz
        var raizAnt = GameObject.Find(RAIZ);
        if (raizAnt != null) Object.DestroyImmediate(raizAnt);
        var raiz = new GameObject(RAIZ);

        var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI;

        Directory.CreateDirectory("Assets/CiudadHorneada/Meshes");
        CrearGO("Balasto",          raiz, vB, tB, uB, matBalasto, flags);
        CrearGO("Carril_Izquierdo", raiz, vC, tC, uC, matCarril,  flags);
        CrearGO("Carril_Derecho",   raiz, vD, tD, uD, matCarril,  flags);
        CrearGO("Andenes",          raiz, vA, tA, uA, matAnden,   flags);

        // Marcadores de estación / apeadero
        if (w.stations != null)
            foreach (var st in w.stations)
                CrearMarcador(st, raiz);

        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        EditorUtility.DisplayDialog("Ferroviario ✅",
            $"Red ferroviaria completa:\n" +
            $"  • {segs} segmentos de vía (Madrid-Hendaya + Alsasua-Castejón)\n" +
            $"  • Balasto 4 m + carriles 1.435 m de ancho de vía\n" +
            $"  • {w.stations?.Length ?? 0} estaciones marcadas\n" +
            "Raíz 'Ferroviario_Asset' static.", "Genial");
    }

    // Genera un polígono relleno para los andenes (format: [x,y,z, x,y,z,...])
    static void GenerarPoligonoVia(float[] pts, float yOffset,
        List<Vector3> v, List<int> t, List<Vector2> u)
    {
        // Centroide
        int n = pts.Length / 3;
        Vector2 c = Vector2.zero;
        for (int i = 0; i < n; i++) c += new Vector2(pts[i * 3], pts[i * 3 + 2]);
        c /= n;

        int base0 = v.Count;
        float yC = Altura(c.x, c.y, yOffset);
        v.Add(new Vector3(c.x, yC, c.y));
        u.Add(new Vector2(0.5f, 0.5f));

        for (int i = 0; i < n; i++)
        {
            float px = pts[i * 3], pz = pts[i * 3 + 2];
            float py = Altura(px, pz, yOffset);
            v.Add(new Vector3(px, py, pz));
            u.Add(new Vector2(i / (float)n, 0f));
        }
        for (int i = 1; i <= n; i++)
        {
            t.Add(base0);
            t.Add(base0 + i);
            t.Add(base0 + (i % n) + 1);
        }
    }

    static void CrearMarcador(Station st, GameObject raiz)
    {
        float y = Altura(st.x, st.z, 1f);
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = $"Estacion_{st.name}_{st.type}";
        go.transform.SetParent(raiz.transform);
        go.transform.position = new Vector3(st.x, y, st.z);
        go.transform.localScale = st.type == "station"
            ? new Vector3(40f, 4f, 12f)  // estación principal — edificio largo
            : new Vector3(15f, 2f, 6f);   // apeadero — más pequeño
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sharedMaterial = CargarMat(MAT_ANDEN, new Color(0.75f, 0.72f, 0.65f));
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    static (List<Vector3>, List<int>, List<Vector2>) Buf() =>
        (new List<Vector3>(1 << 14), new List<int>(1 << 15), new List<Vector2>(1 << 14));

    static void AñadirQuad(List<Vector3> v, List<int> t, List<Vector2> u,
        Vector2 aL, Vector2 aR, Vector2 bL, Vector2 bR, float ya, float yb, float v0, float v1)
    {
        int b = v.Count;
        v.Add(new Vector3(aL.x, ya, aL.y)); v.Add(new Vector3(aR.x, ya, aR.y));
        v.Add(new Vector3(bL.x, yb, bL.y)); v.Add(new Vector3(bR.x, yb, bR.y));
        u.Add(new Vector2(0, v0)); u.Add(new Vector2(1, v0));
        u.Add(new Vector2(0, v1)); u.Add(new Vector2(1, v1));
        t.Add(b); t.Add(b + 2); t.Add(b + 1);
        t.Add(b + 1); t.Add(b + 2); t.Add(b + 3);
    }

    static void CrearGO(string nombre, GameObject padre,
        List<Vector3> v, List<int> t, List<Vector2> u, Material mat, StaticEditorFlags flags)
    {
        if (v.Count == 0) return;
        var mesh = new Mesh { name = nombre, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.SetUVs(0, u);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(
            $"Assets/CiudadHorneada/Meshes/via_{nombre.ToLower()}.asset"));
        var go = new GameObject(nombre);
        go.transform.SetParent(padre.transform, true);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.isStatic = true;
        GameObjectUtility.SetStaticEditorFlags(go, flags);
    }

    static Material CargarMat(string path, Color fallbackColor)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        var mg = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        mg.color = fallbackColor;
        return mg;
    }

    [MenuItem("Tools/Alsasua/Mundo/↩️ Limpiar Ferroviario", priority = 15)]
    static void Limpiar()
    {
        var r = GameObject.Find(RAIZ);
        if (r != null) Object.DestroyImmediate(r);
    }
}
#endif
