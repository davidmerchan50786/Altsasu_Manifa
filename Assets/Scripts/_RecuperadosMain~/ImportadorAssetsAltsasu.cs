// Assets/Scripts/Editor/ImportadorAssetsAltsasu.cs
// ═══════════════════════════════════════════════════════════════════════════
//  IMPORTADOR DE ASSETS — ALTSASU MANIFA
//
//  Menú: Altsasu GTA → Importar Assets → ...
//
//  Importa los paquetes de E:\assets que están copiados en PackagesToImport/.
//  Tras importar cada paquete, configura automáticamente los prefabs en escena.
//
//  ORDEN RECOMENDADO:
//   1. Naturaleza (árboles/arbustos)
//   2. Materiales de terreno
//   3. Casas / edificios
//   4. Accesorios de calle (farolas, vallas)
//   5. Cielo dinámico
//   6. → Ejecutar "Montar Escena Realista"
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using System.IO;

public static class ImportadorAssetsAltsasu
{
    // Carpeta donde están los .unitypackage (un nivel arriba de Assets)
    static string PkgDir => Path.Combine(
        Path.GetDirectoryName(Application.dataPath), "PackagesToImport");

    // ── Menús ─────────────────────────────────────────────────────────────────

    [MenuItem("Altsasu GTA/Importar Assets/1 - Naturaleza (árboles y arbustos)", false, 100)]
    static void ImportNaturaleza()
    {
        ImportarYConfigurar(
            "Free Low Poly Nature Forest.unitypackage",
            "Nature Starter Kit 2.unitypackage",
            "Yughues Free Bushes.unitypackage",
            "Idyllic Fantasy Nature.unitypackage"
        );
    }

    [MenuItem("Altsasu GTA/Importar Assets/2 - Texturas de terreno y materiales", false, 101)]
    static void ImportTerreno()
    {
        ImportarYConfigurar(
            "terrain_textures_free.unitypackage",
            "freerealisticoutdoormaterials.unitypackage",
            "mountain_terrain_rock_and_tree.unitypackage"
        );
    }

    [MenuItem("Altsasu GTA/Importar Assets/3 - Casas y edificios", false, 102)]
    static void ImportEdificios()
    {
        ImportarYConfigurar(
            "VILLAGE HOUSES PACK.unitypackage"
        );
    }

    [MenuItem("Altsasu GTA/Importar Assets/4 - Farolas y accesorios de calle", false, 103)]
    static void ImportCalle()
    {
        ImportarYConfigurar(
            "Street Lamps 2.unitypackage",
            "simple_street_props.unitypackage",
            "Realistic Fences Pack.unitypackage"
        );
    }

    [MenuItem("Altsasu GTA/Importar Assets/5 - Cielo dinámico", false, 104)]
    static void ImportCielo()
    {
        ImportarYConfigurar(
            "Fast Dynamic Sky.unitypackage"
        );
    }

    [MenuItem("Altsasu GTA/Importar Assets/TODOS (puede tardar varios minutos)", false, 120)]
    static void ImportTodos()
    {
        if (!EditorUtility.DisplayDialog("Importar todos los paquetes",
            "Esto importará ~12 paquetes de assets.\nPuede tardar 5-10 minutos.\n¿Continuar?",
            "Sí, importar todo", "Cancelar")) return;

        ImportarYConfigurar(
            "Free Low Poly Nature Forest.unitypackage",
            "Nature Starter Kit 2.unitypackage",
            "Yughues Free Bushes.unitypackage",
            "Idyllic Fantasy Nature.unitypackage",
            "terrain_textures_free.unitypackage",
            "freerealisticoutdoormaterials.unitypackage",
            "mountain_terrain_rock_and_tree.unitypackage",
            "VILLAGE HOUSES PACK.unitypackage",
            "Street Lamps 2.unitypackage",
            "simple_street_props.unitypackage",
            "Realistic Fences Pack.unitypackage",
            "Fast Dynamic Sky.unitypackage"
        );
    }

    [MenuItem("Altsasu GTA/Importar Assets/Ver qué paquetes hay disponibles", false, 130)]
    static void VerPaquetes()
    {
        if (!Directory.Exists(PkgDir))
        {
            EditorUtility.DisplayDialog("PackagesToImport no encontrado",
                $"No se encontró la carpeta:\n{PkgDir}\n\n" +
                "Ejecuta primero el script de setup de PowerShell.", "OK");
            return;
        }

        var pkgs = Directory.GetFiles(PkgDir, "*.unitypackage");
        string lista = pkgs.Length > 0
            ? string.Join("\n", System.Array.ConvertAll(pkgs, p => "• " + Path.GetFileName(p)))
            : "(ninguno)";

        EditorUtility.DisplayDialog($"Paquetes disponibles ({pkgs.Length})",
            $"En {PkgDir}:\n\n{lista}", "OK");
    }

    // ── Core ──────────────────────────────────────────────────────────────────

    static void ImportarYConfigurar(params string[] nombresPaquetes)
    {
        if (!Directory.Exists(PkgDir))
        {
            EditorUtility.DisplayDialog("PackagesToImport no encontrado",
                $"Carpeta esperada:\n{PkgDir}\n\n" +
                "Asegúrate de haber ejecutado el script de copia de PowerShell.", "OK");
            return;
        }

        bool alguno = false;
        foreach (string nombre in nombresPaquetes)
        {
            string rutaPkg = Path.Combine(PkgDir, nombre);
            if (!File.Exists(rutaPkg))
            {
                Debug.LogWarning($"[Importador] Paquete no encontrado: {rutaPkg}");
                continue;
            }
            Debug.Log($"[Importador] Importando: {nombre}");
            AssetDatabase.ImportPackage(rutaPkg, false); // false = sin diálogo interactivo
            alguno = true;
        }

        if (alguno)
        {
            AssetDatabase.Refresh();
            Debug.Log("[Importador] ✅ Importación completada. Ejecuta 'Montar Escena Realista' para aplicar.");
        }
    }
}
