// Assets/Scripts/Editor/ReparadorJugadorVisual.cs
// Tools → Alsasua → Assets → 🧍 Reparar Jugador (modelo + animaciones)
//
// Problema que corrige (jun 2026, ver pantallazo "jugador negro en T-pose"):
//   · Jugador_Altsasua.prefab usaba como modelo un FBX de ANIMACIÓN del
//     Guardia Civil (Angry_To_Tantrum_Sit) — sin textura → negro.
//   · El Animator tenía m_Avatar: 0 — sin Avatar humanoide → T-pose eterna.
//
// Solución:
//   1. Reimporta Civil_SummerStreet_01 (único civil texturizado) como Humanoid
//      → su Avatar permite retargetear los clips GC_* del Guardia Civil.
//   2. Crea material HDRP/Lit con albedo + normal + roughness + emission.
//   3. Genera Jugador_Modelo_SummerStreet.prefab (FBX + material + Animator
//      con Avatar y JugadorAnimator.controller).
//   4. Conecta ese prefab al campo 'prefabPersonaje' de ControladorJugador en
//      Jugador_Altsasua.prefab y elimina el modelo roto embebido.

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Linq;

public static class ReparadorJugadorVisual
{
    const string FBX_PATH    = "Assets/_ExtractedAssets/Personajes/MeshyAI/Civil_SummerStreet_01/Meshy_AI_Casual_Summer_Street__0421162005_texture.fbx";
    const string TEX_BASE    = "Assets/_ExtractedAssets/Personajes/MeshyAI/Civil_SummerStreet_01/Meshy_AI_Casual_Summer_Street__0421162005_texture";
    const string CTRL_PATH   = "Assets/Animators/JugadorAnimator.controller";
    const string MAT_PATH    = "Assets/Materials/Personajes/Jugador_SummerStreet.mat";
    const string MODELO_PATH = "Assets/Prefabs/Personajes/Jugador_Modelo_SummerStreet.prefab";
    const string PLAYER_PATH = "Assets/Prefabs/Personajes/Jugador_Altsasua.prefab";

    [MenuItem("Tools/Alsasua/Assets/🧍 Reparar Jugador (modelo + animaciones)", priority = 18)]
    public static void Reparar()
    {
        // ── 1. FBX → rig Humanoid con Avatar propio ──────────────────────────
        var imp = AssetImporter.GetAtPath(FBX_PATH) as ModelImporter;
        if (imp == null)
        {
            EditorUtility.DisplayDialog("FBX no encontrado", FBX_PATH, "OK");
            return;
        }
        if (imp.animationType != ModelImporterAnimationType.Human)
        {
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.SaveAndReimport();
        }

        var avatar = AssetDatabase.LoadAllAssetsAtPath(FBX_PATH)
                                  .OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            EditorUtility.DisplayDialog("Avatar no válido",
                "El FBX no generó un Avatar humanoide válido.\n" +
                "Revisa el mapeo de huesos en el importador (pestaña Rig).", "OK");
            return;
        }

        // ── 2. Material HDRP con las texturas Meshy ──────────────────────────
        var mat = CrearMaterial();

