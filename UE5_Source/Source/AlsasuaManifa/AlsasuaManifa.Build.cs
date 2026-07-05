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

        // Forzar IWYU (include-what-you-use), obligatorio a partir de UE5.2+.
        IWYUSupport = IWYUSupport.Full;

        // ── Dependencias públicas (expuestas a otros módulos que dependan de este) ──
        PublicDependencyModuleNames.AddRange(new string[]
        {
            "Core",
            "CoreUObject",
            "Engine",
            "InputCore",

            // Sistema de entrada moderno (UE5.1+): IMC_Jugador, IA_Mover, etc.
            "EnhancedInput",

            // IA y navegación (enemigos que patrullan sobre NavMesh).
            "AIModule",
            "NavigationSystem",
        });

        // ── Dependencias privadas (solo para la implementación .cpp) ──
        //  Los módulos de GASP son plugins EXPERIMENTALES: además de listarlos
        //  aquí hay que habilitarlos en el .uproject (ver README).
        PrivateDependencyModuleNames.AddRange(new string[]
        {
            // Motion Matching / Game Animation Sample Project (GASP).
            "PoseSearch",                    // base de datos de poses + motion matching
            "MotionTrajectory",              // UCharacterTrajectoryComponent (trayectoria)
            "AnimationWarping",              // orientation/stride/slope warping
            "AnimationLocomotionLibrary",    // nodos de anim de locomoción reutilizables
            "Chooser",                       // tablas de selección de animación (Chooser)

            // Utilidades de animación / gameplay usadas por los sistemas anteriores.
            "AnimationCore",
            "GameplayTags",
        });
    }
}
