// Assets/Scripts/TexturasVivo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  TEXTURAS VIVO — construye materiales PBR a partir de las texturas CC0
//  (Poly Haven) en Resources/PBR_Vivo. Cacheados y compartidos.
//
//  Si hay mapa ARM (aerial_asphalt_01, concrete_wall_008) se reempaqueta a
//  MaskMap de HDRP (o a Metallic/Occlusion de Standard) para tener
//  oclusión + metalness + smoothness por píxel. Si falta el ARM o no es
//  legible, se cae a valores planos (el material sigue funcionando).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public static class TexturasVivo
{
    static Material _asfalto, _hormigon, _adoquin, _chapa, _ladrillo;

    /// <summary>Asfalto real (aerial_asphalt_01) con ARM. null si no está descargado.</summary>
    public static Material Asfalto =>
        _asfalto != null ? _asfalto :
        (_asfalto = Construir("PBR_Vivo/aerial_asphalt_01_diff_1k",
                              "PBR_Vivo/aerial_asphalt_01_nor_gl_1k",
                              new Vector2(8, 24), 0.25f, 0f, Color.white,
                              "PBR_Vivo/aerial_asphalt_01_arm_1k"));

    /// <summary>Hormigón real (concrete_wall_008) con ARM. null si no está descargado.</summary>
    public static Material Hormigon =>
        _hormigon != null ? _hormigon :
        (_hormigon = Construir("PBR_Vivo/concrete_wall_008_diff_1k",
                               "PBR_Vivo/concrete_wall_008_nor_gl_1k",
                               new Vector2(4, 8), 0.2f, 0f, Color.white,
                               "PBR_Vivo/concrete_wall_008_arm_1k"));

    /// <summary>Adoquín real (cobblestone_03) — plaza y calles. Sin ARM (valores planos).</summary>
    public static Material Adoquin =>
        _adoquin != null ? _adoquin :
        (_adoquin = Construir("PBR_Vivo/cobblestone_03_diff_1k",
                              "PBR_Vivo/cobblestone_03_nor_gl_1k",
                              new Vector2(6, 6), 0.15f, 0f, Color.white));

    /// <summary>Chapa industrial ondulada (corrugated_iron) — fábricas. Sin ARM.</summary>
    public static Material ChapaIndustrial =>
        _chapa != null ? _chapa :
        (_chapa = Construir("PBR_Vivo/corrugated_iron_diff_1k",
                            "PBR_Vivo/corrugated_iron_nor_gl_1k",
                            new Vector2(4, 3), 0.4f, 0.6f, Color.white));

    /// <summary>Ladrillo real (brick_4) — chimeneas y fachadas. Sin ARM.</summary>
    public static Material Ladrillo =>
        _ladrillo != null ? _ladrillo :
        (_ladrillo = Construir("PBR_Vivo/brick_4_diff_1k",
                               "PBR_Vivo/brick_4_nor_gl_1k",
                               new Vector2(2, 4), 0.2f, 0f, Color.white));

    /// <summary>Placa metálica pintada (blue_metal_plate) con tinte — carrocerías, tren. Sin ARM.</summary>
    public static Material MetalTintado(Color tinte) =>
        Construir("PBR_Vivo/blue_metal_plate_diff_1k",
                  "PBR_Vivo/blue_metal_plate_nor_gl_1k",
                  new Vector2(3, 6), 0.55f, 0.7f, tinte);

    static Material Construir(string diffuse, string normal, Vector2 tiling,
                              float smoothness, float metallic, Color tinte,
                              string arm = null)
    {
        var diff = Resources.Load<Texture2D>(diffuse);
        if (diff == null) return null;   // no descargado → respaldo de color plano
        var nor = Resources.Load<Texture2D>(normal);

        var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh) { name = "PBRVivo_" + diff.name };

        // Color base
        if (m.HasProperty("_BaseColorMap")) m.SetTexture("_BaseColorMap", diff);
        else if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", diff);

        // Tinte multiplicativo sobre el mapa de color (mantiene detalle, cambia el tono)
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tinte);
        else if (m.HasProperty("_Color")) m.SetColor("_Color", tinte);

        // Normal
        if (nor != null)
        {
            if (m.HasProperty("_NormalMap")) m.SetTexture("_NormalMap", nor);
            else if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", nor);
            m.EnableKeyword("_NORMALMAP");
        }

        // ARM → mask por píxel (oclusión + metal + smoothness reales).
        bool armAplicado = !string.IsNullOrEmpty(arm) &&
                           AplicarArm(m, Resources.Load<Texture2D>(arm));

        // Respaldo: si no hay ARM (o falló), valores planos como antes.
        if (!armAplicado)
        {
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", metallic);
        }

        // Tiling coherente en todos los mapas (antes el normal usaba 1:1 → mal escalado)
        if (m.HasProperty("_BaseColorMap")) m.SetTextureScale("_BaseColorMap", tiling);
        if (m.HasProperty("_MainTex"))      m.SetTextureScale("_MainTex", tiling);
        if (m.HasProperty("_NormalMap"))    m.SetTextureScale("_NormalMap", tiling);
        if (m.HasProperty("_BumpMap"))      m.SetTextureScale("_BumpMap", tiling);
        if (m.HasProperty("_MaskMap"))      m.SetTextureScale("_MaskMap", tiling);
        if (m.HasProperty("_MetallicGlossMap")) m.SetTextureScale("_MetallicGlossMap", tiling);
        if (m.HasProperty("_OcclusionMap")) m.SetTextureScale("_OcclusionMap", tiling);

        return m;
    }

    // ── Reempaqueta el ARM de Poly Haven (R=AO, G=Roughness, B=Metalness) al
    //    layout que espera el shader destino. Devuelve false si no se pudo
    //    (textura ausente o no legible) para que se usen los valores planos.
    static bool AplicarArm(Material m, Texture2D arm)
    {
        if (arm == null) return false;

        Color[] src;
        try { src = arm.GetPixels(); }      // requiere isReadable=true (lo pone el importador)
        catch { return false; }             // no legible → respaldo
        if (src == null || src.Length == 0) return false;

        int w = arm.width, h = arm.height;

        // ── HDRP/Lit: MaskMap = R:Metallic  G:Occlusion  B:Detail  A:Smoothness
        if (m.HasProperty("_MaskMap"))
        {
            var dst = new Color[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                float ao = src[i].r, rough = src[i].g, metal = src[i].b;
                dst[i] = new Color(metal, ao, 0f, 1f - rough);
            }
            var mask = new Texture2D(w, h, TextureFormat.RGBA32, true, true) // linear
            { name = "MaskVivo_" + arm.name, wrapMode = TextureWrapMode.Repeat };
            mask.SetPixels(dst);
            mask.Apply(true, true);

            m.SetTexture("_MaskMap", mask);
            m.EnableKeyword("_MASKMAP");
            SetIf(m, "_Metallic", 1f);            // multiplicador passthrough sobre mask.R
            SetIf(m, "_SmoothnessRemapMin", 0f);  SetIf(m, "_SmoothnessRemapMax", 1f);
            SetIf(m, "_AORemapMin", 0f);          SetIf(m, "_AORemapMax", 1f);
            return true;
        }

        // ── Standard: MetallicGloss = R:Metallic A:Smoothness ; Occlusion lee G
        if (m.HasProperty("_MetallicGlossMap"))
        {
            var dmg = new Color[src.Length];
            var docc = new Color[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                float ao = src[i].r, rough = src[i].g, metal = src[i].b;
                dmg[i]  = new Color(metal, metal, metal, 1f - rough);
                docc[i] = new Color(ao, ao, ao, 1f);
            }
            var mg = new Texture2D(w, h, TextureFormat.RGBA32, true, true)
            { name = "MetalGlossVivo_" + arm.name, wrapMode = TextureWrapMode.Repeat };
            mg.SetPixels(dmg); mg.Apply(true, true);

            var occ = new Texture2D(w, h, TextureFormat.RGBA32, true, true)
            { name = "OcclVivo_" + arm.name, wrapMode = TextureWrapMode.Repeat };
            occ.SetPixels(docc); occ.Apply(true, true);

            m.SetTexture("_MetallicGlossMap", mg);
            m.EnableKeyword("_METALLICGLOSSMAP");
            SetIf(m, "_GlossMapScale", 1f);
            SetIf(m, "_Metallic", 1f);
            if (m.HasProperty("_OcclusionMap"))
            {
                m.SetTexture("_OcclusionMap", occ);
                m.EnableKeyword("_OCCLUSIONMAP");
                SetIf(m, "_OcclusionStrength", 1f);
            }
            return true;
        }

        return false;
    }

    static void SetIf(Material m, string prop, float v)
    {
        if (m.HasProperty(prop)) m.SetFloat(prop, v);
    }
}
