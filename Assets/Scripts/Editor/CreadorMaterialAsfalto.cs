// Assets/Scripts/Editor/CreadorMaterialAsfalto.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CREADOR DE MATERIAL DE ASFALTO — HDRP/URP
//
//  Genera automáticamente los materiales PBR de carretera usando las texturas
//  de Assets/Textures/Roads/ y los asigna al GeneradorCallesAltsasu.
//
//  MENÚ: Alsasua GTA → Crear Materiales de Asfalto
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using System.IO;

public static class CreadorMaterialAsfalto
{
    const string RUTA_TEXTURAS = "Assets/Textures/Roads";
    const string RUTA_MATERIALES = "Assets/Materials/Roads";

    // ── Nombres de texturas ───────────────────────────────────────────────
    // Set 1: Polyhaven asphalt_02 (oscuro, más realista para carreteras)
    const string ALB1  = "asphalt02_albedo_2k.jpg";
    const string NOR1  = "asphalt02_normal_2k.jpg";
    const string ROUGH1 = "asphalt02_roughness_2k.jpg";
    const string HGT1  = "asphalt02_height_2k.png";

    // Set 2: Pebbled asphalt (granulado, bueno para aceras/arcenes)
    const string ALB2  = "pebbled_asphalt_albedo.png";
    const string NOR2  = "pebbled_asphalt_Normal-ogl.png";
    const string HGT2  = "pebbled_asphalt_Height.png";
    const string AO2   = "pebbled_asphalt_ao.png";

    [MenuItem("Altsasu GTA/Crear Materiales de Asfalto", false, 10)]
    public static void CrearMateriales()
    {
        // Crear carpeta de materiales si no existe
        if (!AssetDatabase.IsValidFolder(RUTA_MATERIALES))
        {
            AssetDatabase.CreateFolder("Assets/Materials", "Roads");
            AssetDatabase.Refresh();
        }

        // Detectar pipeline de render
        bool esHDRP = IsHDRP();
        bool esURP  = !esHDRP && IsURP();
        string shaderName = esHDRP ? "HDRP/Lit"
                          : esURP  ? "Universal Render Pipeline/Lit"
                          : "Standard";

        Debug.Log($"[AsfaltoSetup] Usando shader: {shaderName}");

        // ── Material 1: Asfalto principal ─────────────────────────────────
        var matAsfalto = CrearMaterial("M_Asfalto_Carretera", shaderName,
            ALB1, NOR1, ROUGH1, HGT1, null, esHDRP, esURP);

        // ── Material 2: Arcén / acera (pebbled) ───────────────────────────
        var matArcen = CrearMaterial("M_Arcen_Hormigon", shaderName,
            ALB2, NOR2, null, HGT2, AO2, esHDRP, esURP);

        // ── Material 3: Líneas de carretera (blanco emissive) ─────────────
        var matLineas = new Material(Shader.Find(shaderName));
        matLineas.name = "M_Lineas_Carretera";
        matLineas.color = new Color(0.95f, 0.95f, 0.80f);
        if (esHDRP)
        {
            // Líneas ligeramente emissive para visibilidad nocturna
            matLineas.SetColor("_EmissiveColor", new Color(0.15f, 0.15f, 0.10f));
            matLineas.EnableKeyword("_EMISSION");
        }
        GuardarMaterial(matLineas);

        // ── Asignar al GeneradorCallesAltsasu si está en escena ──────────────
        var generador = Object.FindFirstObjectByType<GeneradorCallesAltsasu>();
        if (generador != null)
        {
            generador.materialAsfalto = matAsfalto;
            generador.materialArcen   = matArcen;
            generador.materialLineas  = matLineas;
            EditorUtility.SetDirty(generador);
            Debug.Log("[AsfaltoSetup] Materiales asignados al GeneradorCallesAltsasu.");
        }
        else
        {
            Debug.LogWarning("[AsfaltoSetup] GeneradorCallesAltsasu no encontrado en la escena. " +
                             "Arrástralo desde Assets/Scripts/ a un GameObject y ejecuta de nuevo.");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Materiales de Asfalto",
            $"✅ Materiales creados en {RUTA_MATERIALES}:\n\n" +
            "• M_Asfalto_Carretera  (albedo + normal + roughness)\n" +
            "• M_Arcen_Hormigon     (pebbled asphalt + AO)\n" +
            "• M_Lineas_Carretera   (blanco emissive)\n\n" +
            (generador != null ? "✅ Asignados al GeneradorCallesAltsasu." :
             "⚠ Añade GeneradorCallesAltsasu a la escena y ejecuta de nuevo."),
            "OK");
    }

