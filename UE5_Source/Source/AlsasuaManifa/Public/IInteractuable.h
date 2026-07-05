// IInteractuable.h
// ═══════════════════════════════════════════════════════════════════════════
//  Interfaz para objetos con los que el jugador puede interactuar (UE 5.4).
//  Cualquier Actor (puerta, cofre, NPC, palanca…) que implemente esta interfaz
//  recibirá una llamada a Interactuar() cuando el jugador pulse IA_Interactuar
//  y lo tenga en la mira (line trace desde el personaje).
// ═══════════════════════════════════════════════════════════════════════════

#pragma once

#include "CoreMinimal.h"
#include "UObject/Interface.h"
#include "IInteractuable.generated.h"

class AAlsasuaCharacter;

// Declaración de la UInterface (parte requerida por el sistema de reflexión).
UINTERFACE(MinimalAPI, BlueprintType)
class UInteractuable : public UInterface
{
    GENERATED_BODY()
};

/**
 * Interfaz nativa/Blueprint para objetos interactuables.
 * Implementar en C++ (override de Interactuar_Implementation) o en Blueprint.
 */
class ALSASUAMANIFA_API IInteractuable
{
    GENERATED_BODY()

public:
    /**
     * Se llama cuando el jugador interactúa con este objeto.
     * @param Jugador  Personaje que inicia la interacción.
     */
    UFUNCTION(BlueprintNativeEvent, BlueprintCallable, Category = "Interaccion")
    void Interactuar(AAlsasuaCharacter* Jugador);
};
