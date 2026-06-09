// Assets/Scripts/SistemaOcclusion.cs  (+ Editor/UtilOcclusionEstatica.cs)
// ═══════════════════════════════════════════════════════════════════════════
//  OCCLUSION CULLING — herramienta de marcado estático + activación runtime
//
//  Runtime: activa GPU Occlusion Culling en la cámara principal si el
//  tier de calidad lo permite (tier 0-2). En tier 3 (Performance) se
//  desactiva para ahorrar CPU de occlusion queries.
//
//  ACTIVACIÓN (solo editor, una vez):
//    1. Menú Alsasua ▸ Occlusion ▸ Marcar geometría estática
//       (marca automáticamente edificios, suelo, muros como Occluder/Occludee)
//    2. Window ▸ Rendering ▸ Occlusion Culling ▸ Bake
//    3. Este script activa Camera.useOcclusionCulling en runtime según el tier
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public class SistemaOcclusion : MonoBehaviour
{
    public static SistemaOcclusion Instance { get; private set; }

    [SerializeField] bool activarEnTierPerformance = false;

    Camera _cam;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _cam = Camera.main;
    }

    void Update()
    {
        if (_cam == null) { _cam = Camera.main; return; }

        int tier = SistemaOptimizacion.TierCalidad;
        bool debeActivar = tier <= 2 || activarEnTierPerformance;
        if (_cam.useOcclusionCulling != debeActivar)
            _cam.useOcclusionCulling = debeActivar;
    }
}

// ── Editor tool — solo compila en el editor ──────────────────────────────
#if UNITY_EDITOR
namespace AlsasuaEditor
{
    using UnityEditor;

    public static class UtilOcclusionEstatica
    {
        [MenuItem("Alsasua/Occlusion/Marcar geometría estática")]
        static void MarcarGeometria()
        {
            string[] nombresOccluder = { "Edificio", "Building", "Muro", "Wall",
                                         "Suelo", "Ground", "Tunel", "Tunnel" };
            int marcados = 0;
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                bool esOccluder = false;
                foreach (var n in nombresOccluder)
                    if (go.name.Contains(n)) { esOccluder = true; break; }

                if (esOccluder || go.GetComponent<MeshRenderer>() != null)
                {
                    Undo.RecordObject(go, "Marcar Occlusion Static");
                    GameObjectUtility.SetStaticEditorFlags(go,
                        StaticEditorFlags.OccluderStatic |
                        StaticEditorFlags.OccludeeStatic |
                        StaticEditorFlags.BatchingStatic);
                    marcados++;
                }
            }
            Debug.Log($"[OcclusionUtil] {marcados} objetos marcados como static.");
        }
    }
}
#endif
