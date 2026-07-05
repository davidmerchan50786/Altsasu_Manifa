// AlsasuaPlayerController.cpp
// ═══════════════════════════════════════════════════════════════════════════
//  Implementación del PlayerController del jugador (UE 5.4).
// ═══════════════════════════════════════════════════════════════════════════

#include "AlsasuaPlayerController.h"
#include "Kismet/GameplayStatics.h"

AAlsasuaPlayerController::AAlsasuaPlayerController()
{
    // La sensibilidad se aplica en el personaje leyendo estos valores.
    MouseSensitivityX = 1.0f;
    MouseSensitivityY = 1.0f;
}

void AAlsasuaPlayerController::BeginPlay()
{
    Super::BeginPlay();

    // No mostrar el cursor: juego de acción en tercera persona.
    bShowMouseCursor = false;

    // Modo de entrada solo-juego (el ratón controla la cámara, no la UI).
    FInputModeGameOnly ModoJuego;
    SetInputMode(ModoJuego);
}

void AAlsasuaPlayerController::SetMouseSensitivity(float X, float Y)
{
    // Clamp mínimo para evitar sensibilidad nula.
    MouseSensitivityX = FMath::Max(0.05f, X);
    MouseSensitivityY = FMath::Max(0.05f, Y);
}

void AAlsasuaPlayerController::TogglePause()
{
    bJuegoEnPausa = !bJuegoEnPausa;
    UGameplayStatics::SetGamePaused(this, bJuegoEnPausa);
}
