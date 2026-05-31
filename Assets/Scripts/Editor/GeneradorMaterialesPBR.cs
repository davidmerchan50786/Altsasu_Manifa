#if UNITY_EDITOR
// Assets/Scripts/Editor/GeneradorMaterialesPBR.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GENERADOR DE MATERIALES HDRP A PARTIR DE TEXTURAS PBR DESCARGADAS
//
//  Lee las carpetas de Assets/AlsasuaData/Textures/PBR/* (descargadas por
//  Tools/descargar_materiales_pbr.py) y crea un HDRP/Lit material por cada una
//  con todos los mapas asignados: Albedo, Normal, MaskMap (Metallic/AO/Detail/Smoothness),
//  Height (POM) y AO.
//
//  Salida: Assets/AlsasuaData/Materials/PBR/*.mat
//
//  Después AsignadorMaterialesAAA usará estos materiales PBR en lugar de los procedurales.
// ═══════════════════════════════════════════════════════════════════════════

using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

public static class GeneradorMaterialesPBR
{
    const string SRC_DIR = "Assets/AlsasuaData/Textures/PBR";
    const string OUT_DIR = "Assets/AlsasuaData/Materials/PBR";

    [MenuItem("Altsasu GTA/Utilidades/★ Crear Materiales PBR desde Texturas", false, 360)]
    public static void Crear()
    {
        if (!Directory.Exists(SRC_DIR))
        {
            EditorUtility.DisplayDialog("Sin texturas PBR",
                "No existe " + SRC_DIR + ".\n\n" +
                "Ejecuta primero:\n" +
                "  cd Tools && python descargar_materiales_pbr.py", "OK");
            return;
        }

        if (!Directory.Exists(OUT_DIR))
        {
            Directory.CreateDirectory(OUT_DIR);
            AssetDatabase.Refresh();
        }

        var carpetas = Directory.GetDirectories(SRC_DIR);
        if (carpetas.Length == 0)
        {
            EditorUtility.DisplayDialog("Vacío",
                "La carpeta " + SRC_DIR + " no contiene subcarpetas con texturas.", "OK");
            return;
        }

        var shader = Shader.Find("HDRP/Lit");
        if (shader == null)
        {
            EditorUtility.DisplayDialog("HDRP no encontrado",
                "Shader 'HDRP/Lit' no disponible. ¿El proyecto está configurado con HDRP?", "OK");
            return;
        }

        int creados = 0, errores = 0;
        try
        {
            for (int i = 0; i < carpetas.Length; i++)
            {
                EditorUtility.DisplayProgressBar("Materiales PBR",
                    $"{i + 1}/{carpetas.Length}", (float)i / carpetas.Length);

                try
                {
                    if (ProcesarCarpeta(carpetas[i], shader)) creados++;
                    else errores++;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[PBR] Error en {carpetas[i]}: {e.Message}");
                    errores++;
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("✅ Materiales PBR creados",
            $"Materiales generados: {creados}\n" +
            $"Errores: {errores}\n\n" +
            $"Salida: {OUT_DIR}\n\n" +
            "Siguiente paso: re-ejecuta el Paso 8 (Aplicar Materiales AAA).", "OK");
    }

    // ─────────────────────────────────────────────────────────────────────

    static bool ProcesarCarpeta(string carpeta, Shader shader)
    {
        string nombreMat = Path.GetFileName(carpeta);
        var ficheros = Directory.GetFiles(carpeta, "*.jpg")
                              .Concat(Directory.GetFiles(carpeta, "*.png"))
                              .ToArray();

        if (ficheros.Length == 0) return false;

        // Localizar los mapas por sufijo
        string albedo  = ficheros.FirstOrDefault(f => f.Contains("_Color"));
        string normal  = ficheros.FirstOrDefault(f => f.Contains("_NormalGL"));
        string rough   = ficheros.FirstOrDefault(f => f.Contains("_Roughness"));
        string ao      = ficheros.FirstOrDefault(f => f.Contains("_AmbientOcclusion"));
        string height  = ficheros.FirstOrDefault(f => f.Contains("_Displacement"));
        string metal   = ficheros.FirstOrDefault(f => f.Contains("_Metalness"));

        if (albedo == null)
        {
            Debug.LogWarning($"[PBR] Sin Color map en {carpeta} — omitida.");
            return false;
        }

        // Configurar importadores Unity: Albedo = sRGB, demás = linear
        ConfigurarImporter(albedo, sRGB: true,  esNormal: false);
        ConfigurarImporter(normal, sRGB: false, esNormal: true);
        ConfigurarImporter(rough,  sRGB: false, esNormal: false);
        ConfigurarImporter(ao,     sRGB: false, esNormal: false);
        ConfigurarImporter(height, sRGB: false, esNormal: false);
        ConfigurarImporter(metal,  sRGB: false, esNormal: false);

        // Crear material HDRP/Lit
        var mat = new Material(shader) { name = nombreMat };

        // Color base
        var texAlb = AssetDatabase.LoadAssetAtPath<Texture2D>(albedo);
        mat.SetTexture("_BaseColorMap", texAlb);
        mat.SetColor("_BaseColor", Color.white);

        // Normal map
        if (normal != null)
        {
            var texN = AssetDatabase.LoadAssetAtPath<Texture2D>(normal);
            mat.SetTexture("_NormalMap", texN);
            mat.SetFloat("_NormalScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
        }

        // MaskMap HDRP combina: R=Metallic, G=AO, B=DetailMask, A=Smoothness(=1-Rough)
        // Construir uno combinado a partir de rough/ao/metal
        if (rough != null || ao != null || metal != null)
        {
            var mask = ConstruirMaskMap(rough, ao, metal, carpeta, nombreMat);
            if (mask != null)
            {
                mat.SetTexture("_MaskMap", mask);
                mat.EnableKeyword("_MASKMAP");
            }
        }

        // Height map para Parallax Occlusion Mapping (mejora 3D notable)
        if (height != null)
        {
            var texH = AssetDatabase.LoadAssetAtPath<Texture2D>(height);
            mat.SetTexture("_HeightMap", texH);
            mat.SetFloat("_HeightAmplitude", 0.02f); // 2cm de relieve, sutil
            mat.SetFloat("_HeightCenter", 0.5f);
            mat.SetFloat("_DisplacementMode", 2); // Pixel Displacement (POM)
            mat.EnableKeyword("_HEIGHTMAP");
            mat.EnableKeyword("_PIXEL_DISPLACEMENT");
        }

        // Smoothness controlado por MaskMap.a; sin slider extra
        mat.SetFloat("_Smoothness", 1f);
        mat.SetFloat("_Metallic", 0f);

        // Tile por defecto (1m). Los grandes (Plaza adoquines) se ajustan en AsignadorMateriales
        mat.SetTextureScale("_BaseColorMap", new Vector2(1f, 1f));

        // Guardar
        string ruta = $"{OUT_DIR}/M_{nombreMat}.mat";
        AssetDatabase.CreateAsset(mat, ruta);
        return true;
    }

    // Combina Roughness, AO, Metalness en una sola textura RGBA con el formato HDRP MaskMap
    static Texture2D ConstruirMaskMap(string roughPath, string aoPath, string metalPath,
                                       string carpeta, string nombre)
    {
        var rough = roughPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(roughPath) : null;
        var ao    = aoPath    != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath)    : null;
        var metal = metalPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(metalPath) : null;

        if (rough == null && ao == null && metal == null) return null;

        // Asegurar que las texturas son readable temporalmente
        HacerReadable(roughPath); HacerReadable(aoPath); HacerReadable(metalPath);
        rough = roughPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(roughPath) : null;
        ao    = aoPath    != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath)    : null;
        metal = metalPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(metalPath) : null;

        // Resolución de referencia: la del rough/ao/metal (la primera que exista)
        Texture2D refTex = rough ?? ao ?? metal;
        int w = refTex.width;
        int h = refTex.height;
        var mask = new Texture2D(w, h, TextureFormat.RGBA32, true, true);

        var roughPx = rough != null ? rough.GetPixels() : null;
        var aoPx    = ao    != null ? ao.GetPixels()    : null;
        var metalPx = metal != null ? metal.GetPixels() : null;

        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++)
        {
            float r = metalPx != null && i < metalPx.Length ? metalPx[i].r : 0f;  // Metallic
            float g = aoPx    != null && i < aoPx.Length    ? aoPx[i].r    : 1f;  // AO
            float b = 0f;                                                          // Detail mask
            float a = roughPx != null && i < roughPx.Length ? 1f - roughPx[i].r    // Smoothness = 1-Rough
                                                            : 0.5f;
            px[i] = new Color(r, g, b, a);
        }
        mask.SetPixels(px);
        mask.Apply(true, false);

        // Guardar PNG
        string maskPath = $"{carpeta}/{nombre}_MaskMap.png";
        File.WriteAllBytes(maskPath, mask.EncodeToPNG());
        AssetDatabase.ImportAsset(maskPath);

        // Configurar como lineal (no sRGB) — es un mask, no color
        var imp = AssetImporter.GetAtPath(maskPath) as TextureImporter;
        if (imp != null)
        {
            imp.sRGBTexture = false;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
    }

    static void HacerReadable(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null && !imp.isReadable)
        {
            imp.isReadable = true;
            imp.SaveAndReimport();
        }
    }

    static void ConfigurarImporter(string path, bool sRGB, bool esNormal)
    {
        if (string.IsNullOrEmpty(path)) return;
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return;

        bool dirty = false;
        if (esNormal && imp.textureType != TextureImporterType.NormalMap)
        {
            imp.textureType = TextureImporterType.NormalMap;
            dirty = true;
        }
        else if (!esNormal && imp.textureType != TextureImporterType.Default)
        {
            imp.textureType = TextureImporterType.Default;
            dirty = true;
        }

        if (imp.sRGBTexture != sRGB)
        {
            imp.sRGBTexture = sRGB;
            dirty = true;
        }

        if (imp.maxTextureSize < 2048)
        {
            imp.maxTextureSize = 2048;
            dirty = true;
        }

        if (dirty) imp.SaveAndReimport();
    }
}
#endif
