// AlsasuaCharacter.h
// ═══════════════════════════════════════════════════════════════════════════
//  Personaje jugable principal de AlsasuaManifa.
//  Objetivo EXACTO: Unreal Engine 5.4.
//
//  Integra:
//    · Enhanced Input (UE5.4) : IMC_Jugador + IA_Mover/Mirar/Saltar/Correr/
//                               Agacharse/Interactuar
//    · Motion Matching / GASP : UCharacterTrajectoryComponent alimenta PoseSearch
//    · Cámara AAA             : SpringArm con lag + Camera (tercera persona)
//    · Esqueleto              : SK_Mannequin (mannequin de Epic / GASP)
//    · Hooks de AimOffset     : GetAimOffsetYaw/Pitch para el AnimBP
//    · Variables ALS/GASP     : Speed2D, Direction, Gait, bIsInAir para el AnimBP
//    · Sistema de interacción : line trace + interfaz IInteractuable
//    · Traversal / vault      : sphere trace de detección de salientes
//    · Foot IK y pisadas      : trazas de pie + sonido de pisada
//    · Delegados de atributos : OnHealthChanged / OnStaminaChanged (para HUD)
//    · Guardado de partida    : GuardarPartida / CargarPartida (USaveGame)
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
class USoundBase;
struct FInputActionValue;

/**
 * Marcha de locomoción del personaje (para blends y Motion Matching del AnimBP).
 */
UENUM(BlueprintType)
enum class EMovementGait : uint8
{
    Idle    UMETA(DisplayName = "Parado"),
    Walk    UMETA(DisplayName = "Andar"),
    Run     UMETA(DisplayName = "Correr"),
    Sprint  UMETA(DisplayName = "Esprintar")
};

// Delegados de atributos (para que el HUD/UMG reaccione a los cambios).
DECLARE_DYNAMIC_MULTICAST_DELEGATE_TwoParams(FOnHealthChanged,  float, Current, float, Max);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_TwoParams(FOnStaminaChanged, float, Current, float, Max);

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

    // ── Delegados de atributos (BlueprintAssignable para enlazar el HUD) ────
    /** Se emite al cambiar la salud (Current, Max). */
    UPROPERTY(BlueprintAssignable, Category = "Atributos|Salud")
    FOnHealthChanged OnHealthChanged;

    /** Se emite al cambiar el aguante (Current, Max). */
    UPROPERTY(BlueprintAssignable, Category = "Atributos|Aguante")
    FOnStaminaChanged OnStaminaChanged;

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

    /** Interactuar (bool: Started → line trace + IInteractuable). */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Input", meta = (AllowPrivateAccess = "true"))
    TObjectPtr<UInputAction> IA_Interactuar;

    // ── Manejadores de entrada (Enhanced Input) ────────────────────────────────
    void OnMover(const FInputActionValue& Valor);
    void OnMirar(const FInputActionValue& Valor);
    void OnSaltar(const FInputActionValue& Valor);
    void OnSaltarFin(const FInputActionValue& Valor);
    void OnCorrer(const FInputActionValue& Valor);
    void OnCorrerFin(const FInputActionValue& Valor);
    void OnAgacharse(const FInputActionValue& Valor);
    void OnInteractuar(const FInputActionValue& Valor);

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

    /** Umbral de velocidad para pasar de "Andar" a "Correr" (para EMovementGait). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Movimiento", meta = (AllowPrivateAccess = "true", ClampMin = "0.0"))
    float UmbralCorrer = 350.f;

    // ── Interacción / Traversal / Foot IK ──────────────────────────────────────

    /** Alcance del line trace de interacción (cm). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Interaccion", meta = (AllowPrivateAccess = "true", ClampMin = "0.0"))
    float AlcanceInteraccion = 200.f;

    /** Distancia hacia delante para detectar un saliente que trepar (cm). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Traversal", meta = (AllowPrivateAccess = "true", ClampMin = "0.0"))
    float DistanciaDeteccionSaliente = 80.f;

    /** Radio del sphere trace de detección de salientes (cm). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Traversal", meta = (AllowPrivateAccess = "true", ClampMin = "1.0"))
    float RadioDeteccionSaliente = 25.f;

    /** Distancia de la traza de Foot IK hacia abajo desde el pie (cm). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "FootIK", meta = (AllowPrivateAccess = "true", ClampMin = "0.0"))
    float DistanciaTrazaFootIK = 60.f;

    /** Nombre del socket del pie izquierdo en la malla (esqueleto Mannequin). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "FootIK", meta = (AllowPrivateAccess = "true"))
    FName SocketPieIzquierdo = TEXT("foot_l");

    /** Nombre del socket del pie derecho en la malla (esqueleto Mannequin). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "FootIK", meta = (AllowPrivateAccess = "true"))
    FName SocketPieDerecho = TEXT("foot_r");

    /** Sonido de pisada (se reproduce en PlayFootstepSound, típicamente desde un AnimNotify). */
    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Sonido", meta = (AllowPrivateAccess = "true"))
    TObjectPtr<USoundBase> FootstepSound;

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

    // ── Estado / variables ALS-GASP (expuesto al AnimBP como BlueprintReadOnly) ─

    /** Velocidad planar actual (cm/s) — para blends de locomoción. */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Estado", meta = (AllowPrivateAccess = "true"))
    float Speed2D = 0.f;

    /** Dirección del movimiento respecto al forward del actor [-180,180]. */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Estado", meta = (AllowPrivateAccess = "true"))
    float Direction = 0.f;

    /** Marcha de locomoción actual (parado/andar/correr/sprint). */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Estado", meta = (AllowPrivateAccess = "true"))
    EMovementGait Gait = EMovementGait::Idle;

    /** true si el personaje está en el aire (salto/caída). */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Estado", meta = (AllowPrivateAccess = "true"))
    bool bIsInAir = false;

    /** true mientras el personaje está esprintando de verdad (intención + aguante). */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Estado", meta = (AllowPrivateAccess = "true"))
    bool bIsRunning = false;

    /** true mientras el personaje está agachado. */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Estado", meta = (AllowPrivateAccess = "true"))
    bool bIsCrouching = false;

    /** true mientras el jugador MANTIENE la tecla de correr (intención de sprint). */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Estado", meta = (AllowPrivateAccess = "true"))
    bool bWantsToSprint = false;

    /** true si hay un saliente delante apto para saltar/trepar (vault). */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Estado", meta = (AllowPrivateAccess = "true"))
    bool bCanVault = false;

