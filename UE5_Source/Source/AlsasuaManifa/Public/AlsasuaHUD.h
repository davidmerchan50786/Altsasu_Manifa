// AlsasuaHUD.h
// ═══════════════════════════════════════════════════════════════════════════
//  HUD del juego (UE 5.4). Crea e inserta en pantalla el widget UMG principal
//  (barras de salud/aguante, minimapa, etc.).
// ═══════════════════════════════════════════════════════════════════════════

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/HUD.h"
#include "AlsasuaHUD.generated.h"

class UUserWidget;

UCLASS()
class ALSASUAMANIFA_API AAlsasuaHUD : public AHUD
{
    GENERATED_BODY()

public:
    /** Clase del widget UMG principal (asignar un WBP en el editor). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "HUD")
    TSubclassOf<UUserWidget> HUDWidgetClass;

    /** Instancia del widget creada en BeginPlay. */
    UPROPERTY(BlueprintReadOnly, Category = "HUD")
    TObjectPtr<UUserWidget> HUDWidget;

protected:
    virtual void BeginPlay() override;
};
