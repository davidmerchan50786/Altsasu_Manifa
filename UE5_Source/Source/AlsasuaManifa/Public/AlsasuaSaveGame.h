// AlsasuaSaveGame.h
// ═══════════════════════════════════════════════════════════════════════════
//  Objeto de guardado de partida (UE 5.4). Persiste el estado del personaje
//  (salud, aguante, posición y rotación) en una ranura de disco.
// ═══════════════════════════════════════════════════════════════════════════

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/SaveGame.h"
#include "AlsasuaSaveGame.generated.h"

class AAlsasuaCharacter;

/**
 * Datos serializados de la partida. Las propiedades marcadas con SaveGame se
 * escriben/leen automáticamente por el sistema de guardado de Unreal.
 */
UCLASS()
class ALSASUAMANIFA_API UAlsasuaSaveGame : public USaveGame
{
    GENERATED_BODY()

public:
    /** Nombre de la ranura de guardado por defecto. */
    UPROPERTY(BlueprintReadWrite, Category = "Guardado")
    FString SlotName = TEXT("AlsasuaSave1");

    /** Índice de usuario (0 en un juego para un solo jugador). */
    UPROPERTY(BlueprintReadWrite, Category = "Guardado")
    int32 UserIndex = 0;

    /** Salud guardada. */
    UPROPERTY(SaveGame, BlueprintReadWrite, Category = "Guardado")
    float SavedHealth = 100.f;

    /** Aguante guardado. */
    UPROPERTY(SaveGame, BlueprintReadWrite, Category = "Guardado")
    float SavedStamina = 100.f;

    /** Posición guardada en el mundo. */
    UPROPERTY(SaveGame, BlueprintReadWrite, Category = "Guardado")
    FVector SavedLocation = FVector::ZeroVector;

    /** Rotación guardada. */
    UPROPERTY(SaveGame, BlueprintReadWrite, Category = "Guardado")
    FRotator SavedRotation = FRotator::ZeroRotator;

    // ── Ayudantes estáticos ────────────────────────────────────────────────

    /**
     * Guarda el estado actual del personaje en la ranura indicada.
     * @return true si el guardado en disco tuvo éxito.
     */
    static bool GuardarPersonaje(AAlsasuaCharacter* Personaje, const FString& Slot = TEXT("AlsasuaSave1"), int32 Usuario = 0);

    /**
     * Carga el estado desde la ranura indicada y lo aplica al personaje.
     * @return true si existía una partida y se aplicó correctamente.
     */
    static bool CargarPersonaje(AAlsasuaCharacter* Personaje, const FString& Slot = TEXT("AlsasuaSave1"), int32 Usuario = 0);
};
