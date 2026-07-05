// AlsasuaGameMode.cpp
// ═══════════════════════════════════════════════════════════════════════════
//  Implementación del GameMode principal (UE 5.4).
// ═══════════════════════════════════════════════════════════════════════════

#include "AlsasuaGameMode.h"
#include "AlsasuaCharacter.h"
#include "AlsasuaPlayerController.h"
#include "AlsasuaHUD.h"

AAlsasuaGameMode::AAlsasuaGameMode()
{
    // El pawn por defecto es nuestro personaje jugable.
    // En el proyecto real se puede sobreescribir con un Blueprint (BP_JugadorAlsasua)
    // vía DefaultGame.ini/DefaultEngine.ini para asignar la malla SK_Mannequin,
    // IMC/IA y el AnimBP de Motion Matching sin recompilar C++.
    DefaultPawnClass = AAlsasuaCharacter::StaticClass();

    // Controlador del jugador (sensibilidad de ratón, pausa, modo de entrada).
    PlayerControllerClass = AAlsasuaPlayerController::StaticClass();

    // HUD: crea el widget UMG principal. Asignar la clase del WBP en un Blueprint
    // derivado de AAlsasuaHUD, o dejar este como base.
    HUDClass = AAlsasuaHUD::StaticClass();
}