private:
    // Aplica la velocidad de movimiento según el estado (sprint/andar/agachado).
    void ActualizarVelocidadMovimiento();
    // Consume/regenera aguante y corta el sprint si se agota.
    void ActualizarAguante(float DeltaSeconds);
    // Actualiza Speed2D, Direction, Gait y bIsInAir para el AnimBP.
    void ActualizarVariablesAnimacion();
    // Sphere trace hacia delante+arriba para detectar salientes (actualiza bCanVault).
    void CheckTraversal();

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

    // ── Accesores de estado / ALS (para el AnimInstance) ───────────────────────

    UFUNCTION(BlueprintPure, Category = "Estado")
    FORCEINLINE float GetSpeed2D() const { return Speed2D; }

    UFUNCTION(BlueprintPure, Category = "Estado")
    FORCEINLINE float GetMovementDirection() const { return Direction; }

    UFUNCTION(BlueprintPure, Category = "Estado")
    FORCEINLINE EMovementGait GetMovementGait() const { return Gait; }

    UFUNCTION(BlueprintPure, Category = "Estado")
    FORCEINLINE bool IsInAir() const { return bIsInAir; }

    UFUNCTION(BlueprintPure, Category = "Estado")
    FORCEINLINE bool IsRunning() const { return bIsRunning; }

    UFUNCTION(BlueprintPure, Category = "Estado")
    FORCEINLINE bool IsCrouchingState() const { return bIsCrouching; }

    UFUNCTION(BlueprintPure, Category = "Estado")
    FORCEINLINE bool CanVault() const { return bCanVault; }

    // ── Atributos: getters y setters (setters emiten los delegados) ────────────

    UFUNCTION(BlueprintPure, Category = "Atributos|Salud")
    FORCEINLINE float GetCurrentHealth() const { return CurrentHealth; }

    UFUNCTION(BlueprintPure, Category = "Atributos|Salud")
    FORCEINLINE float GetMaxHealth() const { return MaxHealth; }

    /** Fija la salud (clamp 0..Max) y emite OnHealthChanged. */
    UFUNCTION(BlueprintCallable, Category = "Atributos|Salud")
    void SetCurrentHealth(float NuevaSalud);

    UFUNCTION(BlueprintPure, Category = "Atributos|Aguante")
    FORCEINLINE float GetCurrentStamina() const { return CurrentStamina; }

    UFUNCTION(BlueprintPure, Category = "Atributos|Aguante")
    FORCEINLINE float GetMaxStamina() const { return MaxStamina; }

    /** Fija el aguante (clamp 0..Max) y emite OnStaminaChanged. */
    UFUNCTION(BlueprintCallable, Category = "Atributos|Aguante")
    void SetCurrentStamina(float NuevoAguante);

    /** Normalizado 0–1 para barras de HUD. */
    UFUNCTION(BlueprintPure, Category = "Atributos|Aguante")
    float GetStaminaNormalized() const { return MaxStamina > 0.f ? CurrentStamina / MaxStamina : 0.f; }

    // ── Foot IK y sonido de pisadas ────────────────────────────────────────────

    /**
     * Devuelve la posición de suelo bajo el pie indicado (line trace hacia abajo).
     * Útil para nodos de Foot IK (Two Bone IK) en el AnimBP.
     * @param bLeftFoot  true = pie izquierdo, false = pie derecho.
     */
    UFUNCTION(BlueprintCallable, Category = "FootIK")
    FVector GetFootIKLocation(bool bLeftFoot) const;

    /** Reproduce el sonido de pisada en la posición del actor (desde un AnimNotify). */
    UFUNCTION(BlueprintCallable, Category = "Sonido")
    void PlayFootstepSound();

    // ── Guardado de partida ────────────────────────────────────────────────────

    /** Guarda el estado del personaje en la ranura por defecto. */
    UFUNCTION(BlueprintCallable, Category = "Guardado")
    bool GuardarPartida();

    /** Carga el estado del personaje desde la ranura por defecto. */
    UFUNCTION(BlueprintCallable, Category = "Guardado")
    bool CargarPartida();
};
