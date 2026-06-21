// Assets/Scripts/Editor/PipelineUltraprecisoEditor.cs
// Tools → Alsasua → 🌐 Pipeline Ultra-Preciso (10 fuentes)
// Tools → Alsasua → Debug/📊 Estado Datos Ultra-Precisos

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public static class PipelineUltraprecisoEditor
{
    [MenuItem("Tools/Alsasua/Terreno/🌐 Pipeline Ultra-Preciso (10 fuentes)", priority = 50)]
    public static void EjecutarPipeline()
    {
        bool ok = EditorUtility.DisplayDialog(
            "🌐 Pipeline Ultra-Preciso",
            "Descargará datos de 10 fuentes independientes:\n\n" +
            "① LIDAR PNOA 3ª Cob. — 5+ pts/m² (tejados exactos, árboles)\n" +
            "② Catastro INSPIRE — footprints cm + año construcción\n" +
            "③ IDENA WFS — edificaciones, farolas, arbolado Navarra\n" +
            "④ IDENA WCS — DTM 2m (mejor que IGN 5m)\n" +
            "⑤ CartoCiudad — portales exactos en borde de parcela\n" +
            "⑥ Overture Maps — alturas ML Microsoft/Meta/TomTom\n" +
            "⑦ Microsoft AI Footprints — 1.4B edificios IA (ODbL)\n" +
            "⑧ OSM Overpass — todos los tags de edificios\n" +
            "⑨ Mapillary — señales/bancos/farolas desde fotos calle\n" +
            "⑩ PNOA-MA 2024 — ortofoto máxima actualidad (25cm)\n\n" +
            "Requiere Python 3.8+ y conexión a internet.\n" +
            "Tiempo estimado: 5-30 min (depende del LIDAR).\n\n" +
            "Paquetes Python necesarios:\n" +
            "pip install requests laspy lazrs-python numpy scipy overturemaps\n\n" +
            "¿Ejecutar?",
            "🚀 Ejecutar", "Cancelar");

        if (!ok) return;

        string script = Path.Combine(
            Application.dataPath.Replace("Assets", ""),
            "PipelineDatosUltraprecisos.py");

        if (!File.Exists(script))
        {
            EditorUtility.DisplayDialog("Error",
                "PipelineDatosUltraprecisos.py no encontrado.\n" +
                "Debe estar en la raíz del proyecto.", "OK");
            return;
        }

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName         = "python",
            Arguments        = $"\"{script}\"",
            UseShellExecute  = true,
            WorkingDirectory = Application.dataPath.Replace("Assets", ""),
        };

        System.Diagnostics.Process.Start(psi);

        EditorUtility.DisplayDialog("🚀 Pipeline iniciado",
            "El pipeline corre en una ventana de Python.\n\n" +
            "Cuando termine verás:\n" +
            "  ✅ edificios_ultra.json\n" +
            "  ✅ lidar_buildings.json\n" +
            "  ✅ terreno_2m.asc\n" +
            "  ✅ portales_cartociudad.json\n" +
            "  ...\n\n" +
            "Luego en Unity:\n" +
            "1. Tools → Alsasua → Escena → 🎬 Crear Escena Principal\n" +
            "   (recrea la escena con todos los sistemas de precisión)\n" +
            "2. Dale Play", "OK");
    }

    [MenuItem("Tools/Alsasua/Terreno/🌐 Pipeline — Solo LIDAR", priority = 51)]
    public static void EjecutarSoloLIDAR()
    {
        LanzarPipeline("lidar",
            "Descarga los tiles LAZ del PNOA 3ª Cobertura (5+ pts/m²)\n" +
            "y los procesa para extraer:\n" +
            "• Puntos de suelo → terreno 0.5m\n" +
            "• Puntos de edificio → forma exacta de cada tejado\n" +
            "• Puntos de vegetación → posición y altura de cada árbol");
    }

    [MenuItem("Tools/Alsasua/Terreno/🌐 Pipeline — Solo Fusión", priority = 52)]
    public static void EjecutarFusion()
    {
        LanzarPipeline("fusion",
            "Fusiona todos los datos descargados en:\n" +
            "• edificios_ultra.json (LIDAR + Overture + Catastro + OSM)");
    }

    static void LanzarPipeline(string solo, string desc)
    {
        string script = Path.Combine(
            Application.dataPath.Replace("Assets", ""), "PipelineDatosUltraprecisos.py");

        if (!File.Exists(script))
        {
            EditorUtility.DisplayDialog("Error", "PipelineDatosUltraprecisos.py no encontrado.", "OK");
            return;
        }

        bool ok = EditorUtility.DisplayDialog($"Pipeline {solo}", desc + "\n\n¿Ejecutar?",
            "▶ Ejecutar", "Cancelar");
        if (!ok) return;

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName         = "python",
            Arguments        = $"\"{script}\" --solo {solo}",
            UseShellExecute  = true,
            WorkingDirectory = Application.dataPath.Replace("Assets", ""),
        };
        System.Diagnostics.Process.Start(psi);
    }

    // ── Estado ────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/Debug/📊 Estado Datos Ultra-Precisos", priority = 21)]
    public static void EstadoDatosUltra()
    {
        var root = Application.dataPath.Replace("Assets", "Assets/AlsasuaData");

        var archivos = new Dictionary<string, string>
        {
            ["edificios_ultra.json"]         = "FUSIÓN edificios (LIDAR+Overture+Cat+OSM)",
            ["lidar_buildings.json"]         = "LIDAR: tejados por edificio (clase 6)",
            ["lidar_ground.xyz"]             = "LIDAR: puntos suelo (clase 2)",
            ["lidar_trees.json"]             = "LIDAR: árboles detectados (clase 5)",
            ["terreno_2m.asc"]               = "DTM 2m IDENA (mejor que IGN 5m)",
            ["catastro_edificios.json"]      = "Catastro INSPIRE (año construc.)",
            ["idena_edificaciones.json"]     = "IDENA: edificaciones Navarra",
            ["idena_alumbrado.json"]         = "IDENA: farolas inventario",
            ["idena_arbolado.json"]          = "IDENA: arbolado urbano",
            ["portales_cartociudad.json"]    = "CartoCiudad: portales exactos",
            ["overture_buildings.geojson"]   = "Overture: footprints ML",
            ["overture_alturas.json"]        = "Overture: alturas ML edificios",
            ["microsoft_footprints.json"]    = "Microsoft AI: footprints 1.4B",
            ["mapillary_imagenes.json"]      = "Mapillary: fotos de calle",
            ["mapillary_objetos.json"]       = "Mapillary: objetos detectados ML",
        };

        string msg = "═══ DATOS ULTRA-PRECISOS ═══\n\n";
        int presentes = 0;
        foreach (var kv in archivos)
        {
            string path = Path.Combine(root, kv.Key);
            bool existe = File.Exists(path);
            if (existe) presentes++;
            string size = existe ? $"{new FileInfo(path).Length/1024}KB" : "—";
            msg += $"{(existe ? "✅" : "❌")} {kv.Key,-38} {size,8}  {kv.Value}\n";
        }

        // Tiles LIDAR
        string lidarDir = Path.Combine(root, "lidar_tiles");
        int nLAZ = Directory.Exists(lidarDir) ? Directory.GetFiles(lidarDir, "*.laz").Length : 0;
        msg += $"\n✅ Tiles LIDAR LAZ: {nLAZ}/6 (1km×1km, 5+ pts/m²)\n";

        msg += $"\n{presentes}/{archivos.Count} archivos de precisión presentes\n";
        msg += "\nPara descargar todo:\nTools → Alsasua → 🌐 Pipeline Ultra-Preciso";

        Debug.Log(msg);
        EditorUtility.DisplayDialog("📊 Datos Ultra-Precisos", msg, "OK");
    }
}
#endif
