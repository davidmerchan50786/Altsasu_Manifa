// AlsasuaAnimInstance.h
// ═══════════════════════════════════════════════════════════════════════════
//  AnimInstance base en C++ para el AnimBP del personaje (UE 5.4).
//  Expone al AnimBP (como BlueprintReadOnly) las variables de locomoción que
//  necesita el grafo de Motion Matching (GASP): velocidad, dirección, estados
//  y el componente de trayectoria para el nodo Motion Matching de PoseSearch.
// ═══════════════════════════════════════════════════════════════════════════

#pragma once

#include "CoreMinimal.h"
#include "Animation/AnimInstance.h"
#include "AlsasuaCharacter.h"          // para EMovementGait y el tipo del owner
#include "AlsasuaAnimInstance.generated.h"

class AAlsasuaCharacter;
class UCharacterTrajectoryComponent;

/**
 * AnimInstance del personaje. Deriva el AnimBP de esta clase para leer las
 * variables ya calculadas en C++ (más barato y determinista que hacerlo en el grafo).
 */
UCLASS()
class ALSASUAMANIFA_API UAlsasuaAnimInstance : public UAnimInstance
{
    GENERATED_BODY()

public:
    // Se llama una vez al inicializar la instancia de animación.
    virtual void NativeInitializeAnimation() override;
    // Se llama cada frame antes de evaluar el grafo de animación.
    virtual void NativeUpdateAnimation(float DeltaSeconds) override;

protected:
    /** Personaje propietario (cacheado en NativeInitializeAnimation). */
    UPROPERTY(BlueprintReadOnly, Category = "Referencias")
    TObjectPtr<AAlsasuaCharacter> OwnerCharacter;

    // ── Locomoción (para blends y Motion Matching) ─────────────────────────
    /** Velocidad planar (cm/s). */
    UPROPERTY(BlueprintReadOnly, Category = "Locomocion")
    float Speed2D = 0.f;

    /** Dirección del movimiento respecto al forward del actor [-180,180]. */
    UPROPERTY(BlueprintReadOnly, Category = "Locomocion")
    float Direction = 0.f;

    /** Marcha actual (parado/andar/correr/sprint). */
    UPROPERTY(BlueprintReadOnly, Category = "Locomocion")
    EMovementGait Gait = EMovementGait::Idle;

    /** true si el personaje está en el aire (salto/caída). */
    UPROPERTY(BlueprintReadOnly, Category = "Locomocion")
    bool bIsInAir = false;

    /** true si el personaje está agachado. */
    UPROPERTY(BlueprintReadOnly, Category = "Locomocion")
    bool bIsCrouching = false;

    /** true si el personaje está esprintando. */
    UPROPERTY(BlueprintReadOnly, Category = "Locomocion")
    bool bIsRunning = false;

    /** true si hay un saliente detectado para saltar/trepar (vault). */
    UPROPERTY(BlueprintReadOnly, Category = "Locomocion")
    bool bCanVault = false;

    // ── AimOffset ───────────────────────────────────────────────────────────
    UPROPERTY(BlueprintReadOnly, Category = "AimOffset")
    float AimOffsetYaw = 0.f;

    UPROPERTY(BlueprintReadOnly, Category = "AimOffset")
    float AimOffsetPitch = 0.f;

    // ── Motion Matching ───────────────────────────────────────────────────
    /**
     * Componente de trayectoria de GASP. Conéctalo en el AnimBP al pin
     * "Trajectory" del nodo Motion Matching de PoseSearch.
     */
    UPROPERTY(BlueprintReadOnly, Category = "MotionMatching")
    TObjectPtr<UCharacterTrajectoryComponent> Trajectory;
};
