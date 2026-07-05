// AlsasuaPlayerController.h
// ═══════════════════════════════════════════════════════════════════════════
//  PlayerController del jugador (UE 5.4). Gestiona la sensibilidad del ratón,
//  el modo de entrada (solo juego) y la pausa.
// ═══════════════════════════════════════════════════════════════════════════

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/PlayerController.h"
#include "AlsasuaPlayerController.generated.h"

UCLASS()
class ALSASUAMANIFA_API AAlsasuaPlayerController : public APlayerController
{
    GENERATED_BODY()

public:
    AAlsasuaPlayerController();

    /** Sensibilidad horizontal del ratón (multiplicador del yaw). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Raton", meta = (ClampMin = "0.05"))
    float MouseSensitivityX = 1.0f;

    /** Sensibilidad vertical del ratón (multiplicador del pitch). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Raton", meta = (ClampMin = "0.05"))
    float MouseSensitivityY = 1.0f;

    /** Ajusta la sensibilidad del ratón en tiempo de ejecución (menú de opciones). */
    UFUNCTION(BlueprintCallable, Category = "Raton")
    void SetMouseSensitivity(float X, float Y);

    /** Alterna el estado de pausa del juego. */
    UFUNCTION(BlueprintCallable, Category = "Juego")
    void TogglePause();

protected:
    virtual void BeginPlay() override;

    /** true mientras el juego está en pausa (control interno de TogglePause). */
    UPROPERTY(BlueprintReadOnly, Category = "Juego")
    bool bJuegoEnPausa = false;
};
