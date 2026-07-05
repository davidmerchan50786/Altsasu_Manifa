// AlsasuaManifa.Build.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Reglas de compilación del módulo primario del juego AlsasuaManifa.
//  Objetivo EXACTO: Unreal Engine 5.4.
//
//  Dependencias necesarias para:
//    · Enhanced Input          → sistema de entrada moderno (IMC/IA)
//    · Motion Matching / GASP   → PoseSearch, MotionTrajectory, AnimationWarping,
//                                 AnimationLocomotionLibrary, Chooser
//    · IA / Navegación          → AIModule, NavigationSystem (NavMesh de enemigos)
//    · Stubs de GAS             → GameplayAbilities, GameplayTags, GameplayTasks
// ═══════════════════════════════════════════════════════════════════════════

using UnrealBuildTool;

public class AlsasuaManifa : ModuleRules
{
    public AlsasuaManifa(ReadOnlyTargetRules Target) : base(Target)
    {
        // Cabeceras precompiladas explícitas (recomendado por Epic para módulos de juego).
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

        // C++20: estándar oficial de UE 5.4 para módulos de juego.
        CppStandard = CppStandardVersion.Cpp20;

        // Desactivar Unity build → compilación IWYU pura (detecta includes que faltan).
        bUseUnity = false;

        // Forzar IWYU (include-what-you-use), obligatorio desde UE5.2+.
        IWYUSupport = IWYUSupport.Full;

        // ── Dependencias públicas (expuestas a módulos que dependan de este) ──
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

            // Stubs listos para GAS (Gameplay Ability System).
            "GameplayAbilities",
            "GameplayTags",
            "GameplayTasks",
        });

        // ── Dependencias privadas (solo para la implementación .cpp) ──
        //  Los módulos de GASP son plugins: además de listarlos aquí hay que
        //  habilitarlos en AlsasuaManifa.uproject (ya incluidos en este repo).
        PrivateDependencyModuleNames.AddRange(new string[]
        {
            // Motion Matching / Game Animation Sample Project (GASP).
            "PoseSearch",                    // base de datos de poses + motion matching
            "MotionTrajectory",              // UCharacterTrajectoryComponent (trayectoria)
            "AnimationWarping",              // orientation/stride/slope warping
            "AnimationLocomotionLibrary",    // nodos de anim de locomoción reutilizables
            "Chooser",                       // tablas de selección de animación (Chooser)

            // Utilidades de animación usadas por los sistemas anteriores.
            "AnimationCore",
        });
    }
}
