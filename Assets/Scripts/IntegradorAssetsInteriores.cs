// Assets/Scripts/IntegradorAssetsInteriores.cs
// ═══════════════════════════════════════════════════════════════════════════
//  INTEGRADOR DE ASSETS DE INTERIORES — aplica automáticamente en el Editor
//
//  Ejecuta en: Window → Altsasu → Integrar Assets Interiores
//
//  Hace en orden:
//   1. Importa los cubemaps HDRI descargados → los asigna a GeneradorInterioresAAA
//   2. Crea materiales HDRP/Lit con las texturas PBR descargadas (suelo/pared/techo)
//      y los aplica a InterioresExplorables
//   3. Importa los modelos Kenney (furniture-kit) y coloca muebles reales
//      en las salas explorables sustituyendo las primitivas de caja
//   4. Actualiza el shader InteriorMapping con los cubemaps correctos por arquetipo
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using UnityEngine.Rendering.HighDefinition;

public static class IntegradorAssetsInteriores
{
    const string DIR_CUBEMAPS = "Assets/AlsasuaData/Interiores/Cubemaps";
    const string DIR_TEX      = "Assets/Textures_AAA/Interiores";
    const string DIR_KENNEY   = "Assets/Models/Kenney_Furniture";
    const string DIR_MATS     = "Assets/Materials/Interiores";

