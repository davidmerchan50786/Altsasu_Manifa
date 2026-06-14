// Assets/Scripts/Editor/ConfiguradorSVT.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONFIGURADOR — Streaming Virtual Texturing (SVT) para ortofoto/terreno
//
//  REALIDAD DE SVT EN HDRP 17.3 (importante, no es "marcar una casilla"):
//   · SVT se ACTIVA a nivel de proyecto (PlayerSettings) y exige REINICIAR el
//     editor. Esto lo hace este script.
//   · El MUESTREO de una textura virtual ocurre SOLO en Shader Graph (propiedad
//     "Virtual Texture" + nodo "Sample Virtual Texture"). El shader HDRP/Terrain
//     integrado NO soporta SVT → la ortofoto drastrada sobre malla (mesh) es el
//     caso real de SVT aquí; el Terrain splatmapeado sigue por mipmap streaming
//     clásico (ver GestorStreamingTexturas).
//   · El feedback (qué tiles cargar) lo gestiona HDRP automáticamente cuando el
//     material usa nodos VT; no hay que escribir el pase de feedback a mano. Lo
//     único afinable es el tamaño de caché GPU (HDRP Asset, internal → Inspector)
//     y la caché CPU (runtime, ver GestorStreamingTexturas).
//   · Las texturas de un "VT stack" NO deben usar Mipmap Streaming (lo sustituye
//     SVT) pero SÍ deben tener mips. De eso se encarga el AssetPostprocessor.
//
//  Capa: Alsasua.Editor (todo esto es editor-only).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEditor;
using UnityEngine;

public static class ConfiguradorSVT
{
    [MenuItem("Tools/Alsasua/Render/SVT · Activar Streaming Virtual Texturing")]
    public static void ActivarSVT()
    {
        if (PlayerSettings.GetVirtualTexturingSupportEnabled())
        {
            Debug.Log("[SVT] Ya estaba activado en Player Settings.");
            return;
        }

        // Activa el soporte de VT. Requiere reinicio del editor para surtir efecto.
        PlayerSettings.SetVirtualTexturingSupportEnabled(true);
        Debug.LogWarning("[SVT] Soporte de Virtual Texturing ACTIVADO en Player Settings. " +
                         "REINICIA el editor para que tenga efecto. " +
                         "Después: crea un material Shader Graph con una propiedad 'Virtual Texture' " +
                         "(apila Albedo+Normal+Mask de la ortofoto) y un nodo 'Sample Virtual Texture'.");
    }

    [MenuItem("Tools/Alsasua/Render/SVT · Reimportar texturas de ortofoto (preparar para VT)")]
    public static void ReimportarOrtofoto()
    {
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/AlsasuaData" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!ImportadorTexturasVT.EsTexturaVT(path)) continue;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            n++;
        }
        Debug.Log($"[SVT] Reimportadas {n} texturas de ortofoto con ajustes VT.");
    }
}

// ── AssetPostprocessor: deja las texturas de la ortofoto listas para un VT stack ──
//   · mips ON (VT trabaja por niveles de mip)
//   · MIPMAP STREAMING OFF (SVT lo reemplaza; tenerlo activo duplica gestión)
//   · sin "Read/Write" (ahorra RAM duplicada)
//   · clamp + HQ compression + tamaño grande (la ortofoto es 0.25 m/px)
public sealed class ImportadorTexturasVT : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!EsTexturaVT(assetPath)) return;

        // CRÍTICO (fix OOM en bucle): los ajustes de VT —maxTextureSize 8192 +
        // mipmap streaming OFF (todos los mips residentes)— SOLO son seguros y
        // útiles cuando SVT está REALMENTE activado. Con SVT desactivado (estado
        // por defecto), forzar eso sobre la ortofoto completa (30 MB → varios GB al
        // descomprimir) revienta el importador en CADA refresh → crash loop del editor.
        // Sin SVT: no-op (la textura se importa con sus ajustes normales del .meta).
        if (!PlayerSettings.GetVirtualTexturingSupportEnabled()) return;

        var ti = (TextureImporter)assetImporter;
        ti.mipmapEnabled      = true;
        ti.streamingMipmaps   = false;                                  // ← SVT gestiona el streaming
        ti.isReadable         = false;
        ti.wrapMode           = TextureWrapMode.Clamp;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.maxTextureSize     = 4096;                                   // 8192 de la ortofoto completa es excesivo
        // sRGB para color (albedo/ortofoto). Para normales/mask habría que poner
        // sRGBTexture=false y el tipo adecuado; aquí tratamos color.
        ti.sRGBTexture        = true;
    }

    /// <summary>Texturas candidatas a entrar en un VT stack (ortofoto del mundo).</summary>
    public static bool EsTexturaVT(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string p = path.ToLowerInvariant();
        return p.Contains("ortofoto") || p.Contains("orto_tiles") || p.Contains("/orto_");
    }
}
