// AlsasuaAnimInstance.cpp
// ═══════════════════════════════════════════════════════════════════════════
//  Implementación del AnimInstance del personaje (UE 5.4).
// ═══════════════════════════════════════════════════════════════════════════

#include "AlsasuaAnimInstance.h"
#include "AlsasuaCharacter.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "CharacterTrajectoryComponent.h"

void UAlsasuaAnimInstance::NativeInitializeAnimation()
{
    Super::NativeInitializeAnimation();

    // Cachear el personaje propietario una sola vez.
    OwnerCharacter = Cast<AAlsasuaCharacter>(TryGetPawnOwner());
    if (OwnerCharacter)
    {
        // Guardar la referencia a la trayectoria para el nodo Motion Matching.
        Trajectory = OwnerCharacter->GetCharacterTrajectory();
    }
}

void UAlsasuaAnimInstance::NativeUpdateAnimation(float DeltaSeconds)
{
    Super::NativeUpdateAnimation(DeltaSeconds);

    // Si aún no está cacheado (p. ej. tras un respawn), intentar de nuevo.
    if (OwnerCharacter == nullptr)
    {
        OwnerCharacter = Cast<AAlsasuaCharacter>(TryGetPawnOwner());
        if (OwnerCharacter)
            Trajectory = OwnerCharacter->GetCharacterTrajectory();
        return;
    }

    // Leer las variables ya calculadas por el personaje (con comprobaciones nulas).
    Speed2D        = OwnerCharacter->GetSpeed2D();
    Direction      = OwnerCharacter->GetMovementDirection();
    Gait           = OwnerCharacter->GetMovementGait();
    bIsRunning     = OwnerCharacter->IsRunning();
    bIsCrouching   = OwnerCharacter->IsCrouchingState();
    bCanVault      = OwnerCharacter->CanVault();
    AimOffsetYaw   = OwnerCharacter->GetAimOffsetYaw();
    AimOffsetPitch = OwnerCharacter->GetAimOffsetPitch();

    // Estado aéreo directamente del CharacterMovement.
    if (const UCharacterMovementComponent* Mov = OwnerCharacter->GetCharacterMovement())
    {
        bIsInAir = Mov->IsFalling();
    }
}
