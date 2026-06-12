// Assets/Scripts/SistemaWater.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA AGUA — HDRP Water System para el río Arakil/Burunda
//
//  Conduce una WaterSurface (River) reaccionando al clima:
//    • Tormenta → oleaje alto, foam máximo, viento fuerte
//    • Lluvia   → oleaje medio, algo de foam
//    • Calma    → agua tersa, sin foam
//
//  ACTIVACIÓN (solo editor, una vez):
//    1. HDRP Asset → Water → Water Surfaces: ON
//    2. Crear un GameObject WaterSurface (tipo River) sobre el cauce del Arakil
//    3. Asignarlo al campo waterSurface de este componente
//    4. Añadir el define ALSASUA_WATER en Project Settings → Player → Scripting Defines
//
//  Sin el define el script es un no-op que compila limpiamente.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
#if ALSASUA_WATER
using UnityEngine.Rendering.HighDefinition;
#endif

public class SistemaWater : MonoBehaviour
{
    public static SistemaWater Instance { get; private set; }

#if ALSASUA_WATER
    [SerializeField] WaterSurface waterSurface;
#endif

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

#if ALSASUA_WATER
    void Update()
    {
        if (waterSurface == null) return;
        var clima = SistemaClimaExtension.EstadoActual;

        switch (clima)
        {
            case SistemaClima.EstadoClima.Tormenta:
                waterSurface.ripples.windSpeed = 12f;
                waterSurface.foam.enable       = true;
                break;
            case SistemaClima.EstadoClima.LluviaLigera:
                waterSurface.ripples.windSpeed = 5f;
                waterSurface.foam.enable       = true;
                break;
            default:
                waterSurface.ripples.windSpeed = 1f;
                waterSurface.foam.enable       = false;
                break;
        }
    }
#endif
}
