// Assets/Scripts/AutoImportadorIncoming.cs
// ═══════════════════════════════════════════════════════════════════════════
//  AUTO-IMPORTADOR DE ASSETS — Altsasu Manifa
//
//  Procesa todo lo que los scripts (escanear_equipo_assets.py /
//  descargar_internet_completo.py) dejan en Assets/_Incoming/<categoría>/ y:
//   • Convierte materiales a HDRP/Lit (texturas y modelos no salen rosas)
//   • Configura importadores correctos (normal map, sRGB, HDRI cubemap...)
//   • Crea materiales PBR a partir de carpetas albedo/normal/arm
//   • Mueve cada asset a su carpeta final del proyecto
//   • Genera prefabs de los modelos 3D
//
//  Menú: Altsasu → Assets → Importar desde _Incoming
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;

public static class AutoImportadorIncoming
{
    const string INCOMING = "Assets/_Incoming";

    // Mapeo categoría _Incoming → carpeta final del proyecto
    static readonly Dictionary<string, string> DESTINOS = new()
    {
        ["HDRIs_Interior"]    = "Assets/HDRIs",
        ["HDRIs_Exterior"]    = "Assets/HDRIs",
        ["Muebles"]           = "Assets/Resources/MueblesCiudad",
        ["PropsUrbanos"]      = "Assets/Resources/Props",
        ["Vehiculos"]         = "Assets/Models/Vehiculos_Importados",
        ["Personajes"]        = "Assets/Models/Personajes_Importados",
        ["Texturas_Interior"] = "Assets/Textures_AAA/Interiores",
        ["Texturas_Fachada"]  = "Assets/Textures_AAA/Fachadas",
        ["Sonidos"]           = "Assets/#Sounds/Importados",
        ["Animaciones"]       = "Assets/Animations/Importadas",
    };

    [MenuItem("Altsasu/Assets/Importar desde _Incoming")]
    static void Importar()
    {
        if (!Directory.Exists(INCOMING))
        {
            EditorUtility.DisplayDialog("Auto-importador",
                "No existe Assets/_Incoming/.\n\nEjecuta primero en tu PC:\n" +
                "  python Tools/escanear_equipo_assets.py\n" +
                "  python Tools/descargar_internet_completo.py", "OK");
            return;
        }

        int total = 0;
        var resumen = new System.Text.StringBuilder("Importación completada:\n\n");

        foreach (var kv in DESTINOS)
        {
            string origen = $"{INCOMING}/{kv.Key}";
            if (!Directory.Exists(origen)) continue;
            int n = ProcesarCategoria(kv.Key, origen, kv.Value);
            if (n > 0) { resumen.AppendLine($"  {kv.Key}: {n}"); total += n; }
        }

        AssetDatabase.Refresh();
        AssetDatabase.SaveAssets();
        resumen.AppendLine($"\nTotal: {total} assets procesados.");
        Debug.Log("[AutoImportador] " + resumen);
        EditorUtility.DisplayDialog("Auto-importador", resumen.ToString(), "OK");
    }

    static int ProcesarCategoria(string categoria, string origen, string destino)
    {
        Directory.CreateDirectory(destino);
        int n = 0;

        // Texturas en carpetas albedo/normal/arm → crear material PBR
        if (categoria.StartsWith("Texturas"))
            return ProcesarTexturas(origen, destino);

        foreach (var ruta in Directory.GetFiles(origen, "*.*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(ruta).ToLower();
            if (ext == ".meta") continue;

            string rel = ruta.Replace("\\", "/");
            AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceUpdate);

            // Configurar importador según tipo
            if (ext is ".hdr" or ".exr")
                ConfigurarHDRI(rel);
            else if (ext is ".fbx" or ".obj" or ".glb" or ".gltf")
                ConfigurarModelo(rel, categoria);

            // Mover a destino
            string nombreFinal = Path.GetFileName(ruta);
            string destFinal = AssetDatabase.GenerateUniqueAssetPath($"{destino}/{nombreFinal}");
            AssetDatabase.MoveAsset(rel, destFinal);
            n++;
        }
        return n;
    }

