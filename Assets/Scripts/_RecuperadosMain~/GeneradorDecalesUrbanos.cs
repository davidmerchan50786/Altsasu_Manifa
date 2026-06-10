#if UNITY_EDITOR
// Assets/Scripts/Editor/GeneradorDecalesUrbanos.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DECALES URBANOS — añade variedad visual al suelo
//    · Tapas de alcantarilla (manhole covers) en calles principales
//    · Pasos de cebra en intersecciones
//    · Manchas de aceite en carreteras
//    · Grietas en aceras
//
//  Usa DecalProjector de HDRP — no requiere mesh modificada.
//  Las texturas las genera de forma procedural si no existen.
// ═══════════════════════════════════════════════════════════════════════════

using System.IO;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEditor;

public static class GeneradorDecalesUrbanos
{
    const string TEX_DIR = "Assets/AlsasuaData/Textures/Decals";

    static Material _matAlcantarilla, _matMancha, _matGrieta, _matPasoCebra;

    public static void Generar()
    {
        GenerarTexturasSiHaceFalta();
        CrearMateriales();

        var padre = GameObject.Find("Decales_Urbanos");
        if (padre != null) Object.DestroyImmediate(padre);
        padre = new GameObject("Decales_Urbanos");

        int alcantarillas = 0, manchas = 0, pasoCebra = 0, grietas = 0;
        const int N_ALCANTARILLAS = 60;
        const int N_MANCHAS       = 120;
        const int N_GRIETAS       = 80;
        const int N_PASOSCEBRA    = 30;

        var t = Terrain.activeTerrain;
        if (t == null) { EditorUtility.DisplayDialog("Sin terrain", "Crea terrain primero.", "OK"); return; }

        // Rango aproximado: 5 km × 18 km, pero los decales útiles están en la zona urbana
        float xMin = 1500f, xMax = 2400f;
        float zMin = 8200f, zMax = 9100f;

        try
        {
            for (int i = 0; i < N_ALCANTARILLAS; i++)
            {
                Vector3 p = PosicionEnSuelo(xMin, xMax, zMin, zMax, t);
                CrearDecal($"Alcantarilla_{i}", p, _matAlcantarilla,
                           tamano: 1f, padre.transform);
                alcantarillas++;
            }
            for (int i = 0; i < N_MANCHAS; i++)
            {
                Vector3 p = PosicionEnSuelo(xMin, xMax, zMin, zMax, t);
                CrearDecal($"Mancha_{i}", p, _matMancha,
                           tamano: Random.Range(0.8f, 2.5f), padre.transform,
                           rotacionZ: Random.Range(0f, 360f));
                manchas++;
            }
            for (int i = 0; i < N_GRIETAS; i++)
            {
                Vector3 p = PosicionEnSuelo(xMin, xMax, zMin, zMax, t);
                CrearDecal($"Grieta_{i}", p, _matGrieta,
                           tamano: Random.Range(1f, 3f), padre.transform,
                           rotacionZ: Random.Range(0f, 360f));
                grietas++;
            }
            for (int i = 0; i < N_PASOSCEBRA; i++)
            {
                Vector3 p = PosicionEnSuelo(xMin, xMax, zMin, zMax, t);
                CrearDecal($"PasoCebra_{i}", p, _matPasoCebra,
                           tamano: 4f, padre.transform,
                           rotacionZ: Random.Range(0f, 4) * 90f);
                pasoCebra++;
            }
        }
        catch (System.Exception e) { Debug.LogError("[Decales] " + e); }

        EditorUtility.DisplayDialog("✅ Decales urbanos",
            $"• Alcantarillas: {alcantarillas}\n" +
            $"• Manchas: {manchas}\n" +
            $"• Grietas: {grietas}\n" +
            $"• Pasos de cebra: {pasoCebra}\n\n" +
            "Coloca el padre 'Decales_Urbanos' donde quieras refinar manualmente.", "OK");
    }

    // ─────────────────────────────────────────────────────────────────────

    static Vector3 PosicionEnSuelo(float xMin, float xMax, float zMin, float zMax, Terrain t)
    {
        float x = Random.Range(xMin, xMax);
        float z = Random.Range(zMin, zMax);
        float y = t.SampleHeight(new Vector3(x, 0, z)) + 0.01f;
        return new Vector3(x, y, z);
    }

    static void CrearDecal(string nombre, Vector3 pos, Material mat,
                           float tamano, Transform padre, float rotacionZ = 0f)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(90f, rotacionZ, 0f); // mirando hacia abajo

