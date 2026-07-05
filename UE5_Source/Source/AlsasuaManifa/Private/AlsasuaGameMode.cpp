// AlsasuaGameMode.cpp
// ═══════════════════════════════════════════════════════════════════════════
//  Implementación del GameMode principal (UE 5.4).
// ═══════════════════════════════════════════════════════════════════════════

#include "AlsasuaGameMode.h"
#include "AlsasuaCharacter.h"
#include "GameFramework/HUD.h"

AAlsasuaGameMode::AAlsasuaGameMode()
{
    // El pawn por defecto es nuestro personaje jugable.
    // En el proyecto real se puede sobreescribir con un Blueprint (BP_JugadorAlsasua)
    // vía DefaultEngine.ini para asignar la malla SK_Mannequin, IMC/IA y el AnimBP
    // de Motion Matching sin recompilar C++.
    DefaultPawnClass = AAlsasuaCharacter::StaticClass();

    // HUD por defecto (stub). Sustituir por un AHUD/UUserWidget propio cuando
    // se implemente la interfaz (barras de salud/aguante, minimapa, etc.).
    HUDClass = AHUD::StaticClass();
}
