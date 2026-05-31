#if UNITY_EDITOR
// Assets/Scripts/Editor/ImportadorDatosIGN.cs
// ═══════════════════════════════════════════════════════════════════════════
//  IMPORTADOR DE DATOS OFICIALES IGN NAVARRA
//
//  Toma los JSON descargados por descargar_ign_navarra.py y los copia a la
//  ubicación que usan los generadores (AlsasuaData/*.json).
//
//  MENÚ: Altsasu GTA → Territorio Real → ★ Importar Datos IGN Navarra
// ═══════════════════════════════════════════════════════════════════════════

using System.IO;
using UnityEngine;
using UnityEditor;

public static class ImportadorDatosIGN
{
    // MENÚ-LEGACY: [MenuItem("Altsasu GTA/Territorio Real/★ Importar Datos IGN Navarra", false, 5)]
    public static void Importar()
    {
        string ignDir = "Assets/AlsasuaData/IGN";
        string dataDir = "Assets/AlsasuaData";

        if (!AssetDatabase.IsValidFolder(ignDir))
        {
            EditorUtility.DisplayDialog("Sin datos IGN",
                "No hay datos descargados.\n\n" +
                "Ejecuta primero el script Python:\n\n" +
                "  cd Tools\n" +
                "  pip install requests shapely pyproj\n" +
                "  python descargar_ign_navarra.py\n\n" +
                "Los datos se guardarán en Assets/AlsasuaData/IGN/",
                "Entendido");
            return;
        }

        var mapeos = new (string origen, string destino)[]
        {
            ("buildings_ign.json", "buildings_unity.json"),
            ("roads_ign.json",     "roads_unity.json"),
            ("railways_ign.json",  "railways_unity.json"),
            ("hydrography_ign.json","waterways_unity.json"),
        };

        int copiados = 0;
        foreach (var (origen, destino) in mapeos)
        {
            string src = $"{ignDir}/{origen}";
            string dst = $"{dataDir}/{destino}";

            if (File.Exists(src))
            {
                // Backup del archivo destino si existe
                if (File.Exists(dst))
                {
                    string backup = dst + ".bak";
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Copy(dst, backup);
                }
                File.Copy(src, dst, true);
                copiados++;
                Debug.Log($"[IGN] ✓ {origen} → {destino}");
            }
        }

        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("✅ Datos IGN importados",
            $"Copiados {copiados} archivos JSON oficiales.\n\n" +
            "Ahora regenera:\n" +
            "1. ★ Crear Terrain + Ortofoto\n" +
            "2. ★ Generar Edificios OSM Reales\n" +
            "3. ★ Generar Infraestructura Completa\n" +
            "4. ★ Mobiliario Urbano", "OK");
    }

    // MENÚ-LEGACY: [MenuItem("Altsasu GTA/Territorio Real/Mostrar info datos descargados", false, 6)]
    public static void MostrarInfo()
    {
        string ignDir = "Assets/AlsasuaData/IGN";
        if (!AssetDatabase.IsValidFolder(ignDir))
        {
            EditorUtility.DisplayDialog("Sin datos", "No hay datos IGN descargados.", "OK");
            return;
        }

        var info = new System.Text.StringBuilder();
        info.AppendLine("=== Datos IGN descargados ===\n");

        var archivos = Directory.GetFiles(ignDir, "*.json");
        foreach (var f in archivos)
        {
            var fi = new FileInfo(f);
            info.AppendLine($"• {Path.GetFileName(f)}");
            info.AppendLine($"  Tamaño: {fi.Length / 1024:N0} KB");
            info.AppendLine($"  Fecha:  {fi.LastWriteTime:yyyy-MM-dd HH:mm}");
            info.AppendLine();
        }

        EditorUtility.DisplayDialog("Info datos IGN", info.ToString(), "OK");
    }
}
#endif
