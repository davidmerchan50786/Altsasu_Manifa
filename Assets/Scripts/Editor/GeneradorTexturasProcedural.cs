#if UNITY_EDITOR
// Assets/Scripts/Editor/GeneradorTexturasProcedural.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GENERADOR DE TEXTURAS PROCEDURALES — adoquines, ladrillo, piedra, asfalto
//
//  Crea texturas seamless 512x512 guardadas en Assets/AlsasuaData/Textures/Proc/
//  Tras generar, ejecuta el AsignadorMaterialesAAA para aplicarlas.
//
//  MENÚ: Altsasu GTA → Territorio Real → ★ Generar Texturas Procedurales
// ═══════════════════════════════════════════════════════════════════════════

using System.IO;
using UnityEngine;
using UnityEditor;

public static class GeneradorTexturasProcedural
{
    const string CARPETA = "Assets/AlsasuaData/Textures/Proc";
    const int RES = 512;

    // MENÚ-LEGACY: [MenuItem("Altsasu GTA/Territorio Real/★ Generar Texturas Procedurales", false, 10)]
    public static void Generar()
    {
        if (!AssetDatabase.IsValidFolder(CARPETA))
        {
            if (!AssetDatabase.IsValidFolder("Assets/AlsasuaData/Textures"))
                AssetDatabase.CreateFolder("Assets/AlsasuaData", "Textures");
            AssetDatabase.CreateFolder("Assets/AlsasuaData/Textures", "Proc");
        }

        EditorUtility.DisplayProgressBar("Texturas Proc", "Adoquines...", 0.1f);
        GuardarTextura("Adoquines", GenerarAdoquines());

        EditorUtility.DisplayProgressBar("Texturas Proc", "Adoquines normal map...", 0.2f);
        GuardarTextura("Adoquines_N", GenerarAdoquinesNormal());

        EditorUtility.DisplayProgressBar("Texturas Proc", "Ladrillo...", 0.3f);
        GuardarTextura("Ladrillo", GenerarLadrillo());

        EditorUtility.DisplayProgressBar("Texturas Proc", "Piedra...", 0.45f);
        GuardarTextura("Piedra", GenerarPiedra());

        EditorUtility.DisplayProgressBar("Texturas Proc", "Asfalto...", 0.6f);
        GuardarTextura("Asfalto", GenerarAsfalto());

        EditorUtility.DisplayProgressBar("Texturas Proc", "Hormigón...", 0.7f);
        GuardarTextura("Hormigon", GenerarHormigon());

        EditorUtility.DisplayProgressBar("Texturas Proc", "Hierba...", 0.8f);
        GuardarTextura("Hierba_Proc", GenerarHierba());

        EditorUtility.DisplayProgressBar("Texturas Proc", "Plaster (paredes)...", 0.9f);
        GuardarTextura("Plaster", GenerarPlaster());

        EditorUtility.ClearProgressBar();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("✅ Texturas generadas",
            "Texturas procedurales seamless guardadas en:\n" + CARPETA + "\n\n" +
            "• Adoquines (zonas peatonales + diffuse + normal)\n" +
            "• Ladrillo (edificios modernos)\n" +
            "• Piedra (casco antiguo, muros)\n" +
            "• Asfalto (carreteras)\n" +
            "• Hormigón (aceras, naves industriales)\n" +
            "• Hierba\n" +
            "• Plaster (fachadas)\n\n" +
            "Ahora ejecuta:\n★ Aplicar Materiales AAA a todo", "OK");
    }

    // =========================================================================
    //  ADOQUINES — voronoi irregular con juntas de mortero
    // =========================================================================

