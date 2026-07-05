// AlsasuaGameMode.cpp
// ═══════════════════════════════════════════════════════════════════════════
//  Implementación del GameMode principal.
// ═══════════════════════════════════════════════════════════════════════════

#include "AlsasuaGameMode.h"
#include "AlsasuaCharacter.h"

AAlsasuaGameMode::AAlsasuaGameMode()
{
    // El pawn por defecto es nuestro personaje jugable.
    // En el proyecto real se puede sobreescribir con un Blueprint (BP_AlsasuaCharacter)
    // vía DefaultEngine.ini para asignar la malla SK_Mannequin, IMC/IA y el AnimBP
    // de Motion Matching sin recompilar C++.
    DefaultPawnClass = AAlsasuaCharacter::StaticClass();
}
