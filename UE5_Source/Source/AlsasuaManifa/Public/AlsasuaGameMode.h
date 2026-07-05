// AlsasuaGameMode.h
// ═══════════════════════════════════════════════════════════════════════════
//  GameMode principal de AlsasuaManifa. Establece AAlsasuaCharacter como el
//  pawn por defecto que se posee al arrancar la partida.
// ═══════════════════════════════════════════════════════════════════════════

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "AlsasuaGameMode.generated.h"

UCLASS()
class ALSASUAMANIFA_API AAlsasuaGameMode : public AGameModeBase
{
    GENERATED_BODY()

public:
    // Constructor: fija el DefaultPawnClass a AAlsasuaCharacter.
    AAlsasuaGameMode();
};