    static Texture2D GenerarAdoquines()
    {
        var tex = new Texture2D(RES, RES, TextureFormat.RGB24, true);
        // Generar puntos de voronoi
        const int N = 30;
        var pts = new Vector2[N];
        var cols = new Color[N];
        for (int i = 0; i < N; i++)
        {
            pts[i] = new Vector2(Random.value, Random.value);
            float gris = Random.Range(0.35f, 0.65f);
            float tinte = Random.Range(-0.03f, 0.03f);
            cols[i] = new Color(gris + tinte, gris + tinte * 0.5f, gris - tinte * 0.5f);
        }
        // Función con wrapping para seamless
        var px = new Color[RES * RES];
        for (int y = 0; y < RES; y++)
        {
            for (int x = 0; x < RES; x++)
            {
                float u = (float)x / RES, v = (float)y / RES;
                float d1 = 999f, d2 = 999f;
                int idx = 0;
                for (int i = 0; i < N; i++)
                {
                    // Distancia toroidal (seamless)
                    float dx = Mathf.Abs(u - pts[i].x); if (dx > 0.5f) dx = 1f - dx;
                    float dy = Mathf.Abs(v - pts[i].y); if (dy > 0.5f) dy = 1f - dy;
                    float d = dx * dx + dy * dy;
                    if (d < d1) { d2 = d1; d1 = d; idx = i; }
                    else if (d < d2) d2 = d;
                }
                // Mortero entre adoquines
                float edge = Mathf.Sqrt(d2) - Mathf.Sqrt(d1);
                Color c = cols[idx];
                if (edge < 0.012f)
                {
                    float t = edge / 0.012f;
                    c = Color.Lerp(new Color(0.18f, 0.16f, 0.14f), c, t);
                }
                // Variación interna (textura piedra)
                float ruido = Mathf.PerlinNoise(u * 40f, v * 40f) * 0.15f;
                c.r += ruido - 0.075f; c.g += ruido - 0.075f; c.b += ruido - 0.075f;
                px[y * RES + x] = c;
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static Texture2D GenerarAdoquinesNormal()
    {
        var tex = new Texture2D(RES, RES, TextureFormat.RGB24, true);
        // Genera un normal map basado en altura (cada adoquín ligeramente convexo)
        const int N = 30;
        var pts = new Vector2[N];
        for (int i = 0; i < N; i++)
            pts[i] = new Vector2(Random.value, Random.value);

        // Buffer de alturas
        var alturas = new float[RES, RES];
        for (int y = 0; y < RES; y++)
        for (int x = 0; x < RES; x++)
        {
            float u = (float)x / RES, v = (float)y / RES;
            float d1 = 999f, d2 = 999f;
            for (int i = 0; i < N; i++)
            {
                float dx = Mathf.Abs(u - pts[i].x); if (dx > 0.5f) dx = 1f - dx;
                float dy = Mathf.Abs(v - pts[i].y); if (dy > 0.5f) dy = 1f - dy;
                float d = dx * dx + dy * dy;
                if (d < d1) { d2 = d1; d1 = d; }
                else if (d < d2) d2 = d;
            }
            float edge = Mathf.Sqrt(d2) - Mathf.Sqrt(d1);
            alturas[x, y] = Mathf.Clamp01(edge * 8f);
        }
        // Convertir a normal
        var px = new Color[RES * RES];
        for (int y = 0; y < RES; y++)
        for (int x = 0; x < RES; x++)
        {
            int xp = (x + 1) % RES, xm = (x - 1 + RES) % RES;
            int yp = (y + 1) % RES, ym = (y - 1 + RES) % RES;
            float dx = alturas[xp, y] - alturas[xm, y];
            float dy = alturas[x, yp] - alturas[x, ym];
            Vector3 n = new Vector3(-dx * 4f, -dy * 4f, 1f).normalized;
            px[y * RES + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f);
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // =========================================================================
    //  LADRILLO — filas alternadas con mortero
    // =========================================================================

    static Texture2D GenerarLadrillo()
    {
        var tex = new Texture2D(RES, RES, TextureFormat.RGB24, true);
        const float anchoLad = 0.125f;  // 8 ladrillos de ancho
        const float altoLad  = 0.0625f; // 16 filas
        const float mortero  = 0.008f;
        var px = new Color[RES * RES];
        for (int y = 0; y < RES; y++)
        for (int x = 0; x < RES; x++)
        {
            float u = (float)x / RES, v = (float)y / RES;
            int fila = Mathf.FloorToInt(v / altoLad);
            float offset = (fila % 2 == 0) ? 0f : anchoLad * 0.5f;
            float uMod = (u + offset) % anchoLad;
            float vMod = v % altoLad;

            bool esMortero = uMod < mortero || uMod > anchoLad - mortero ||
                             vMod < mortero || vMod > altoLad - mortero;

            Color c;
            if (esMortero)
            {
                c = new Color(0.55f, 0.52f, 0.48f);
            }
            else
            {
                // Color ladrillo con variación
                float seed = fila * 13.7f + Mathf.FloorToInt(u / anchoLad) * 7.3f;
                float r = 0.55f + (Mathf.PerlinNoise(seed, seed * 0.5f) - 0.5f) * 0.15f;
                c = new Color(r, r * 0.55f, r * 0.40f);
                // Ruido interno del ladrillo
                float n = Mathf.PerlinNoise(u * 80f, v * 80f) * 0.12f - 0.06f;
                c.r += n; c.g += n; c.b += n;
            }
            px[y * RES + x] = c;
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // =========================================================================
    //  PIEDRA — bloques irregulares (estilo casco antiguo vasco)
    // =========================================================================

    static Texture2D GenerarPiedra()
    {
        var tex = new Texture2D(RES, RES, TextureFormat.RGB24, true);
        const int N = 18;
        var pts = new Vector2[N];
        var cols = new Color[N];
        for (int i = 0; i < N; i++)
        {
            pts[i] = new Vector2(Random.value, Random.value);
            float gris = Random.Range(0.50f, 0.78f);
            float tinte = Random.Range(0f, 0.08f);
            cols[i] = new Color(gris + tinte, gris + tinte * 0.7f, gris - tinte * 0.3f);
        }
        var px = new Color[RES * RES];
        for (int y = 0; y < RES; y++)
        for (int x = 0; x < RES; x++)
        {
            float u = (float)x / RES, v = (float)y / RES;
            float d1 = 999f, d2 = 999f;
            int idx = 0;
            for (int i = 0; i < N; i++)
            {
                float dx = Mathf.Abs(u - pts[i].x); if (dx > 0.5f) dx = 1f - dx;
                float dy = Mathf.Abs(v - pts[i].y); if (dy > 0.5f) dy = 1f - dy;
                float d = dx * dx + dy * dy;
                if (d < d1) { d2 = d1; d1 = d; idx = i; }
                else if (d < d2) d2 = d;
            }
            float edge = Mathf.Sqrt(d2) - Mathf.Sqrt(d1);
            Color c = cols[idx];
            if (edge < 0.020f)
                c = Color.Lerp(new Color(0.32f, 0.28f, 0.22f), c, edge / 0.020f);
            // Ruido interno
            float ruido = Mathf.PerlinNoise(u * 60f, v * 60f) * 0.18f - 0.09f;
            c.r += ruido; c.g += ruido; c.b += ruido;
            px[y * RES + x] = c;
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // =========================================================================
    //  ASFALTO — ruido oscuro con piedrecitas
    // =========================================================================

    static Texture2D GenerarAsfalto()
    {
        var tex = new Texture2D(RES, RES, TextureFormat.RGB24, true);
        var px = new Color[RES * RES];
        for (int y = 0; y < RES; y++)
        for (int x = 0; x < RES; x++)
        {
            float u = (float)x / RES, v = (float)y / RES;
            float n1 = Mathf.PerlinNoise(u * 80f, v * 80f);
            float n2 = Mathf.PerlinNoise(u * 200f, v * 200f);
            float gris = 0.18f + n1 * 0.05f + n2 * 0.08f;
            px[y * RES + x] = new Color(gris, gris, gris * 0.98f);
        }
        // Salpicar piedrecitas claras
        for (int i = 0; i < 500; i++)
        {
            int x = Random.Range(0, RES);
            int y = Random.Range(0, RES);
            int idx = y * RES + x;
            px[idx] = new Color(0.42f, 0.42f, 0.40f);
            if (x + 1 < RES) px[idx + 1] = new Color(0.38f, 0.38f, 0.36f);
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // =========================================================================
    //  HORMIGÓN — acera, naves
    // =========================================================================

    static Texture2D GenerarHormigon()
    {
        var tex = new Texture2D(RES, RES, TextureFormat.RGB24, true);
        var px = new Color[RES * RES];
        for (int y = 0; y < RES; y++)
        for (int x = 0; x < RES; x++)
        {
            float u = (float)x / RES, v = (float)y / RES;
            float n = Mathf.PerlinNoise(u * 30f, v * 30f) * 0.15f;
            float gris = 0.65f + n;
            // Líneas de junta cada 0.25 (acera con baldosas)
            if (u % 0.25f < 0.004f || v % 0.25f < 0.004f)
                gris *= 0.82f;
            px[y * RES + x] = new Color(gris, gris, gris * 0.99f);
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // =========================================================================
    //  HIERBA
    // =========================================================================

    static Texture2D GenerarHierba()
    {
        var tex = new Texture2D(RES, RES, TextureFormat.RGB24, true);
        var px = new Color[RES * RES];
        for (int y = 0; y < RES; y++)
        for (int x = 0; x < RES; x++)
        {
            float u = (float)x / RES, v = (float)y / RES;
            float n1 = Mathf.PerlinNoise(u * 50f, v * 50f);
            float n2 = Mathf.PerlinNoise(u * 200f, v * 200f);
            // Hierba: verde con variaciones
            float r = 0.25f + n1 * 0.10f + n2 * 0.05f;
            float g = 0.45f + n1 * 0.15f + n2 * 0.05f;
            float b = 0.18f + n1 * 0.08f;
            px[y * RES + x] = new Color(r, g, b);
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // =========================================================================
    //  PLASTER (fachadas pintadas) — base + variación de pintura
    // =========================================================================

    static Texture2D GenerarPlaster()
    {
        var tex = new Texture2D(RES, RES, TextureFormat.RGB24, true);
        var px = new Color[RES * RES];
        for (int y = 0; y < RES; y++)
        for (int x = 0; x < RES; x++)
        {
            float u = (float)x / RES, v = (float)y / RES;
            float n = Mathf.PerlinNoise(u * 25f, v * 25f) * 0.12f;
            float ruido = Mathf.PerlinNoise(u * 200f, v * 200f) * 0.04f;
            float c = 0.85f + n - 0.06f + ruido;
            px[y * RES + x] = new Color(c, c * 0.96f, c * 0.90f);
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // =========================================================================
    //  HELPERS
    // =========================================================================

    static void GuardarTextura(string nombre, Texture2D tex)
    {
        string path = $"{CARPETA}/{nombre}.png";
        byte[] bytes = tex.EncodeToPNG();
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", path), bytes);
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);

        // Configurar import settings (sRGB, alpha source none, mipmaps yes, wrap repeat)
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = nombre.EndsWith("_N") ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.wrapMode    = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }
        Debug.Log($"[TexProc] ✓ {nombre}.png generada.");
    }
}
#endif
