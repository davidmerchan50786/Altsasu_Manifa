// Assets/Scripts/Editor/PrepararImpostoresArboles.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PREPARAR IMPOSTORES DE ÁRBOLES — copia texturas billboard REALES a Resources
//
//  ImpostoresArbolesDistantes dibuja el monte lejano como billboards. Antes usaba
//  una silueta PROCEDURAL (un blob verde) → feo. Esto copia texturas billboard de
//  árbol REALES (ALP Poplar + VegetationStudioPro) a Assets/Resources/Impostores/
//  con nombres por especie, para que el sistema runtime las cargue (Resources.Load)
//  y cada especie use su silueta real.
//
//  Mapa especie → textura (editable: cambia las rutas o sustituye los PNG copiados):
//    · ribera (chopo/sauce) → ALP PoplarTree001_Billboard (chopo real)
//    · roble/pino/genérico  → billboards VSPro (árboles reales; reasignables)
//
//  Menú: Tools/Alsasua/Render. Ejecutar UNA vez (o tras cambiar las fuentes).
// ═══════════════════════════════════════════════════════════════════════════

using System.IO;
using UnityEditor;
using UnityEngine;

public static class PrepararImpostoresArboles
{
    const string DST = "Assets/Resources/Impostores";

    // (especie, ruta fuente, nombre destino). Cambia las fuentes si quieres otras siluetas.
    static readonly (string nombre, string src)[] MAPA =
    {
        ("imp_ribera",   "Assets/ALP_Assets/Poplar Tree FREE/Models/Textures/PoplarTree001_Billboard.tga"),
        ("imp_roble",    "Packages/com.denispahunov.mapmagic/Demo/Compatibility/VegetationStudioPro/VSProPackage_billboards/billboard_85ec345c-301e-4eaa-8fd5-adced0b8a33b.png"),
        ("imp_pino",     "Packages/com.denispahunov.mapmagic/Demo/Compatibility/VegetationStudioPro/VSProPackage_billboards/billboard_886a084f-6603-47f2-97f4-95bd52d9495f.png"),
        ("imp_generico", "Packages/com.denispahunov.mapmagic/Demo/Compatibility/VegetationStudioPro/VSProPackage_billboards/billboard_16a94683-e848-4560-9ba6-f383cac42f7b.png"),
    };

    [MenuItem("Tools/Alsasua/Render/🌳 Preparar Impostores de Árboles (texturas reales)")]
    static void Preparar()
    {
        Directory.CreateDirectory(DST);
        AssetDatabase.Refresh();

        int ok = 0; var faltan = new System.Text.StringBuilder();
        foreach (var (nombre, src) in MAPA)
        {
            if (!File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", src))))
            { faltan.Append($"\n· {src}"); continue; }

            string ext = Path.GetExtension(src);
            string dst = $"{DST}/{nombre}{ext}";
            AssetDatabase.DeleteAsset(dst);   // re-copiar limpio
            if (AssetDatabase.CopyAsset(src, dst))
            {
                // Asegurar alpha + sin compresión agresiva en el import del impostor.
                var imp = AssetImporter.GetAtPath(dst) as TextureImporter;
                if (imp != null)
                {
                    imp.alphaIsTransparency   = true;
                    imp.textureCompression    = TextureImporterCompression.Uncompressed;
                    imp.mipmapEnabled         = true;
                    imp.SaveAndReimport();
                }
                ok++;
            }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string msg = $"✅ {ok}/{MAPA.Length} texturas billboard copiadas a {DST}/ (cargadas por ImpostoresArbolesDistantes)." +
                     (faltan.Length > 0 ? $"\n⚠ No encontradas:{faltan}" : "");
        Debug.Log("[ImpostoresArboles] " + msg);
        EditorUtility.DisplayDialog("Preparar Impostores de Árboles", msg + "\n\nDale a Play: el monte lejano usará árboles reales por especie.", "Genial");
    }
}
