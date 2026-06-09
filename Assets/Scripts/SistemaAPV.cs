// Assets/Scripts/SistemaAPV.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ADAPTIVE PROBE VOLUMES — blending de escenarios día/noche
//
//  Mezcla en runtime los APV lighting scenarios "Day" ↔ "Night" leyendo
//  el global _GlobalNightLevel que publica SistemaVolumenHDRP.
//
//  ACTIVACIÓN (solo editor, una vez):
//    1. HDRP Asset → Lighting → Probe Volumes: ON
//    2. Colocar un Adaptive Probe Volume en la escena (cover entire scene)
//    3. Window → Rendering → Lighting → Probe Volumes → Baking Set
//    4. Crear scenarios: "Day" y "Night"
//    5. Bakear (Bake All)
//    6. Añadir el define ALSASUA_APV en Project Settings → Player → Scripting Defines
//
//  Sin el define el script es un no-op que compila limpiamente.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
#if ALSASUA_APV
using UnityEngine.Rendering;
#endif

public class SistemaAPV : MonoBehaviour
{
    public static SistemaAPV Instance { get; private set; }

    [SerializeField] string scenarioDia   = "Day";
    [SerializeField] string scenarioNoche = "Night";

    static readonly int ID_NightLevel = Shader.PropertyToID("_GlobalNightLevel");

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

#if ALSASUA_APV
    void Update()
    {
        float nightLevel = Shader.GetGlobalFloat(ID_NightLevel);
        ProbeReferenceVolume.instance.BlendLightingScenario(scenarioNoche, nightLevel);
    }
#endif
}