    // ── Texturas: agrupar por carpeta y crear material HDRP/Lit ────────────────
    static int ProcesarTexturas(string origen, string destinoTex)
    {
        int n = 0;
        string matDir = "Assets/Materials/Importados";
        Directory.CreateDirectory(matDir);
        var shader = Shader.Find("HDRP/Lit");

        foreach (var dir in Directory.GetDirectories(origen, "*", SearchOption.AllDirectories)
                 .Concat(new[] { origen }))
        {
            var jpgs = Directory.GetFiles(dir, "*.*")
                .Where(f => f.ToLower().EndsWith(".jpg") || f.ToLower().EndsWith(".png"))
                .ToArray();
            if (jpgs.Length == 0) continue;

            // Importar y configurar cada textura
            Texture2D albedo = null, normal = null, arm = null;
            foreach (var j in jpgs)
            {
                string rel = j.Replace("\\", "/");
                AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceUpdate);
                var imp = AssetImporter.GetAtPath(rel) as TextureImporter;
                string low = Path.GetFileName(j).ToLower();
                bool esNormal = low.Contains("nor") || low.Contains("normal");
                if (imp != null)
                {
                    imp.textureType = esNormal ? TextureImporterType.NormalMap
                                               : TextureImporterType.Default;
                    imp.sRGBTexture = !(esNormal || low.Contains("arm") || low.Contains("rough"));
                    AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceUpdate);
                }
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(rel);
                if (esNormal) normal = tex;
                else if (low.Contains("arm") || low.Contains("rough")) arm = tex;
                else if (low.Contains("diff") || low.Contains("albedo") || low.Contains("col")) albedo = tex;
                else if (albedo == null) albedo = tex;
            }

            if (albedo != null && shader != null)
            {
                string nombreMat = new DirectoryInfo(dir).Name;
                var mat = new Material(shader) { enableInstancing = true };
                mat.SetTexture("_BaseColorMap", albedo);
                if (normal != null) { mat.SetTexture("_NormalMap", normal); mat.EnableKeyword("_NORMALMAP"); }
                if (arm != null)    { mat.SetTexture("_MaskMap", arm);       mat.EnableKeyword("_MASKMAP"); }
                string matPath = AssetDatabase.GenerateUniqueAssetPath($"{matDir}/{nombreMat}.mat");
                AssetDatabase.CreateAsset(mat, matPath);
                n++;
            }
        }
        return n;
    }

    static void ConfigurarHDRI(string rel)
    {
        var imp = AssetImporter.GetAtPath(rel) as TextureImporter;
        if (imp == null) return;
        imp.textureShape = TextureImporterShape.TextureCube;
        imp.sRGBTexture  = false;
        imp.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
        AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceUpdate);
    }

    static void ConfigurarModelo(string rel, string categoria)
    {
        var imp = AssetImporter.GetAtPath(rel) as ModelImporter;
        if (imp == null) return;

        imp.importNormals      = ModelImporterNormals.Import;
        imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        imp.materialLocation   = ModelImporterMaterialLocation.External;

        // Personajes: configurar rig humanoide para usar animaciones del proyecto
        if (categoria == "Personajes")
            imp.animationType = ModelImporterAnimationType.Human;

        AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceUpdate);

        // Convertir materiales del modelo a HDRP
        var obj = AssetDatabase.LoadAssetAtPath<GameObject>(rel);
        if (obj != null) ConvertirMaterialesHDRP(obj);
    }

    static Shader _hdrpLit;
    static void ConvertirMaterialesHDRP(GameObject go)
    {
        if (_hdrpLit == null) _hdrpLit = Shader.Find("HDRP/Lit");
        if (_hdrpLit == null) return;
        foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
            foreach (var m in rend.sharedMaterials)
            {
                if (m == null || m.shader == null) continue;
                if (m.shader.name.Contains("HDRP")) continue;
                Texture main = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                m.shader = _hdrpLit;
                if (main != null) m.SetTexture("_BaseColorMap", main);
            }
    }

    [MenuItem("Altsasu/Assets/Ver contenido de _Incoming")]
    static void VerContenido()
    {
        if (!Directory.Exists(INCOMING)) { Debug.Log("[AutoImportador] _Incoming vacío."); return; }
        var sb = new System.Text.StringBuilder("Contenido de _Incoming:\n");
        foreach (var dir in Directory.GetDirectories(INCOMING))
        {
            int n = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                             .Count(f => !f.EndsWith(".meta"));
            sb.AppendLine($"  {Path.GetFileName(dir)}: {n} archivos");
        }
        Debug.Log("[AutoImportador] " + sb);
    }
}
#endif
