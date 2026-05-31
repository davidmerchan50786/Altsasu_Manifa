#if UNITY_EDITOR
// Assets/Scripts/Editor/GeneradorMontanasFondo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MONTAÑAS FONDO — Aralar (sur) y Urbasa (norte)
//
//  Genera un anillo de mesh montañosa procedural en torno al terrain principal,
//  a 4-6 km del centro, con picos hasta 1500 m. Da profundidad visual brutal
//  cuando miras hacia el horizonte.
//
//  Material: low-poly con shader HDRP/Lit color terrain + atmospheric scattering.
//  La niebla aérea HDRP las desvanece naturalmente con la distancia.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;

public static class GeneradorMontanasFondo
{
    const float RADIO_INTERIOR = 4500f;  // m desde el centro
    const float RADIO_EXTERIOR = 12000f; // m
    const int   SEGMENTOS_RADIAL = 240;  // resolución angular (curvatura suave)
    const int   ANILLOS          = 32;    // bandas radiales (lod profundidad)
    const float ALTURA_BASE      = 240f;  // nivel valle
    const float ALTURA_MAX_PICOS = 1400f;

    public static void Generar()
    {
        var existente = GameObject.Find("Montanas_Fondo");
        if (existente != null) Object.DestroyImmediate(existente);

        var raiz = new GameObject("Montanas_Fondo");
        // Centrado en Herriko Plaza
        raiz.transform.position = new Vector3(1918f, 0, 8570f);

        var mesh = ConstruirMeshAnular();
        var mf = raiz.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        var mr = raiz.AddComponent<MeshRenderer>();
        mr.sharedMaterial = CrearMaterialMontana();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        // No collider — solo backdrop visual
        // Etiquetar como static para optimizaciones
        raiz.isStatic = true;

        // Guardar mesh como asset para que persista
        string meshPath = "Assets/AlsasuaData/Mesh_Montanas_Fondo.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null)
            AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("✅ Montañas Aralar/Urbasa",
            $"Mesh anular generada:\n" +
            $"• {SEGMENTOS_RADIAL * (ANILLOS + 1)} vértices\n" +
            $"• Radio: 4.5 km → 12 km\n" +
            $"• Picos hasta {ALTURA_MAX_PICOS:0} m\n\n" +
            "Profundidad visual hacia el horizonte garantizada.\n" +
            "La niebla aérea HDRP las desvanece con la distancia.",
            "OK");
    }

    // ─────────────────────────────────────────────────────────────────────

    static Mesh ConstruirMeshAnular()
    {
        var mesh = new Mesh { name = "Montanas_Anular" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        int vCount = SEGMENTOS_RADIAL * (ANILLOS + 1);
        var verts   = new Vector3[vCount];
        var normals = new Vector3[vCount];
        var colors  = new Color[vCount];
        var uvs     = new Vector2[vCount];

        for (int r = 0; r <= ANILLOS; r++)
        {
            float t = (float)r / ANILLOS;
            // distancia desde el centro: interpolar de RADIO_INT a RADIO_EXT
            float dist = Mathf.Lerp(RADIO_INTERIOR, RADIO_EXTERIOR, t);

            for (int s = 0; s < SEGMENTOS_RADIAL; s++)
            {
                float ang = (s / (float)SEGMENTOS_RADIAL) * Mathf.PI * 2f;
                float cx = Mathf.Cos(ang) * dist;
                float cz = Mathf.Sin(ang) * dist;

                // Altura con ruido fractal — más alto en bandas centrales
                float fAng = ang * 1.5f;
                float pico = 0f;
                pico += Mathf.PerlinNoise(cx * 0.0008f + 10f, cz * 0.0008f + 10f) * 1.0f;
                pico += Mathf.PerlinNoise(cx * 0.0030f + 50f, cz * 0.0030f + 50f) * 0.4f;
                pico += Mathf.PerlinNoise(cx * 0.012f, cz * 0.012f) * 0.15f;

                // Curva radial: máximo en el anillo medio
                float perfilRadial = Mathf.Sin(t * Mathf.PI); // 0..1..0
                float h = pico * perfilRadial;

                // Picos especialmente altos al sur (Aralar) y al norte (Urbasa)
                float bonus = Mathf.Abs(Mathf.Sin(ang));
                h *= 0.4f + bonus * 0.8f;

                float y = ALTURA_BASE + h * ALTURA_MAX_PICOS;

                int idx = r * SEGMENTOS_RADIAL + s;
                verts[idx]   = new Vector3(cx, y, cz);
                normals[idx] = Vector3.up; // recalculamos abajo
                uvs[idx]     = new Vector2(s / (float)SEGMENTOS_RADIAL, t);

                // Color por altura — verde abajo, gris medio, blanco arriba (nieve)
                Color c;
                if (h < 0.3f)      c = Color.Lerp(new Color(0.30f, 0.42f, 0.20f),
                                                  new Color(0.45f, 0.50f, 0.30f), h / 0.3f);
                else if (h < 0.7f) c = Color.Lerp(new Color(0.45f, 0.50f, 0.30f),
                                                  new Color(0.55f, 0.52f, 0.45f), (h - 0.3f) / 0.4f);
                else               c = Color.Lerp(new Color(0.55f, 0.52f, 0.45f),
                                                  new Color(0.90f, 0.92f, 0.95f), (h - 0.7f) / 0.3f);
                colors[idx] = c;
            }
        }

        // Triángulos
        int triCount = SEGMENTOS_RADIAL * ANILLOS * 2;
        var tris = new int[triCount * 3];
        int ti = 0;
        for (int r = 0; r < ANILLOS; r++)
        {
            for (int s = 0; s < SEGMENTOS_RADIAL; s++)
            {
                int s1 = (s + 1) % SEGMENTOS_RADIAL;
                int a = r * SEGMENTOS_RADIAL + s;
                int b = r * SEGMENTOS_RADIAL + s1;
                int c = (r + 1) * SEGMENTOS_RADIAL + s;
                int d = (r + 1) * SEGMENTOS_RADIAL + s1;
                tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
            }
        }

        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.uv = uvs;
        mesh.colors = colors;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        return mesh;
    }

    static Material CrearMaterialMontana()
    {
        var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh) { name = "Mat_Montanas_Fondo" };
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.1f);
        // Vertex color para que se vea el degradado verde→gris→blanco
        m.EnableKeyword("_VERTEX_COLOR");
        return m;
    }
}
#endif
