// Assets/Scripts/Editor/ImportadorPBRVivo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Configura automáticamente las texturas CC0 de Resources/PBR_Vivo al
//  importarlas: los mapas "_nor_" como NormalMap y los "_arm_"/"_rough_" en
//  espacio lineal (sRGB off). Así el material PBR se ve correcto sin tocar
//  nada a mano.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEditor;

public class ImportadorPBRVivo : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').Contains("/Resources/PBR_Vivo/")) return;

        var imp = (TextureImporter)assetImporter;
        string n = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();

        if (n.Contains("_nor"))
        {
            imp.textureType = TextureImporterType.NormalMap;
        }
        else if (n.Contains("_arm") || n.Contains("_rough") || n.Contains("_mask") || n.Contains("_metal"))
        {
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = false;   // datos lineales, no color
            imp.isReadable = true;     // legible: reempaquetamos ARM→MaskMap en runtime
            imp.textureCompression = TextureImporterCompression.Uncompressed; // mask sin artefactos BC
        }
        else
        {
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;    // albedo/diffuse = color
        }

        imp.maxTextureSize = 1024;
        imp.wrapMode = UnityEngine.TextureWrapMode.Repeat;
    }
}