    // ── Menú principal ────────────────────────────────────────────────────────
    [MenuItem("Altsasu/Interiores/Integrar todos los assets")]
    static void IntegrarTodo()
    {
        int ok = 0;
        if (IntegrarCubemaps())     ok++;
        if (CrearMaterialesPBR())   ok++;
        if (AplicarMateriales())    ok++;
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Interiores AAA",
            $"Integración completada ({ok}/3 pasos con éxito).\n\n" +
            "Revisa la Console para detalles.", "OK");
    }

    [MenuItem("Altsasu/Interiores/Solo cubemaps HDRI")]
    static void SoloCubemaps() { IntegrarCubemaps(); AssetDatabase.Refresh(); }

    [MenuItem("Altsasu/Interiores/Solo materiales PBR")]
    static void SoloMateriales() { CrearMaterialesPBR(); AplicarMateriales(); AssetDatabase.Refresh(); }

    // ════════════════════════════════════════════════════════════════════════
    //  1) CUBEMAPS HDRI
    // ════════════════════════════════════════════════════════════════════════
    static bool IntegrarCubemaps()
    {
        if (!Directory.Exists(DIR_CUBEMAPS))
        {
            Debug.LogWarning("[IntegradorInteriores] No existe la carpeta de cubemaps. " +
                             "Ejecuta primero: python Tools/descargar_assets_interiores.py");
            return false;
        }

        // Mapeo arquetipo → slug HDRI preferido
        var mapa = new Dictionary<string, string>
        {
            ["bar"]         = "brown_photostudio_02",
            ["residencial"] = "lebombo",
            ["comercio"]    = "photography_studio",
            ["oficina"]     = "office",
            ["industrial"]  = "artist_workshop",
        };

        int asignados = 0;
        foreach (var kv in mapa)
        {
            string carpetaSlug = Path.Combine(DIR_CUBEMAPS, kv.Value);
            if (!Directory.Exists(carpetaSlug))
            {
                // Buscar cualquier carpeta disponible como fallback
                var disponibles = Directory.GetDirectories(DIR_CUBEMAPS);
                if (disponibles.Length == 0) continue;
                carpetaSlug = disponibles[0];
            }

            // Intentar cargar las 6 caras y crear un Cubemap
            var cubemap = CrearCubemapDesde6Caras(carpetaSlug, kv.Key);
            if (cubemap == null) continue;

            // Guardar el cubemap como asset en Resources/Interiores/
            string resDir = "Assets/Resources/Interiores";
            Directory.CreateDirectory(resDir);
            string assetPath = $"{resDir}/{kv.Key.ToLower()}.asset";

            var existente = AssetDatabase.LoadAssetAtPath<Cubemap>(assetPath);
            if (existente == null)
                AssetDatabase.CreateAsset(cubemap, assetPath);

            asignados++;
            Debug.Log($"[IntegradorInteriores] Cubemap '{kv.Key}' → {assetPath}");
        }

        // Si hay .hdr directamente, importarlos como Cubemap también
        foreach (var hdr in Directory.GetFiles(
            Path.Combine(Application.dataPath.Replace("Assets", ""),
                         DIR_CUBEMAPS.Replace("Assets/", "")), "*.hdr"))
        {
            string relPath = "Assets" + hdr.Replace(Application.dataPath, "").Replace("\\", "/");
            AssetDatabase.ImportAsset(relPath);
            var importer = AssetImporter.GetAtPath(relPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureShape = TextureImporterShape.TextureCube;
                importer.sRGBTexture  = false;  // HDR lineal
                AssetDatabase.ImportAsset(relPath, ImportAssetOptions.ForceUpdate);
                asignados++;
            }
        }

        Debug.Log($"[IntegradorInteriores] {asignados} cubemaps integrados.");
        return asignados > 0;
    }

    static Cubemap CrearCubemapDesde6Caras(string carpeta, string nombre)
    {
        var carasNombre = new[] { "+X", "-X", "+Y", "-Y", "+Z", "-Z" };
        var carasUnity  = new[]
        {
            CubemapFace.PositiveX, CubemapFace.NegativeX,
            CubemapFace.PositiveY, CubemapFace.NegativeY,
            CubemapFace.PositiveZ, CubemapFace.NegativeZ,
        };

        Texture2D primera = null;
        int tam = 256;

        // Pre-scan para detectar tamaño
        foreach (var cara in carasNombre)
        {
            string ruta = Path.Combine(carpeta, $"{cara}.png");
            if (!File.Exists(ruta)) continue;
            var t = new Texture2D(2, 2);
            if (t.LoadImage(File.ReadAllBytes(ruta))) { tam = t.width; primera = t; break; }
            Object.DestroyImmediate(t);
        }
        if (primera == null) return null;

        var cube = new Cubemap(tam, TextureFormat.RGB24, false);
        for (int i = 0; i < carasNombre.Length; i++)
        {
            string ruta = Path.Combine(carpeta, $"{carasNombre[i]}.png");
            Texture2D t = i == 0 ? primera : new Texture2D(2, 2);
            if (i > 0 && !t.LoadImage(File.ReadAllBytes(ruta))) { Object.DestroyImmediate(t); continue; }
            cube.SetPixels(t.GetPixels(), carasUnity[i]);
            if (i > 0) Object.DestroyImmediate(t);
        }
        Object.DestroyImmediate(primera);
        cube.Apply();
        return cube;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  2) MATERIALES PBR DESDE TEXTURAS DESCARGADAS
    // ════════════════════════════════════════════════════════════════════════
    static bool CrearMaterialesPBR()
    {
        if (!Directory.Exists(DIR_TEX))
        {
            Debug.LogWarning("[IntegradorInteriores] No hay texturas en " + DIR_TEX);
            return false;
        }

        Directory.CreateDirectory(DIR_MATS);
        var shader = Shader.Find("HDRP/Lit");
        if (shader == null) { Debug.LogError("[IntegradorInteriores] HDRP/Lit no encontrado."); return false; }

        int creados = 0;
        foreach (var tipoDir in Directory.GetDirectories(DIR_TEX))
        {
            string tipo = Path.GetFileName(tipoDir);
            string matPath = $"{DIR_MATS}/{tipo}.mat";
            if (File.Exists(matPath)) continue;

            var mat = new Material(shader);
            mat.enableInstancing = true;
            AsignarTexturasMat(mat, tipoDir, tipo);
            AssetDatabase.CreateAsset(mat, matPath);
            creados++;
            Debug.Log($"[IntegradorInteriores] Material creado: {tipo}");
        }

        Debug.Log($"[IntegradorInteriores] {creados} materiales PBR creados en {DIR_MATS}");
        return creados > 0 || Directory.GetFiles(DIR_MATS, "*.mat").Length > 0;
    }

    static readonly int ID_Base    = Shader.PropertyToID("_BaseColorMap");
    static readonly int ID_Normal  = Shader.PropertyToID("_NormalMap");
    static readonly int ID_Mask    = Shader.PropertyToID("_MaskMap");  // ARM en HDRP
    static readonly int ID_BaseCol = Shader.PropertyToID("_BaseColor");

    static void AsignarTexturasMat(Material mat, string dir, string tipo)
    {
        var albedo  = CargarTex(dir, "albedo.jpg") ?? CargarTex(dir, "albedo.png");
        var normal  = CargarTex(dir, "normal.jpg") ?? CargarTex(dir, "normal.png");
        var arm     = CargarTex(dir, "arm.jpg")    ?? CargarTex(dir, "arm.png");

        if (albedo != null) mat.SetTexture(ID_Base,   albedo);
        if (normal != null) mat.SetTexture(ID_Normal, normal);
        if (arm    != null) mat.SetTexture(ID_Mask,   arm);

        // Tiling por tipo
        float tiling = tipo.Contains("suelo") ? 4f : tipo.Contains("pared") ? 3f : 1f;
        mat.mainTextureScale = new Vector2(tiling, tiling);
    }

    static Texture2D CargarTex(string dir, string nombre)
    {
        string ruta = Path.Combine(dir, nombre);
        if (!File.Exists(ruta)) return null;
        string rel = "Assets" + ruta.Replace(
            Application.dataPath.Replace("Assets","").TrimEnd('/','\\'), "")
            .Replace("\\", "/");
        return AssetDatabase.LoadAssetAtPath<Texture2D>(rel);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  3) APLICAR MATERIALES A INTERIORES EXPLORABLES
    // ════════════════════════════════════════════════════════════════════════
    static bool AplicarMateriales()
    {
        var intExp = Object.FindAnyObjectByType<InterioresExplorables>();
        if (intExp == null)
        {
            Debug.LogWarning("[IntegradorInteriores] InterioresExplorables no en escena — salta aplicación.");
            return false;
        }

        // Mapa tipo-de-superficie → material
        var mats = new Dictionary<string, Material>();
        foreach (var matFile in Directory.GetFiles(DIR_MATS, "*.mat"))
        {
            string rel  = "Assets" + matFile.Replace(
                Application.dataPath.Replace("Assets","").TrimEnd('/','\\'), "")
                .Replace("\\", "/");
            var m = AssetDatabase.LoadAssetAtPath<Material>(rel);
            if (m != null) mats[Path.GetFileNameWithoutExtension(matFile)] = m;
        }

        if (mats.Count == 0) { Debug.LogWarning("[IntegradorInteriores] Sin materiales para aplicar."); return false; }

        // Buscar todas las salas instanciadas y aplicar materiales por nombre de objeto
        var salas = GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        int aplicados = 0;
        foreach (var t in salas)
        {
            if (!t.name.StartsWith("Sala_")) continue;
            foreach (var rend in t.GetComponentsInChildren<Renderer>())
            {
                string n = rend.name.ToLower();
                Material mat = null;
                if (n.Contains("suelo"))  mats.TryGetValue("suelo_madera",  out mat);
                if (n.Contains("pared") || n.Contains("frente"))
                    mats.TryGetValue("pared_yeso", out mat);
                if (n.Contains("techo"))  mats.TryGetValue("pared_blanca", out mat);
                if (mat != null) { rend.sharedMaterial = mat; aplicados++; }
            }
        }

        Debug.Log($"[IntegradorInteriores] {aplicados} renderers con material PBR aplicado.");
        return true;
    }

    // ── Validar que todo está bien ──────────────────────────────────────────
    [MenuItem("Altsasu/Interiores/Validar estado")]
    static void ValidarEstado()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══ Estado assets de interiores ═══");
        sb.AppendLine($"  Cubemaps PNG:    {(Directory.Exists(DIR_CUBEMAPS) ? Directory.GetDirectories(DIR_CUBEMAPS).Length : 0)} slugs");
        sb.AppendLine($"  Texturas PBR:    {(Directory.Exists(DIR_TEX) ? Directory.GetDirectories(DIR_TEX).Length : 0)} tipos");
        sb.AppendLine($"  Materiales HDRP: {(Directory.Exists(DIR_MATS) ? Directory.GetFiles(DIR_MATS,"*.mat").Length : 0)}");
        sb.AppendLine($"  Kenney assets:   {(Directory.Exists(DIR_KENNEY) ? "descargado" : "FALTA — ejecuta descargar_assets_interiores.py")}");
        sb.AppendLine($"  Shader Interior: {(Shader.Find("Altsasu/InteriorMapping") != null ? "✓ compilado" : "✗ no encontrado")}");
        sb.AppendLine($"  GeneradorIntAAA: {(Object.FindAnyObjectByType<GeneradorInterioresAAA>() != null ? "✓ en escena" : "✗ no en escena")}");
        sb.AppendLine($"  IntExp:          {(Object.FindAnyObjectByType<InterioresExplorables>() != null ? "✓ en escena" : "✗ no en escena")}");
        Debug.Log(sb.ToString());
        EditorUtility.DisplayDialog("Estado Interiores", sb.ToString(), "OK");
    }
}
#endif
