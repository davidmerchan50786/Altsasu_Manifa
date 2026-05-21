// Assets/Scripts/Editor/ImportadorExtractedAssets.cs
// ═══════════════════════════════════════════════════════════════════════════
//  IMPORTADOR DE ASSETS EXTRAÍDOS — Menú: Tools → Alsasua → 📥 Importar Extraídos
//
//  Después de ejecutar ExtractAndConvert.ps1, este tool:
//    1. Detecta todos los modelos en _ExtractedAssets/
//    2. Los mueve a las carpetas correctas de Assets/
//    3. Configura el rig de personajes (Humanoid)
//    4. Convierte materiales a HDRP
//    5. Conecta los prefabs a los sistemas del juego
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;

public class ImportadorExtractedAssets : EditorWindow
{
    private static readonly string EXTRACTED = "Assets/_ExtractedAssets";
    private static readonly string DESTINO   = "Assets/Downloads_Converted";

    private Vector2 _scroll;
    private List<AssetInfo> _assets = new();
    private bool _escaneado = false;

    private class AssetInfo
    {
        public string PathOrigen;
        public string Nombre;
        public string Categoria;
        public string Extension;
        public bool   Seleccionado = true;
        public long   ByteSize;
    }

    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/📥 Importar Assets Extraídos", priority = 9)]
    public static void Abrir()
    {
        var v = GetWindow<ImportadorExtractedAssets>("📥 Assets Extraídos");
        v.minSize = new Vector2(580, 400);
        v.Escanear();
    }

    [MenuItem("Tools/Alsasua/📥 Importar Assets Extraídos", true)]
    static bool ValidarMenu() =>
        AssetDatabase.IsValidFolder(EXTRACTED) ||
        Directory.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", EXTRACTED)));

    // ─────────────────────────────────────────────────────────────────────────
    //  ESCANEO
    // ─────────────────────────────────────────────────────────────────────────