        // ── 3. Prefab del modelo ──────────────────────────────────────────────
        var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FBX_PATH);
        var root = Object.Instantiate(fbxAsset);
        root.name = "Jugador_Modelo_SummerStreet";

        // Escala a altura humana (~1.75 m) si viene desproporcionado
        var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (skins.Length > 0)
        {
            var b = skins[0].bounds;
            foreach (var r in skins) b.Encapsulate(r.bounds);
            if (b.size.y > 0.1f)
            {
                float esc = 1.75f / b.size.y;
                if (esc < 0.7f || esc > 1.3f) root.transform.localScale = Vector3.one * esc;
            }
        }

        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();

        var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CTRL_PATH);
        var anim = root.GetComponent<Animator>() ?? root.AddComponent<Animator>();
        anim.avatar = avatar;
        anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false;

        var modeloPrefab = PrefabUtility.SaveAsPrefabAsset(root, MODELO_PATH);
        Object.DestroyImmediate(root);

        // ── 4. Conectar al prefab del jugador y limpiar el modelo roto ───────
        var contents = PrefabUtility.LoadPrefabContents(PLAYER_PATH);
        try
        {
            // Eliminar cualquier subárbol con SkinnedMeshRenderer (modelo viejo)
            foreach (var smr in contents.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null) continue;
                Transform top = smr.transform;
                while (top.parent != null && top.parent != contents.transform)
                    top = top.parent;
                if (top != contents.transform)
                    Object.DestroyImmediate(top.gameObject);
            }

            // prefabPersonaje = modelo nuevo (ControladorJugador lo instancia
            // como '_PersonajeMixamo' y busca ahí el Animator)
            var cj = contents.GetComponentInChildren<ControladorJugador>(true);
            if (cj != null)
            {
                var so = new SerializedObject(cj);
                var prop = so.FindProperty("prefabPersonaje");
                if (prop != null)
                {
                    prop.objectReferenceValue = modeloPrefab;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                else Debug.LogWarning("[ReparadorJugador] Campo 'prefabPersonaje' no encontrado.");
            }
            else Debug.LogWarning("[ReparadorJugador] ControladorJugador no está en el prefab.");

            // Animator del root (si existe): avatar + controller coherentes
            var animRoot = contents.GetComponent<Animator>();
            if (animRoot != null)
            {
                animRoot.avatar = avatar;
                animRoot.runtimeAnimatorController = ctrl;
            }

            PrefabUtility.SaveAsPrefabAsset(contents, PLAYER_PATH);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("🧍 Jugador reparado",
            "• Modelo: Civil SummerStreet (texturizado, HDRP)\n" +
            "• Avatar humanoide válido → clips GC_* retargetean\n" +
            "• Controller: JugadorAnimator\n\n" +
            "Pulsa Play para verlo. Si las animaciones no encajan,\n" +
            "revisa los estados del JugadorAnimator.controller.", "OK");
    }

    static Material CrearMaterial()
    {
        var existente = AssetDatabase.LoadAssetAtPath<Material>(MAT_PATH);
        if (existente != null) return existente;

        if (!AssetDatabase.IsValidFolder("Assets/Materials/Personajes"))
            AssetDatabase.CreateFolder("Assets/Materials", "Personajes");

        var mat = new Material(Shader.Find("HDRP/Lit")) { name = "Jugador_SummerStreet" };

        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(TEX_BASE + ".png");
        if (albedo != null) mat.SetTexture("_BaseColorMap", albedo);

        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(TEX_BASE + "_normal.png");
        if (normal != null)
        {
            var nImp = AssetImporter.GetAtPath(TEX_BASE + "_normal.png") as TextureImporter;
            if (nImp != null && nImp.textureType != TextureImporterType.NormalMap)
            {
                nImp.textureType = TextureImporterType.NormalMap;
                nImp.SaveAndReimport();
            }
            mat.SetTexture("_NormalMap", normal);
            mat.SetFloat("_NormalScale", 1f);
        }

        var rough = AssetDatabase.LoadAssetAtPath<Texture2D>(TEX_BASE + "_roughness.png");
        if (rough != null) mat.SetTexture("_MaskMap", rough);

        var emis = AssetDatabase.LoadAssetAtPath<Texture2D>(TEX_BASE + "_emission.png");
        if (emis != null)
        {
            mat.SetTexture("_EmissiveColorMap", emis);
            mat.SetColor("_EmissiveColor", Color.white * 0.3f);
        }

        mat.SetFloat("_Smoothness", 0.3f);
        mat.SetFloat("_Metallic",   0f);
        mat.enableInstancing = true;

        AssetDatabase.CreateAsset(mat, MAT_PATH);
        return mat;
    }
}
#endif
