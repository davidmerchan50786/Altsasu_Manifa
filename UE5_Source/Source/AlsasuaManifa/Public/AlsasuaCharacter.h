// AlsasuaCharacter.h
// ═══════════════════════════════════════════════════════════════════════════
//  Personaje jugable principal de AlsasuaManifa.
//  Objetivo EXACTO: Unreal Engine 5.4.
//
//  Integra:
//    · Enhanced Input (UE5.4) : IMC_Jugador + IA_Mover/Mirar/Saltar/Correr/Agacharse
//    · Motion Matching / GASP : UCharacterTrajectoryComponent alimenta PoseSearch
//    · Cámara AAA             : SpringArm con lag + Camera (tercera persona)
//    · Esqueleto              : SK_Mannequin (mannequin de Epic / GASP)
//    · Hooks de AimOffset     : GetAimOffsetYaw/Pitch para el AnimBP
//    · Stubs listos para GAS  : Salud (Health) y Aguante (Stamina)
// ═══════════════════════════════════════════════════════════════════════════

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Character.h"
#include "InputActionValue.h"
#include "AlsasuaCharacter.generated.h"

// Declaraciones adelantadas (evitan incluir cabeceras pesadas aquí).
class USpringArmComponent;
class UCameraComponent;
class UCharacterTrajectoryComponent;   // Motion Matching / GASP (MotionTrajectory, UE5.4)
class UInputMappingContext;
class UInputAction;
struct FInputActionValue;

/**
 * Personaje jugable principal. Diseñado para locomoción con Motion Matching
 * (GASP): la malla se orienta al movimiento y el AnimBP consulta la trayectoria
 * y los hooks de AimOffset expuestos aquí.
 */
UCLASS()
class ALSASUAMANIFA_API AAlsasuaCharacter : public ACharacter
{
    GENERATED_BODY()

public:
    // Constructor: crea y configura los componentes (cámara, spring arm, trayectoria).
    AAlsasuaCharacter();

protected:
    // ── Ciclo de vida ─────────────────────────────────────────────────────────
    virtual void BeginPlay() override;
    virtual void Tick(float DeltaSeconds) override;
    virtual void SetupPlayerInputComponent(class UInputComponent* PlayerInputComponent) override;

    // Detección del ápice del salto (útil para blends de anim / partículas).
    virtual void NotifyJumpApex() override;
    // Aterrizaje: reinicia estado de salto.
    virtual void Landed(const FHitResult& Hit) override;

    // ── Componentes de cámara (cámara AAA en tercera persona) ──────────────────

    /** Brazo de resorte: sostiene la cámara con retardo (lag) para un movimiento suave. */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Camara", meta = (AllowPrivateAccess = "true"))
    TObjectPtr<USpringArmComponent> SpringArm;

    /** Cámara de seguimiento en el extremo del brazo de resorte. */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Camara", meta = (AllowPrivateAccess = "true"))
    TObjectPtr<UCameraComponent> Camera;

    // ── Motion Matching / GASP ─────────────────────────────────────────────────