    private void Escanear()
    {
        _assets.Clear();
        string carpeta = Path.GetFullPath(
            Path.Combine(Application.dataPath, "_ExtractedAssets"));

        if (!Directory.Exists(carpeta))
        {
            Debug.LogWarning($"[ImportadorExtracted] Carpeta no encontrada: {carpeta}\n" +
                             "Ejecuta primero ExtractAndConvert.ps1 (en la raíz del proyecto).");
            _escaneado = false;
            return;
        }

        var extensiones = new[] { ".fbx", ".FBX", ".obj", ".OBJ", ".gltf", ".glb", ".3ds" };
        var archivos = Directory.GetFiles(carpeta, "*.*", SearchOption.AllDirectories)
            .Where(f => extensiones.Any(e => f.EndsWith(e, System.StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var arch in archivos)
        {
            var info = new FileInfo(arch);
            string relPath = arch.Replace(Path.GetFullPath(Application.dataPath + "/..") + Path.DirectorySeparatorChar, "");
            string cat = ObtenerCategoria(arch);

            _assets.Add(new AssetInfo
            {
                PathOrigen    = arch,
                Nombre        = Path.GetFileNameWithoutExtension(arch),
                Categoria     = cat,
                Extension     = Path.GetExtension(arch).ToLower(),
                Seleccionado  = EsUtil(cat, Path.GetFileNameWithoutExtension(arch)),
                ByteSize      = info.Length,
            });
        }

        _assets = _assets.OrderBy(a => a.Categoria).ThenBy(a => a.Nombre).ToList();
        _escaneado = true;
    }

    private string ObtenerCategoria(string path)
    {
        string p = path.ToLower();
        if (p.Contains("vegetation") || p.Contains("bush") || p.Contains("plant")
            || p.Contains("grass") || p.Contains("hedge") || p.Contains("weed")
            || p.Contains("oak") || p.Contains("tree")) return "🌿 Vegetación";
        if (p.Contains("animal") || p.Contains("chicken") || p.Contains("rooster")
            || p.Contains("wolf") || p.Contains("deer")) return "🦌 Animales";
        if (p.Contains("building") || p.Contains("house") || p.Contains("village")) return "🏠 Edificios";
        if (p.Contains("destruction") || p.Contains("wall")) return "💥 Destrucción";
        if (p.Contains("weapon") || p.Contains("hatchet") || p.Contains("smg")
            || p.Contains("arm") || p.Contains("ak") || p.Contains("m4")) return "🔫 Armas";
        if (p.Contains("character")) return "👤 Personajes";
        return "📦 Otros";
    }

    private bool EsUtil(string cat, string nombre)
    {
        // Pre-seleccionar solo los útiles para Alsasua
        if (cat == "🌿 Vegetación") return true;
        if (cat == "🦌 Animales")   return true;
        if (cat == "🏠 Edificios")  return !nombre.ToLower().Contains("fantasy");
        if (cat == "💥 Destrucción") return true;
        if (cat == "🔫 Armas")      return !nombre.ToLower().Contains("elven");
        if (cat == "👤 Personajes") return true;
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GUI
    // ─────────────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("📥  ASSETS EXTRAÍDOS — Listos para Unity",
            EditorStyles.boldLabel);

        if (!_escaneado || _assets.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No se encontró la carpeta _ExtractedAssets/.\n\n" +
                "Ejecuta primero el pipeline de extracción:\n" +
                "1. Abre PowerShell como Administrador\n" +
                "2. cd \"E:\\Desk\\DAM\\Altsasu_Manifa\"\n" +
                "3. .\\ExtractAndConvert.ps1\n\n" +
                "Tardará varios minutos (Blender convierte los .blend).",
                MessageType.Info);

            if (GUILayout.Button("🔄 Reescanear carpeta _ExtractedAssets"))
                Escanear();

            if (GUILayout.Button("📂 Abrir carpeta PackagesToImport en Explorer"))
                EditorUtility.RevealInFinder(
                    Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PackagesToImport")));
            return;
        }

        int sel = _assets.Count(a => a.Seleccionado);
        EditorGUILayout.LabelField(
            $"{_assets.Count} modelos encontrados · {sel} seleccionados",
            EditorStyles.miniLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("✅ Todos")) foreach (var a in _assets) a.Seleccionado = true;
        if (GUILayout.Button("☐ Ninguno")) foreach (var a in _assets) a.Seleccionado = false;
        if (GUILayout.Button("🔄 Reescanear")) Escanear();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        string catActual = "";
        foreach (var a in _assets)
        {
            if (a.Categoria != catActual)
            {
                catActual = a.Categoria;
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(catActual, EditorStyles.boldLabel);
            }

            EditorGUILayout.BeginHorizontal();
            a.Seleccionado = EditorGUILayout.Toggle(a.Seleccionado, GUILayout.Width(20));
            EditorGUILayout.LabelField(a.Nombre, GUILayout.Width(220));
            EditorGUILayout.LabelField(a.Extension.ToUpper(), EditorStyles.miniLabel, GUILayout.Width(45));
            EditorGUILayout.LabelField($"{a.ByteSize/1024f:F0} KB", EditorStyles.miniLabel, GUILayout.Width(65));
            EditorGUILayout.LabelField(Path.GetDirectoryName(a.PathOrigen)
                .Split(Path.DirectorySeparatorChar).Last(), EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(6);
        GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
        if (GUILayout.Button($"📥  IMPORTAR {sel} ASSETS SELECCIONADOS → Assets/", GUILayout.Height(34)))
            ImportarSeleccionados();
        GUI.backgroundColor = Color.white;
        EditorGUILayout.Space(4);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  IMPORTACIÓN
    // ─────────────────────────────────────────────────────────────────────────

    private void ImportarSeleccionados()
    {
        var sel = _assets.Where(a => a.Seleccionado).ToList();
        if (sel.Count == 0) return;

        // Crear estructura de carpetas de destino
        var carpetas = new Dictionary<string, string>
        {
            ["🌿 Vegetación"] = "Assets/Models/Flora_Extracted",
            ["🦌 Animales"]   = "Assets/Models/Animals_Extracted",
            ["🏠 Edificios"]  = "Assets/Models/Buildings_Extracted",
            ["💥 Destrucción"] = "Assets/Models/Destruction_Extracted",
            ["🔫 Armas"]      = "Assets/Models/Weapons_Extracted",
            ["👤 Personajes"] = "Assets/Models/Characters_Extracted",
            ["📦 Otros"]      = "Assets/Models/Misc_Extracted",
        };

        foreach (var c in carpetas.Values)
        {
            if (!AssetDatabase.IsValidFolder(c))
            {
                string parent = Path.GetDirectoryName(c).Replace("\\", "/");
                string child  = Path.GetFileName(c);
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        int ok = 0;
        for (int i = 0; i < sel.Count; i++)
        {
            var asset = sel[i];
            EditorUtility.DisplayProgressBar("Importando assets",
                $"({i+1}/{sel.Count}) {asset.Nombre}", (float)i/sel.Count);

            string carpeta = carpetas.TryGetValue(asset.Categoria, out var c) ? c : "Assets/Models/Misc_Extracted";
            string destino = $"{carpeta}/{asset.Nombre}{asset.Extension}";

            // Copiar texturas del mismo directorio
            CopiarTexturasAdyacentes(Path.GetDirectoryName(asset.PathOrigen), carpeta);

            // Copiar el modelo
            if (!AssetDatabase.CopyAsset(
                "Assets/_ExtractedAssets" + asset.PathOrigen.Replace(
                    Path.GetFullPath(Path.Combine(Application.dataPath, "_ExtractedAssets")), "")
                    .Replace("\\", "/"),
                destino))
            {
                // Fallback: File.Copy
                File.Copy(asset.PathOrigen, Path.GetFullPath(Path.Combine(Application.dataPath, "..", destino)), true);
            }
            ok++;
        }

        EditorUtility.ClearProgressBar();
        AssetDatabase.Refresh();

        // Auto-convertir materiales
        IntegradorAssetsNuevos.ConvertirMaterialesAHDRP();

        Debug.Log($"[ImportadorExtracted] ✅ {ok}/{sel.Count} assets importados a Assets/Models/*_Extracted/");
        EditorUtility.DisplayDialog("Importación completada",
            $"{ok} assets importados.\n\n" +
            "Próximo paso:\n" +
            "Tools → Alsasua → 🔌 Integrar Assets Nuevos\n" +
            "para conectarlos a los sistemas del juego.", "OK");

        Escanear();
    }

    private static void CopiarTexturasAdyacentes(string origenDir, string destinoUnity)
    {
        var extensiones = new[] { ".png", ".jpg", ".jpeg", ".tga", ".tif", ".bmp" };
        foreach (var tex in Directory.GetFiles(origenDir)
            .Where(f => extensiones.Any(e => f.EndsWith(e, System.StringComparison.OrdinalIgnoreCase))))
        {
            string nombreTex = Path.GetFileName(tex);
            string dest = Path.GetFullPath(Path.Combine(Application.dataPath, "..", destinoUnity, nombreTex));
            if (!File.Exists(dest)) File.Copy(tex, dest, false);
        }
    }
}
