// AlsasuaManifa.Build.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Reglas de compilación del módulo primario del juego AlsasuaManifa (UE5.4+).
//
//  Incluye todas las dependencias necesarias para:
//    · Enhanced Input          → sistema de entrada moderno (IMC/IA)
//    · Motion Matching / GASP   → PoseSearch, MotionTrajectory, AnimationWarping,
//                                 AnimationLocomotionLibrary, Chooser
//    · IA / Navegación          → AIModule, NavigationSystem (NavMesh de enemigos)
// ═══════════════════════════════════════════════════════════════════════════

using UnrealBuildTool;

public class AlsasuaManifa : ModuleRules
{
    public AlsasuaManifa(ReadOnlyTargetRules Target) : base(Target)
    {
        // Usar cabeceras precompiladas explícitas (recomendado por Epic para módulos de juego).
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

        // ── Dependencias públicas (expuestas a otros módulos que dependan de este) ──
        PublicDependencyModuleNames.AddRange(new string[]
        {
            "Core",
            "CoreUObject",
            "Engine",
            "InputCore",

            // Sistema de entrada moderno (UE5.1+): IMC_Jugador, IA_Mover, etc.
            "EnhancedInput",

            // Motion Matching / Game Animation Sample Project (GASP).
            "PoseSearch",                    // base de datos de poses + motion matching
            "MotionTrajectory",              // UMotionTrajectoryComponent (trayectoria)
            "AnimationWarping",              // orientation/stride/slope warping
            "AnimationLocomotionLibrary",    // nodos de anim de locomoción reutilizables
            "Chooser",                        // tablas de selección de animación (Chooser)

            // IA y navegación (enemigos que patrullan sobre NavMesh).
            "AIModule",
            "NavigationSystem",
        });

        // ── Dependencias privadas (solo para la implementación .cpp) ──
        PrivateDependencyModuleNames.AddRange(new string[]
        {
            // Añade aquí módulos usados solo internamente (p. ej. "Slate", "UMG").
        });
    }
}
