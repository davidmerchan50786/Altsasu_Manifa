using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System.Collections.Generic;

/// <summary>
/// Upgrades the player character model to use AAA PBR materials (HDRP/Lit).
/// Runs before ControladorJugador so it can assign prefabPersonaje if none is set.
/// Converts Ch20 non-PBR textures and MeshyAI materials to proper HDRP/Lit workflow.
/// </summary>
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(ControladorJugador))]
public class ConfiguradorPersonajeAAA : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════
    //  INSPECTOR
    // ═══════════════════════════════════════════════════════════════════════

    [Header("Configuración PBR")]
    [SerializeField, Range(0f, 1f)] private float metallicCloth    = 0.0f;
    [SerializeField, Range(0f, 1f)] private float smoothnessCloth  = 0.3f;
    [SerializeField, Range(0f, 2f)] private float normalScale      = 1.0f;

    [Header("Auto-detección de personaje")]
    [Tooltip("Si true, busca FBX de personaje automáticamente cuando prefabPersonaje es null")]
    [SerializeField] private bool autoDetectarPersonaje = true;

    [Header("Combate cuerpo a cuerpo")]
    [Tooltip("Si true, añade clips de FightingMotions al Animator si están disponibles")]
    [SerializeField] private bool integrarFightingMotions = true;

    // ═══════════════════════════════════════════════════════════════════════
    //  CACHED SHADER PROPERTY IDS — sin allocs en runtime
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly int _BaseColorMap       = Shader.PropertyToID("_BaseColorMap");
    private static readonly int _BaseColor          = Shader.PropertyToID("_BaseColor");
    private static readonly int _NormalMap           = Shader.PropertyToID("_NormalMap");
    private static readonly int _NormalScale         = Shader.PropertyToID("_NormalScale");
    private static readonly int _MaskMap             = Shader.PropertyToID("_MaskMap");
    private static readonly int _Metallic            = Shader.PropertyToID("_Metallic");
    private static readonly int _Smoothness          = Shader.PropertyToID("_Smoothness");
    private static readonly int _EmissiveColorMap    = Shader.PropertyToID("_EmissiveColorMap");
    private static readonly int _EmissiveColor       = Shader.PropertyToID("_EmissiveColor");
    private static readonly int _EmissiveIntensity   = Shader.PropertyToID("_EmissiveIntensity");

    // Legacy/Standard shader property IDs for reading source textures
    private static readonly int _MainTex             = Shader.PropertyToID("_MainTex");
    private static readonly int _BumpMap             = Shader.PropertyToID("_BumpMap");
    private static readonly int _SpecGlossMap        = Shader.PropertyToID("_SpecGlossMap");
    private static readonly int _MetallicGlossMap    = Shader.PropertyToID("_MetallicGlossMap");
    private static readonly int _EmissionMap         = Shader.PropertyToID("_EmissionMap");
    private static readonly int _GlossMapScale       = Shader.PropertyToID("_GlossMapScale");
    private static readonly int _BumpScale           = Shader.PropertyToID("_BumpScale");
    private static readonly int _OcclusionMap        = Shader.PropertyToID("_OcclusionMap");

    // MeshyAI PBR naming convention
    private static readonly int _RoughnessMap        = Shader.PropertyToID("_RoughnessMap");

    // Tracking created materials for cleanup
    private readonly List<Material> _createdMaterials = new List<Material>();

    // Known character FBX paths (relative to Assets/, without extension for Resources.Load)
    // PRIORIDAD: Survivalist PlayerArmature rigged HDRP > Meshy AI > legacy Ch20
    private static readonly string[] KnownCharacterPaths = new string[]
    {
        "Prefabs/Personajes/PlayerArmature",   // Survivalist HDRP rigged (Resources/)
        "#Xtra/Ch20_nonPBR",
        "_ExtractedAssets/Personajes/MeshyAI/Civil_Casual_01/Civil_Casual_01",
        "_ExtractedAssets/Personajes/MeshyAI/Civil_UrbanEdge/Civil_UrbanEdge",
        "_ExtractedAssets/Personajes/MeshyAI/Civil_CodeKicks/Civil_CodeKicks",
        "_ExtractedAssets/Personajes/MeshyAI/Civil_DenimOveralls/Civil_DenimOveralls",
        "_ExtractedAssets/Personajes/MeshyAI/Civil_SoftGaze_Biped/Civil_SoftGaze_Biped",
        "_ExtractedAssets/Personajes/MeshyAI/Civil_Executive/Civil_Executive",
        "_ExtractedAssets/Personajes/MeshyAI/Civil_SummerStreet_01/Civil_SummerStreet_01",
        "_ExtractedAssets/Personajes/MeshyAI/Civil_Pruu/Civil_Pruu",
    };

    // FightingMotions clip paths (inside FBX files — loaded via Resources or AssetDatabase)
    private static readonly string FightingMotionsFBXPath = "FightingMotionsVolume1/FBX";

    // ═══════════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ═══════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        var controlador = GetComponent<ControladorJugador>();

        // Check if prefabPersonaje is assigned via reflection (it's private)
        var field = typeof(ControladorJugador).GetField("prefabPersonaje",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (field == null)
        {
            AlsasuaLogger.Warn("ConfiguradorAAA",
                "No se pudo acceder a ControladorJugador.prefabPersonaje por reflexión.");
            return;
        }

        var prefab = field.GetValue(controlador) as GameObject;

        if (prefab == null && autoDetectarPersonaje)
        {
            prefab = AutoDetectarPersonajeFBX();
            if (prefab != null)
            {
                field.SetValue(controlador, prefab);
                AlsasuaLogger.Info("ConfiguradorAAA",
                    $"Auto-detectado personaje: {prefab.name}");
            }
        }

        // If we have a prefab (assigned or auto-detected), upgrade its materials
        if (prefab != null)
        {
            UpgradeMaterialsOnPrefab(prefab);
        }
    }

    /// <summary>
    /// Called after ControladorJugador.Start() instantiates the character.
    /// We find the instantiated model and ensure HDRP materials + shadows are correct.
    /// </summary>
    private void Start()
    {
        // ControladorJugador.Start() runs after our Awake (we're -50, it's default 0).
        // But Start() order is NOT guaranteed by ExecutionOrder — both run in Start phase.
        // We use LateStart via a one-frame delay to be safe.
        StartCoroutine(LateStartCoroutine());
    }

    private System.Collections.IEnumerator LateStartCoroutine()
    {
        // Wait one frame so ControladorJugador.Start() has run
        yield return null;

        var modelo = transform.Find("_PersonajeMixamo");
        if (modelo != null)
        {
            ConvertirMaterialesInstancia(modelo.gameObject);
            ConfigurarSombrasHDRP(modelo.gameObject);
            AlsasuaLogger.Info("ConfiguradorAAA",
                "Materiales HDRP/Lit aplicados al personaje instanciado.");
        }
        else
        {
            // Procedural body — nothing to upgrade beyond what ControladorJugador already did
            AlsasuaLogger.Info("ConfiguradorAAA",
                "Cuerpo procedural detectado — sin upgrade PBR necesario.");
        }

        // Integrate FightingMotions if available
        if (integrarFightingMotions)
        {
            IntegrarAnimacionesCombate();
        }
    }

    private void OnDestroy()
    {
        // Clean up runtime-created materials to prevent memory leaks
        for (int i = 0; i < _createdMaterials.Count; i++)
        {
            if (_createdMaterials[i] != null)
                Destroy(_createdMaterials[i]);
        }
        _createdMaterials.Clear();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  AUTO-DETECCIÓN DE PERSONAJE FBX
    // ═══════════════════════════════════════════════════════════════════════

    private GameObject AutoDetectarPersonajeFBX()
    {
        // Try known paths via Resources.Load (requires files in Resources/ folder)
        // Since character FBX are NOT in Resources/, we try direct asset loading
        // which only works in Editor. In builds, prefab must be assigned in Inspector.

#if UNITY_EDITOR
        for (int i = 0; i < KnownCharacterPaths.Length; i++)
        {
            string assetPath = "Assets/" + KnownCharacterPaths[i] + ".fbx";
            var obj = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (obj != null)
            {
                AlsasuaLogger.Info("ConfiguradorAAA",
                    $"Encontrado personaje FBX: {assetPath}");
                return obj;
            }
        }

        // Fallback: search for any FBX with "Ch20" or "Civil_" in name
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Model Ch20");
        if (guids.Length == 0)
            guids = UnityEditor.AssetDatabase.FindAssets("t:Model Civil_");

        for (int i = 0; i < guids.Length; i++)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
            if (path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
            {
                var obj = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (obj != null) return obj;
            }
        }
#endif

        // In builds — try Resources.Load as last resort
        for (int i = 0; i < KnownCharacterPaths.Length; i++)
        {
            var obj = Resources.Load<GameObject>(KnownCharacterPaths[i]);
            if (obj != null) return obj;
        }

        AlsasuaLogger.Warn("ConfiguradorAAA",
            "No se encontró ningún personaje FBX. Asigna prefabPersonaje en el Inspector.");
        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  UPGRADE MATERIALS ON PREFAB (before instantiation)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pre-processes the prefab's shared materials info. Actual material conversion
    /// happens on the instantiated copy in ConvertirMaterialesInstancia().
    /// </summary>
    private void UpgradeMaterialsOnPrefab(GameObject prefab)
    {
        // We don't modify shared materials on the prefab asset directly.
        // Material conversion happens on the instantiated copy to avoid
        // polluting the project asset.
        var renderers = prefab.GetComponentsInChildren<Renderer>(true);
        AlsasuaLogger.Info("ConfiguradorAAA",
            $"Prefab '{prefab.name}' tiene {renderers.Length} renderers — " +
            $"se convertirán a HDRP/Lit tras instanciar.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CONVERTIR MATERIALES DE INSTANCIA A HDRP/Lit
    // ═══════════════════════════════════════════════════════════════════════

    private void ConvertirMaterialesInstancia(GameObject instance)
    {
        Shader hdrpLit = Shader.Find("HDRP/Lit");
        if (hdrpLit == null)
        {
            AlsasuaLogger.Warn("ConfiguradorAAA",
                "Shader 'HDRP/Lit' no encontrado. ¿Está HDRP instalado?");
            return;
        }

        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            var renderer = renderers[r];
            var sharedMats = renderer.sharedMaterials;
            var newMats = new Material[sharedMats.Length];

            for (int m = 0; m < sharedMats.Length; m++)
            {
                var srcMat = sharedMats[m];
                if (srcMat == null)
                {
                    newMats[m] = null;
                    continue;
                }

                // Already HDRP/Lit — clone and adjust properties
                if (srcMat.shader == hdrpLit)
                {
                    var mat = new Material(srcMat);
                    mat.name = srcMat.name + "_AAA";
                    AplicarPropiedadesHDRP(mat);
                    newMats[m] = mat;
                    _createdMaterials.Add(mat);
                    continue;
                }

                // Create new HDRP/Lit material
                var hdrpMat = new Material(hdrpLit);
                hdrpMat.name = srcMat.name + "_HDRP";
                _createdMaterials.Add(hdrpMat);

                // Detect source type and convert
                if (EsShaderStandardSpecular(srcMat))
                {
                    ConvertirSpecularAHDRP(srcMat, hdrpMat);
                }
                else if (EsShaderStandard(srcMat))
                {
                    ConvertirMetallicAHDRP(srcMat, hdrpMat);
                }
                else
                {
                    // Generic fallback — grab whatever textures we can find
                    ConvertirGenericoAHDRP(srcMat, hdrpMat);
                }

                AplicarPropiedadesHDRP(hdrpMat);
                newMats[m] = hdrpMat;
            }

            renderer.materials = newMats;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SHADER DETECTION
    // ═══════════════════════════════════════════════════════════════════════

    private bool EsShaderStandardSpecular(Material mat)
    {
        string sn = mat.shader.name;
        return sn.Contains("Standard (Specular") || sn.Contains("Specular");
    }

    private bool EsShaderStandard(Material mat)
    {
        string sn = mat.shader.name;
        return sn.Contains("Standard") || sn.Contains("Legacy") ||
               sn.Contains("Diffuse") || sn.Contains("Bumped");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CONVERSIÓN: Standard (Specular Setup) → HDRP/Lit
    //  Ch20_nonPBR uses this workflow: Diffuse + Normal + Specular + Glossiness
    // ═══════════════════════════════════════════════════════════════════════

    private void ConvertirSpecularAHDRP(Material src, Material dst)
    {
        // BaseColor from Diffuse
        if (src.HasTexture(_MainTex))
        {
            var diffuse = src.GetTexture(_MainTex);
            dst.SetTexture(_BaseColorMap, diffuse);
        }
        if (src.HasColor(_BaseColor))
            dst.SetColor(_BaseColor, src.GetColor(Shader.PropertyToID("_Color")));
        else
            dst.SetColor(_BaseColor, Color.white);

        // Normal map
        if (src.HasTexture(_BumpMap))
        {
            dst.SetTexture(_NormalMap, src.GetTexture(_BumpMap));
            float srcNormalScale = src.HasFloat(_BumpScale) ? src.GetFloat(_BumpScale) : 1.0f;
            dst.SetFloat(_NormalScale, srcNormalScale);
        }

        // Specular → Metallic conversion:
        // For cloth/skin, specular is typically low → metallic ≈ 0
        // Smoothness from Glossiness: HDRP smoothness = Standard glossiness (same 0-1 range)
        float glossiness = src.HasFloat(_GlossMapScale) ? src.GetFloat(_GlossMapScale) : 0.3f;
        dst.SetFloat(_Metallic, metallicCloth);
        dst.SetFloat(_Smoothness, glossiness);

        // If specular map exists, estimate metallic from its luminance
        // For character cloth this is almost always 0, so we use the cloth preset
        if (src.HasTexture(_SpecGlossMap))
        {
            // We can't easily convert spec→metallic at runtime without ReadPixels.
            // Use conservative cloth values instead.
            dst.SetFloat(_Metallic, metallicCloth);
            dst.SetFloat(_Smoothness, smoothnessCloth);
        }

        // Emission
        CopiarEmision(src, dst);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CONVERSIÓN: Standard (Metallic Setup) → HDRP/Lit
    // ═══════════════════════════════════════════════════════════════════════

    private void ConvertirMetallicAHDRP(Material src, Material dst)
    {
        // BaseColor
        if (src.HasTexture(_MainTex))
            dst.SetTexture(_BaseColorMap, src.GetTexture(_MainTex));

        var colorPropId = Shader.PropertyToID("_Color");
        if (src.HasColor(colorPropId))
            dst.SetColor(_BaseColor, src.GetColor(colorPropId));
        else
            dst.SetColor(_BaseColor, Color.white);

        // Normal
        if (src.HasTexture(_BumpMap))
        {
            dst.SetTexture(_NormalMap, src.GetTexture(_BumpMap));
            float srcNormalScale = src.HasFloat(_BumpScale) ? src.GetFloat(_BumpScale) : 1.0f;
            dst.SetFloat(_NormalScale, srcNormalScale);
        }

        // Metallic + Smoothness
        if (src.HasTexture(_MetallicGlossMap))
        {
            dst.SetTexture(_MaskMap, src.GetTexture(_MetallicGlossMap));
        }
        float met = src.HasFloat(Shader.PropertyToID("_Metallic"))
            ? src.GetFloat(Shader.PropertyToID("_Metallic"))
            : metallicCloth;
        float smooth = src.HasFloat(_GlossMapScale) ? src.GetFloat(_GlossMapScale) : smoothnessCloth;
        dst.SetFloat(_Metallic, met);
        dst.SetFloat(_Smoothness, smooth);

        // Occlusion
        if (src.HasTexture(_OcclusionMap))
        {
            // HDRP uses MaskMap (R=metallic, G=occlusion, B=detail, A=smoothness)
            // Without generating a combined texture, we set the scalar values
        }

        CopiarEmision(src, dst);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CONVERSIÓN: Genérico / MeshyAI → HDRP/Lit
    //  MeshyAI exports: albedo, normal, roughness, metallic, emission textures
    // ═══════════════════════════════════════════════════════════════════════

    private void ConvertirGenericoAHDRP(Material src, Material dst)
    {
        // Try all common property names for albedo/diffuse
        Texture albedo = TryGetTexture(src, "_MainTex", "_BaseColorMap", "_Albedo",
            "_BaseMap", "_DiffuseMap", "_baseColorTexture");
        if (albedo != null)
            dst.SetTexture(_BaseColorMap, albedo);

        // Color tint
        Color tint = Color.white;
        foreach (string prop in new[] { "_Color", "_BaseColor", "_MainColor" })
        {
            int id = Shader.PropertyToID(prop);
            if (src.HasColor(id)) { tint = src.GetColor(id); break; }
        }
        dst.SetColor(_BaseColor, tint);

        // Normal
        Texture normal = TryGetTexture(src, "_BumpMap", "_NormalMap", "_Normal",
            "_normalTexture");
        if (normal != null)
        {
            dst.SetTexture(_NormalMap, normal);
            dst.SetFloat(_NormalScale, normalScale);
        }

        // Roughness → invert to smoothness
        Texture roughness = TryGetTexture(src, "_RoughnessMap", "_Roughness",
            "_metallicRoughnessTexture");
        // We can't invert a texture at runtime without compute — use scalar
        float smoothVal = smoothnessCloth;
        foreach (string prop in new[] { "_Glossiness", "_Smoothness", "_GlossMapScale" })
        {
            int id = Shader.PropertyToID(prop);
            if (src.HasFloat(id)) { smoothVal = src.GetFloat(id); break; }
        }
        // If we found a roughness property as float, invert it
        foreach (string prop in new[] { "_Roughness", "_RoughnessFactor" })
        {
            int id = Shader.PropertyToID(prop);
            if (src.HasFloat(id)) { smoothVal = 1.0f - src.GetFloat(id); break; }
        }
        dst.SetFloat(_Smoothness, smoothVal);

        // Metallic
        Texture metallicTex = TryGetTexture(src, "_MetallicGlossMap", "_MetallicMap",
            "_Metallic");
        if (metallicTex != null)
            dst.SetTexture(_MaskMap, metallicTex);

        float metVal = metallicCloth;
        foreach (string prop in new[] { "_Metallic", "_MetallicFactor" })
        {
            int id = Shader.PropertyToID(prop);
            if (src.HasFloat(id)) { metVal = src.GetFloat(id); break; }
        }
        dst.SetFloat(_Metallic, metVal);

        // Emission
        CopiarEmision(src, dst);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    private Texture TryGetTexture(Material mat, params string[] propertyNames)
    {
        for (int i = 0; i < propertyNames.Length; i++)
        {
            int id = Shader.PropertyToID(propertyNames[i]);
            if (mat.HasTexture(id))
            {
                var tex = mat.GetTexture(id);
                if (tex != null) return tex;
            }
        }
        return null;
    }

    private void CopiarEmision(Material src, Material dst)
    {
        Texture emissionTex = TryGetTexture(src, "_EmissionMap", "_EmissiveColorMap",
            "_EmissiveMap", "_emissiveTexture");
        if (emissionTex != null)
        {
            dst.SetTexture(_EmissiveColorMap, emissionTex);
            dst.SetColor(_EmissiveColor, Color.white);
            dst.SetFloat(_EmissiveIntensity, 1.0f);

            // Enable emission keyword for HDRP
            dst.EnableKeyword("_EMISSIVE_COLOR_MAP");
            dst.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }
    }

    private void AplicarPropiedadesHDRP(Material mat)
    {
        // Ensure cloth-appropriate defaults if not set
        if (!mat.HasTexture(_MaskMap))
        {
            mat.SetFloat(_Metallic, metallicCloth);
            mat.SetFloat(_Smoothness, smoothnessCloth);
        }

        mat.SetFloat(_NormalScale, normalScale);

        // GPU Instancing — project convention
        mat.enableInstancing = true;

        // Enable relevant HDRP keywords
        if (mat.HasTexture(_NormalMap))
            mat.EnableKeyword("_NORMALMAP");
        if (mat.HasTexture(_MaskMap))
            mat.EnableKeyword("_MASKMAP");
        if (mat.HasTexture(_BaseColorMap))
            mat.EnableKeyword("_BASE_COLOR_MAP");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CONFIGURAR SOMBRAS HDRP
    // ═══════════════════════════════════════════════════════════════════════

    private void ConfigurarSombrasHDRP(GameObject instance)
    {
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var rend = renderers[i];

            // Cast shadows — essential for HDRP contact shadows and cascaded shadow maps
            rend.shadowCastingMode = ShadowCastingMode.On;
            rend.receiveShadows = true;

            // Two-sided = false for characters (closed meshes)
            // HDRP uses material-level double-sided, not renderer-level.
            // Set the material property instead.
            var mats = rend.materials;
            for (int m = 0; m < mats.Length; m++)
            {
                if (mats[m] != null && mats[m].HasFloat(Shader.PropertyToID("_DoubleSidedEnable")))
                {
                    mats[m].SetFloat(Shader.PropertyToID("_DoubleSidedEnable"), 0f);
                }
            }

            // Enable motion vectors for HDRP temporal AA
            rend.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  INTEGRACIÓN FIGHTING MOTIONS
    // ═══════════════════════════════════════════════════════════════════════

    private void IntegrarAnimacionesCombate()
    {
        var modelo = transform.Find("_PersonajeMixamo");
        if (modelo == null) return;

        var animator = modelo.GetComponent<Animator>()
                    ?? modelo.GetComponentInChildren<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null) return;

        // Check if FightingMotions FBX directory exists and load clips
#if UNITY_EDITOR
        string fbxDir = "Assets/" + FightingMotionsFBXPath;
        if (!System.IO.Directory.Exists(fbxDir))
        {
            AlsasuaLogger.Info("ConfiguradorAAA",
                "FightingMotionsVolume1 no encontrado — combate cuerpo a cuerpo desactivado.");
            return;
        }

        // Load all animation clips from FBX files in the FightingMotions pack
        string[] fbxFiles = System.IO.Directory.GetFiles(fbxDir, "*.fbx",
            System.IO.SearchOption.AllDirectories);

        var combatClips = new List<AnimationClip>();
        for (int i = 0; i < fbxFiles.Length; i++)
        {
            string assetPath = fbxFiles[i].Replace('\\', '/');
            var objects = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int j = 0; j < objects.Length; j++)
            {
                if (objects[j] is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    combatClips.Add(clip);
                }
            }
        }

        if (combatClips.Count == 0) return;

        // Add clips to an AnimatorOverrideController layered on top of the existing controller
        var overrideController = new AnimatorOverrideController(animator.runtimeAnimatorController);
        overrideController.name = "JugadorAnimator_ConCombate";

        // Get existing clip-slot pairs
        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(
            overrideController.overridesCount);
        overrideController.GetOverrides(overrides);

        // Map fighting clips to placeholder slots by name matching
        // Convention: slot names like "Punch", "Kick", "Block" match FightingMotions clip names
        for (int i = 0; i < overrides.Count; i++)
        {
            var slot = overrides[i];
            if (slot.Value != null) continue; // Already has a clip

            string slotName = slot.Key != null ? slot.Key.name.ToLowerInvariant() : "";
            for (int c = 0; c < combatClips.Count; c++)
            {
                string clipName = combatClips[c].name.ToLowerInvariant();
                // Match by keywords: punch, kick, block, knockdown
                if (SlotMatchesCombatClip(slotName, clipName))
                {
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(
                        slot.Key, combatClips[c]);
                    combatClips.RemoveAt(c);
                    break;
                }
            }
        }

        overrideController.ApplyOverrides(overrides);
        animator.runtimeAnimatorController = overrideController;

        AlsasuaLogger.Info("ConfiguradorAAA",
            $"FightingMotions integrado: {fbxFiles.Length} FBX procesados, " +
            $"clips de combate disponibles para manifestación.");
#else
        // In builds, FightingMotions clips must be pre-assigned in the AnimatorController
        // or loaded from AssetBundles. Runtime FBX loading is not supported.
        AlsasuaLogger.Info("ConfiguradorAAA",
            "FightingMotions: en build, usa AnimatorController pre-configurado.");
#endif
    }

    /// <summary>
    /// Checks if an animator slot name conceptually matches a fighting motion clip name.
    /// </summary>
    private bool SlotMatchesCombatClip(string slotName, string clipName)
    {
        // Keywords for melee combat animations
        string[] keywords = { "punch", "kick", "block", "knockdown", "hook",
                              "uppercut", "jab", "cross", "elbow", "knee",
                              "dodge", "guard", "hit", "combo", "fight" };

        for (int i = 0; i < keywords.Length; i++)
        {
            if (slotName.Contains(keywords[i]) && clipName.Contains(keywords[i]))
                return true;
        }
        return false;
    }
}