    /**
     * Componente de trayectoria de GASP (MotionTrajectory, UE5.4). Registra la
     * trayectoria pasada y predice la futura a partir del CharacterMovement; el
     * AnimBP la usa como consulta para PoseSearch. Se actualiza solo (tick propio).
     */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "MotionMatching", meta = (AllowPrivateAccess = "true"))
    TObjectPtr<UCharacterTrajectoryComponent> TrajectoryComponent;

    // ── Enhanced Input: contexto y acciones ────────────────────────────────────

    /** Contexto de mapeo de entrada del jugador (IMC_Jugador). */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Input", meta = (AllowPrivateAccess = "true"))
    TObjectPtr<UInputMappingContext> IMC_Jugador;

    /** Prioridad con la que se añade el IMC (mayor = tiene preferencia). */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Input", meta = (AllowPrivateAccess = "true"))
    int32 MappingPriority = 0;

    /** Movimiento planar (Vector2D: X = derecha, Y = adelante). */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Input", meta = (AllowPrivateAccess = "true"))
    TObjectPtr<UInputAction> IA_Mover;

    /** Mirar / rotar cámara (Vector2D: X = yaw, Y = pitch). */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Input", meta = (AllowPrivateAccess = "true"))
    TObjectPtr<UInputAction> IA_Mirar;

    /** Saltar (bool: Started → Jump, Completed → StopJumping). */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Input", meta = (AllowPrivateAccess = "true"))
    TObjectPtr<UInputAction> IA_Saltar;

    /** Correr / sprint (bool: mantener para correr). */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Input", meta = (AllowPrivateAccess = "true"))
    TObjectPtr<UInputAction> IA_Correr;

    /** Agacharse (bool: alterna crouch/uncrouch). */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Input", meta = (AllowPrivateAccess = "true"))
    TObjectPtr<UInputAction> IA_Agacharse;

    // ── Manejadores de entrada (Enhanced Input) ────────────────────────────────
    void OnMover(const FInputActionValue& Valor);
    void OnMirar(const FInputActionValue& Valor);
    void OnSaltar(const FInputActionValue& Valor);
    void OnSaltarFin(const FInputActionValue& Valor);
    void OnCorrer(const FInputActionValue& Valor);
    void OnCorrerFin(const FInputActionValue& Valor);
    void OnAgacharse(const FInputActionValue& Valor);

    // ── Velocidades de movimiento (cm/s) — configurables en el editor ──────────

    /** Velocidad base al andar/trotar (estado por defecto al moverse). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movimiento", meta = (AllowPrivateAccess = "true", ClampMin = "0.0", UIMin = "0.0"))
    float MaxWalkSpeed = 300.f;

    /** Velocidad máxima al esprintar (correr mantenido). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movimiento", meta = (AllowPrivateAccess = "true", ClampMin = "0.0", UIMin = "0.0"))
    float MaxSprintSpeed = 600.f;

    /** Velocidad máxima agachado. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movimiento", meta = (AllowPrivateAccess = "true", ClampMin = "0.0", UIMin = "0.0"))
    float MaxCrouchSpeed = 150.f;

    /** Altura de la cápsula al agacharse (mitad de la altura). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movimiento", meta = (AllowPrivateAccess = "true", ClampMin = "20.0", UIMin = "20.0"))
    float CrouchedHalfHeight = 60.f;

    // ── Atributos listos para GAS (por ahora, stubs sin AbilitySystem) ─────────
    //  Al integrar GameplayAbilities, migrar estos a un UAttributeSet.

    /** Salud actual del personaje. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Atributos|Salud", meta = (AllowPrivateAccess = "true", ClampMin = "0.0"))
    float CurrentHealth = 100.f;

    /** Salud máxima. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Atributos|Salud", meta = (AllowPrivateAccess = "true", ClampMin = "1.0"))
    float MaxHealth = 100.f;

    /** Aguante actual (se consume al esprintar). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Atributos|Aguante", meta = (AllowPrivateAccess = "true", ClampMin = "0.0"))
    float CurrentStamina = 100.f;

    /** Aguante máximo. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Atributos|Aguante", meta = (AllowPrivateAccess = "true", ClampMin = "1.0"))
    float MaxStamina = 100.f;

    /** Aguante consumido por segundo mientras se esprinta. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Atributos|Aguante", meta = (AllowPrivateAccess = "true", ClampMin = "0.0"))
    float StaminaDrainRate = 20.f;

    /** Aguante regenerado por segundo cuando no se esprinta. */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Atributos|Aguante", meta = (AllowPrivateAccess = "true", ClampMin = "0.0"))
    float StaminaRegenRate = 15.f;

    // ── Estado (expuesto al AnimBP como BlueprintReadOnly) ─────────────────────

    /** true mientras el personaje está esprintando de verdad (intención + aguante). */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Estado", meta = (AllowPrivateAccess = "true"))
    bool bIsRunning = false;

    /** true mientras el personaje está agachado. */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Estado", meta = (AllowPrivateAccess = "true"))
    bool bIsCrouching = false;

    /** true mientras el jugador MANTIENE la tecla de correr (intención de sprint). */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Estado", meta = (AllowPrivateAccess = "true"))
    bool bWantsToSprint = false;

private:
    // Aplica la velocidad de movimiento según el estado (sprint/andar/agachado).
    void ActualizarVelocidadMovimiento();
    // Consume/regenera aguante y corta el sprint si se agota.
    void ActualizarAguante(float DeltaSeconds);

public:
    // ── Accesores para el AnimBP (Motion Matching / AimOffset) ─────────────────

    FORCEINLINE USpringArmComponent* GetSpringArm() const { return SpringArm; }
    FORCEINLINE UCameraComponent* GetFollowCamera() const { return Camera; }

    /** Acceso a la trayectoria de GASP para el AnimBP. */
    UFUNCTION(BlueprintCallable, BlueprintPure, Category = "MotionMatching")
    UCharacterTrajectoryComponent* GetCharacterTrajectory() const { return TrajectoryComponent; }

    /**
     * Yaw del AimOffset: diferencia normalizada [-180,180] entre la rotación de
     * control (cámara) y la del actor. El AnimBP lo usa para el blendspace de aim.
     */
    UFUNCTION(BlueprintCallable, BlueprintPure, Category = "AimOffset")
    float GetAimOffsetYaw() const;

    /** Pitch del AimOffset: pitch normalizado [-90,90] de la rotación de control. */
    UFUNCTION(BlueprintCallable, BlueprintPure, Category = "AimOffset")
    float GetAimOffsetPitch() const;

    UFUNCTION(BlueprintPure, Category = "Estado")
    FORCEINLINE bool IsRunning() const { return bIsRunning; }

    UFUNCTION(BlueprintPure, Category = "Estado")
    FORCEINLINE bool IsCrouchingState() const { return bIsCrouching; }

    UFUNCTION(BlueprintPure, Category = "Atributos|Salud")
    FORCEINLINE float GetCurrentHealth() const { return CurrentHealth; }

    UFUNCTION(BlueprintPure, Category = "Atributos|Aguante")
    FORCEINLINE float GetCurrentStamina() const { return CurrentStamina; }

    /** Normalizado 0–1 para barras de HUD. */
    UFUNCTION(BlueprintPure, Category = "Atributos|Aguante")
    float GetStaminaNormalized() const { return MaxStamina > 0.f ? CurrentStamina / MaxStamina : 0.f; }
};
