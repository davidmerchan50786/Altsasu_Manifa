// AlsasuaHUD.cpp
// ═══════════════════════════════════════════════════════════════════════════
//  Implementación del HUD del juego (UE 5.4).
// ═══════════════════════════════════════════════════════════════════════════

#include "AlsasuaHUD.h"
#include "Blueprint/UserWidget.h"

void AAlsasuaHUD::BeginPlay()
{
    Super::BeginPlay();

    // Crear el widget UMG principal y añadirlo a la pantalla (si hay clase asignada).
    if (HUDWidgetClass)
    {
        APlayerController* PC = GetOwningPlayerController();
        HUDWidget = CreateWidget<UUserWidget>(PC, HUDWidgetClass);
        if (HUDWidget)
        {
            HUDWidget->AddToViewport();
        }
    }
}