        var dp = go.AddComponent<DecalProjector>();
        dp.material  = mat;
        dp.size      = new Vector3(tamano, tamano, 2f);
        dp.pivot     = new Vector3(0, 0, 1f); // proyecta hacia abajo
        dp.fadeFactor = 1f;
        dp.drawDistance = 200f;
    }

    // =========================================================================
    //  MATERIALES (Shader.HDRP/Decal)
    // =========================================================================

    static void CrearMateriales()
    {
        var sh = Shader.Find("HDRP/Decal");
        if (sh == null) { Debug.LogError("[Decales] Shader HDRP/Decal no encontrado."); return; }

        _matAlcantarilla = new Material(sh) { name = "Dec_Alcantarilla" };
        _matAlcantarilla.SetTexture("_BaseColorMap",
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{TEX_DIR}/Alcantarilla.png"));
        _matAlcantarilla.SetColor("_BaseColor", Color.white);

        _matMancha = new Material(sh) { name = "Dec_Mancha" };
        _matMancha.SetTexture("_BaseColorMap",
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{TEX_DIR}/Mancha.png"));
        _matMancha.SetColor("_BaseColor", Color.white);

        _matGrieta = new Material(sh) { name = "Dec_Grieta" };
        _matGrieta.SetTexture("_BaseColorMap",
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{TEX_DIR}/Grieta.png"));
        _matGrieta.SetColor("_BaseColor", Color.white);

        _matPasoCebra = new Material(sh) { name = "Dec_PasoCebra" };
        _matPasoCebra.SetTexture("_BaseColorMap",
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{TEX_DIR}/PasoCebra.png"));
        _matPasoCebra.SetColor("_BaseColor", Color.white);
    }

    // =========================================================================
    //  TEXTURAS PROCEDURALES (si no existen)
    // =========================================================================

    static void GenerarTexturasSiHaceFalta()
    {
        if (!Directory.Exists(TEX_DIR)) Directory.CreateDirectory(TEX_DIR);

        TexturaAlcantarilla($"{TEX_DIR}/Alcantarilla.png");
        TexturaMancha($"{TEX_DIR}/Mancha.png");
        TexturaGrieta($"{TEX_DIR}/Grieta.png");
        TexturaPasoCebra($"{TEX_DIR}/PasoCebra.png");

        AssetDatabase.Refresh();
    }

    static void TexturaAlcantarilla(string path)
    {
        if (File.Exists(path)) return;
        int R = 256;
        var tex = new Texture2D(R, R, TextureFormat.RGBA32, false);
        var px  = new Color[R * R];
        Vector2 c = new Vector2(R / 2f, R / 2f);
        for (int y = 0; y < R; y++)
        for (int x = 0; x < R; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), c) / (R / 2f);
            float a = 0f;
            if (d > 0.85f && d < 0.98f) a = 0.9f; // anillo exterior
            if (d < 0.82f)
            {
                // patrón radial de barras
                float ang = Mathf.Atan2(y - c.y, x - c.x);
                if (Mathf.Sin(ang * 12f) > 0.7f) a = 0.6f;
                else a = 0.4f;
            }
            px[y * R + x] = new Color(0.1f, 0.1f, 0.1f, a);
        }
        tex.SetPixels(px); tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
    }

    static void TexturaMancha(string path)
    {
        if (File.Exists(path)) return;
        int R = 256;
        var tex = new Texture2D(R, R, TextureFormat.RGBA32, false);
        var px  = new Color[R * R];
        Vector2 c = new Vector2(R / 2f, R / 2f);
        for (int y = 0; y < R; y++)
        for (int x = 0; x < R; x++)
        {
            float d  = Vector2.Distance(new Vector2(x, y), c) / (R / 2f);
            float n  = Mathf.PerlinNoise(x * 0.05f, y * 0.05f);
            float a  = Mathf.Max(0f, 1f - d * 1.4f) * 0.7f * n;
            if (a < 0.05f) a = 0f;
            px[y * R + x] = new Color(0.05f, 0.04f, 0.03f, a);
        }
        tex.SetPixels(px); tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
    }

    static void TexturaGrieta(string path)
    {
        if (File.Exists(path)) return;
        int R = 256;
        var tex = new Texture2D(R, R, TextureFormat.RGBA32, false);
        var px  = new Color[R * R];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0,0,0,0);

        // 3-5 ramas desde el centro
        int ramas = Random.Range(3, 6);
        Vector2 c = new Vector2(R / 2f, R / 2f);
        for (int r = 0; r < ramas; r++)
        {
            float ang = (r * 2f * Mathf.PI / ramas) + Random.Range(-0.3f, 0.3f);
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 p = c;
            int largo = Random.Range(40, 110);
            for (int s = 0; s < largo; s++)
            {
                p += dir + new Vector2(Random.Range(-0.4f, 0.4f), Random.Range(-0.4f, 0.4f));
                int ix = Mathf.Clamp((int)p.x, 0, R - 1);
                int iy = Mathf.Clamp((int)p.y, 0, R - 1);
                // pixel + vecinos (línea de 2px ancho)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int qx = Mathf.Clamp(ix + dx, 0, R - 1);
                    int qy = Mathf.Clamp(iy + dy, 0, R - 1);
                    float a = (dx == 0 && dy == 0) ? 0.9f : 0.4f;
                    var actual = px[qy * R + qx];
                    if (a > actual.a)
                        px[qy * R + qx] = new Color(0.05f, 0.05f, 0.05f, a);
                }
            }
        }
        tex.SetPixels(px); tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
    }

    static void TexturaPasoCebra(string path)
    {
        if (File.Exists(path)) return;
        int W = 512, H = 256;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px  = new Color[W * H];
        const int FRANJAS = 6;
        int anchoFranja = W / (FRANJAS * 2);
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int franja = x / anchoFranja;
            bool blanco = (franja % 2) == 0;
            float a = blanco ? 0.95f : 0f;
            // ligero desgaste
            if (blanco && Random.value < 0.05f) a *= Random.Range(0.4f, 0.9f);
            px[y * W + x] = blanco ? new Color(0.95f, 0.95f, 0.92f, a) : new Color(0,0,0,0);
        }
        tex.SetPixels(px); tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
    }
}
#endif
