// AlsasuaCharacter.h
// ═══════════════════════════════════════════════════════════════════════════
//  Personaje jugable principal de AlsasuaManifa (puerto UE5 del proyecto Unity).
//
//  Integra:
//    · Enhanced Input (UE5.4+): IMC_Jugador + IA_Mover/Mirar/Saltar/Correr/Agacharse
//    · Motion Matching / GASP : UMotionTrajectoryComponent alimenta PoseSearch
//    · Cámara AAA             : SpringArm con lag + Camera (tercera persona)
//    · Esqueleto              : SK_Mannequin (metahuman/mannequin de Epic)
// ═══════════════════════════════════════════════════════════════════════════

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Character.h"
#include "InputActionValue.h"
#include "AlsasuaCharacter.generated.h"

// Declaraciones adelantadas (evitan incluir cabeceras pesadas aquí).
class USpringArmComponent;
class UCameraComponent;
class UMotionTrajectoryComponent;
class UInputMappingContext;
class UInputAction;
struct FInputActionValue;

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

    // ── Componentes de cámara (cámara AAA en tercera persona) ──────────────────

    /** Brazo de resorte: sostiene la cámara con retardo (lag) para un movimiento suave. */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Camara", meta = (AllowPrivateAccess = "true"))
    TObjectPtr<USpringArmComponent> SpringArm;

    /** Cámara de seguimiento en el extremo del brazo de resorte. */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Camara", meta = (AllowPrivateAccess = "true"))
    TObjectPtr<UCameraComponent> Camara;

    // ── Motion Matching / GASP ─────────────────────────────────────────────────

    /** Componente de trayectoria: predice el futuro/pasado del movimiento para PoseSearch. */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "MotionMatching", meta = (AllowPrivateAccess = "true"))
    TObjectPtr<UMotionTrajectoryComponent> Trayectoria;

    // ── Enhanced Input: contexto y acciones ────────────────────────────────────

    /** Contexto de mapeo de entrada del jugador (IMC_Jugador). */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Input", meta = (AllowPrivateAccess = "true"))
    TObjectPtr<UInputMappingContext> IMC_Jugador;

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

    // ── Constantes de velocidad (cm/s) ─────────────────────────────────────────

    /** Velocidad al andar (agachado o caminando normal). */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Movimiento", meta = (AllowPrivateAccess = "true"))
    float VelocidadAndar = 300.f;

    /** Velocidad al correr (por defecto). */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Movimiento", meta = (AllowPrivateAccess = "true"))
    float VelocidadCorrer = 500.f;

    /** Velocidad al esprintar (correr mantenido). */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Movimiento", meta = (AllowPrivateAccess = "true"))
    float VelocidadSprint = 600.f;

    // ── Estado ─────────────────────────────────────────────────────────────────

    /** true mientras el personaje está agachado. */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Estado", meta = (AllowPrivateAccess = "true"))
    bool bEstaAgachado = false;

    /** true mientras el personaje está corriendo/esprintando. */
    UPROPERTY(VisibleAnywhere, BlueprintReadOnly, Category = "Estado", meta = (AllowPrivateAccess = "true"))
    bool bEstaCorriendo = false;

public:
    // Accesores (útiles para el AnimBP de Motion Matching).
    FORCEINLINE USpringArmComponent* ObtenerSpringArm() const { return SpringArm; }
    FORCEINLINE UCameraComponent* ObtenerCamara() const { return Camara; }
    FORCEINLINE UMotionTrajectoryComponent* ObtenerTrayectoria() const { return Trayectoria; }
    FORCEINLINE bool EstaAgachado() const { return bEstaAgachado; }
    FORCEINLINE bool EstaCorriendo() const { return bEstaCorriendo; }
};