    // =========================================================================
    //  HELPERS
    // =========================================================================

    static Material CrearMaterial(string nombre, string shader,
        string albedo, string normal, string roughness, string height, string ao,
        bool hdrp, bool urp)
    {
        var mat = new Material(Shader.Find(shader)) { name = nombre };
        mat.color = Color.white;

        var texAlb   = Cargar(albedo);
        var texNor   = Cargar(normal);
        var texRough = roughness != null ? Cargar(roughness) : null;
        var texHgt   = height   != null ? Cargar(height)    : null;
        var texAO    = ao       != null ? Cargar(ao)         : null;

        if (hdrp)
        {
            // HDRP Lit property names
            if (texAlb   != null) mat.SetTexture("_BaseColorMap",   texAlb);
            if (texNor   != null)
            {
                mat.SetTexture("_NormalMap", texNor);
                mat.SetFloat("_NormalScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (texHgt != null)
            {
                mat.SetTexture("_HeightMap", texHgt);
                mat.SetFloat("_HeightAmplitude", 0.02f);
                mat.SetFloat("_HeightCenter", 0.5f);
            }
            // Roughness en HDRP va en el canal A del MaskMap (MADS)
            // Si solo tenemos roughness, usarlo como Smoothness invertido
            if (texRough != null)
            {
                // Smoothness = 1 - roughness (HDRP usa smoothness, no roughness)
                mat.SetFloat("_Smoothness", 0.15f); // asfalto = poco brillante
            }
            if (texAO != null) mat.SetTexture("_MaskMap", texAO);

            // Configuración típica de asfalto
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_CoatMask", 0f);
        }
        else if (urp)
        {
            // URP Lit property names
            if (texAlb   != null) mat.SetTexture("_BaseMap",    texAlb);
            if (texNor   != null)
            {
                mat.SetTexture("_BumpMap", texNor);
                mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (texRough != null) mat.SetTexture("_MetallicGlossMap", texRough);
            mat.SetFloat("_Smoothness", 0.1f);
            mat.SetFloat("_Metallic", 0f);
        }
        else
        {
            // Built-in Standard
            if (texAlb  != null) mat.SetTexture("_MainTex",   texAlb);
            if (texNor  != null)
            {
                mat.SetTexture("_BumpMap", texNor);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (texRough != null) mat.SetTexture("_MetallicGlossMap", texRough);
            mat.SetFloat("_Glossiness", 0.1f);
            mat.SetFloat("_Metallic", 0f);
        }

        // UV Tiling: 4 repeticiones por unidad de UV (textura tilea cada ~1.5m)
        mat.mainTextureScale = new Vector2(4f, 4f);

        GuardarMaterial(mat);
        return mat;
    }

    static void GuardarMaterial(Material mat)
    {
        string ruta = $"{RUTA_MATERIALES}/{mat.name}.mat";
        // Eliminar si ya existe
        if (File.Exists(ruta)) AssetDatabase.DeleteAsset(ruta);
        AssetDatabase.CreateAsset(mat, ruta);
        Debug.Log($"[AsfaltoSetup] ✓ {mat.name} → {ruta}");
    }

    static Texture2D Cargar(string nombreArchivo)
    {
        string ruta = $"{RUTA_TEXTURAS}/{nombreArchivo}";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ruta);
        if (tex == null)
            Debug.LogWarning($"[AsfaltoSetup] Textura no encontrada: {ruta}");
        return tex;
    }

    static bool IsHDRP()
    {
        var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        return pipeline != null && pipeline.GetType().Name.Contains("HDRenderPipelineAsset");
    }

    static bool IsURP()
    {
        var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        return pipeline != null && pipeline.GetType().Name.Contains("UniversalRenderPipelineAsset");
    }
}
