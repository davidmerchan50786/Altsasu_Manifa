// AlsasuaCharacter.cpp
// ═══════════════════════════════════════════════════════════════════════════
//  Implementación del personaje jugable principal.
//  Objetivo EXACTO: Unreal Engine 5.4.
// ═══════════════════════════════════════════════════════════════════════════

#include "AlsasuaCharacter.h"

// Cabeceras concretas (IWYU: sin Unity build hay que incluirlas todas).
#include "Camera/CameraComponent.h"
#include "GameFramework/SpringArmComponent.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "GameFramework/Controller.h"
#include "GameFramework/PlayerController.h"
#include "Components/CapsuleComponent.h"
#include "Engine/LocalPlayer.h"

// Enhanced Input (UE5.4).
#include "EnhancedInputComponent.h"
#include "EnhancedInputSubsystems.h"
#include "InputMappingContext.h"
#include "InputAction.h"

// Componente de trayectoria de GASP (plugin MotionTrajectory, UE5.4). Registra
// y predice la trayectoria para PoseSearch y se actualiza por sí mismo.
#include "CharacterTrajectoryComponent.h"

// ─────────────────────────────────────────────────────────────────────────────
//  Constructor: componentes y parámetros de movimiento.
// ─────────────────────────────────────────────────────────────────────────────
AAlsasuaCharacter::AAlsasuaCharacter()
{
    // Tick para regenerar/consumir aguante y actualizar velocidad de movimiento.
    PrimaryActorTick.bCanEverTick = true;

    // ── Colisión de la cápsula ──────────────────────────────────────────────
    GetCapsuleComponent()->InitCapsuleSize(42.f, 96.f);

    // ── Rotación: el personaje gira hacia el movimiento, no hacia el controlador ──
    // (imprescindible para Motion Matching / GASP: la malla orienta con el warping).
    bUseControllerRotationPitch = false;
    bUseControllerRotationYaw   = false;
    bUseControllerRotationRoll  = false;

    // Sin mantener el salto (el ápice se notifica de inmediato).
    JumpMaxHoldTime = 0.f;

    UCharacterMovementComponent* Mov = GetCharacterMovement();
    Mov->bOrientRotationToMovement   = true;                      // gira hacia la dirección de avance
    Mov->RotationRate                = FRotator(0.f, 500.f, 0.f); // velocidad de giro
    Mov->MaxWalkSpeed                = MaxWalkSpeed;              // velocidad base al moverse
    Mov->MaxWalkSpeedCrouched        = MaxCrouchSpeed;           // velocidad agachado
    Mov->JumpZVelocity               = 500.f;
    Mov->AirControl                  = 0.35f;
    Mov->BrakingDecelerationWalking  = 2000.f;
    Mov->bNotifyApex                 = true;                      // dispara NotifyJumpApex()
    Mov->CrouchedHalfHeight          = CrouchedHalfHeight;        // altura de cápsula agachado
    // Permitir agacharse (NavAgentProps es público en UE5.4).
    Mov->NavAgentProps.bCanCrouch    = true;

    // ── Brazo de resorte (cámara AAA con lag) ───────────────────────────────
    SpringArm = CreateDefaultSubobject<USpringArmComponent>(TEXT("SpringArm"));
    SpringArm->SetupAttachment(RootComponent);
    SpringArm->TargetArmLength          = 400.f;                    // distancia de la cámara
    SpringArm->bUsePawnControlRotation   = true;                    // gira con el controlador (ratón)
    SpringArm->bEnableCameraLag          = true;                    // suavizado posicional
    SpringArm->bEnableCameraRotationLag  = true;                    // suavizado rotacional
    SpringArm->CameraLagSpeed            = 10.f;
    SpringArm->CameraRotationLagSpeed    = 15.f;
    SpringArm->SocketOffset              = FVector(0.f, 0.f, 60.f); // sube la cámara sobre el hombro
    // Pitch inicial de -30° (mirando ligeramente hacia abajo).
    SpringArm->SetRelativeRotation(FRotator(-30.f, 0.f, 0.f));

    // ── Cámara de seguimiento ───────────────────────────────────────────────
    Camera = CreateDefaultSubobject<UCameraComponent>(TEXT("FollowCamera"));
    Camera->SetupAttachment(SpringArm, USpringArmComponent::SocketName);
    Camera->bUsePawnControlRotation = false;  // la rotación la aporta el brazo de resorte

    // ── Componente de trayectoria (Motion Matching / GASP) ──────────────────
    TrajectoryComponent = CreateDefaultSubobject<UCharacterTrajectoryComponent>(TEXT("TrajectoryComponent"));
}

// ─────────────────────────────────────────────────────────────────────────────
//  BeginPlay: registrar el contexto de entrada (IMC_Jugador) y sanear atributos.
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::BeginPlay()
{
    Super::BeginPlay();

    // Sanear atributos por si el diseñador dejó valores incoherentes en el editor.
    MaxHealth      = FMath::Max(1.f, MaxHealth);
    MaxStamina     = FMath::Max(1.f, MaxStamina);
    CurrentHealth  = FMath::Clamp(CurrentHealth,  0.f, MaxHealth);
    CurrentStamina = FMath::Clamp(CurrentStamina, 0.f, MaxStamina);

    // Añadir el Input Mapping Context al subsistema de Enhanced Input del jugador local.
    if (APlayerController* PC = Cast<APlayerController>(GetController()))
    {
        if (UEnhancedInputLocalPlayerSubsystem* Subsistema =
                ULocalPlayer::GetSubsystem<UEnhancedInputLocalPlayerSubsystem>(PC->GetLocalPlayer()))
        {
            if (IMC_Jugador)
            {
                Subsistema->AddMappingContext(IMC_Jugador, MappingPriority);
            }
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Tick: gestionar aguante y velocidad de movimiento.
//  (La trayectoria de GASP se actualiza sola; NO se llama a su TickComponent.)
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::Tick(float DeltaSeconds)
{
    Super::Tick(DeltaSeconds);

    ActualizarAguante(DeltaSeconds);
    ActualizarVelocidadMovimiento();
}

// ─────────────────────────────────────────────────────────────────────────────
//  Vinculación de acciones de Enhanced Input.
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::SetupPlayerInputComponent(UInputComponent* PlayerInputComponent)
{
    Super::SetupPlayerInputComponent(PlayerInputComponent);

    // Enhanced Input usa UEnhancedInputComponent en lugar del InputComponent clásico.
    if (UEnhancedInputComponent* EIC = Cast<UEnhancedInputComponent>(PlayerInputComponent))
    {
        // Mover: se dispara mientras hay valor (Triggered).
        if (IA_Mover)
            EIC->BindAction(IA_Mover, ETriggerEvent::Triggered, this, &AAlsasuaCharacter::OnMover);

        // Mirar: cada frame que el ratón/stick aporta delta.
        if (IA_Mirar)
            EIC->BindAction(IA_Mirar, ETriggerEvent::Triggered, this, &AAlsasuaCharacter::OnMirar);

        // Saltar: Started → salta, Completed → deja de saltar.
        if (IA_Saltar)
        {
            EIC->BindAction(IA_Saltar, ETriggerEvent::Started,   this, &AAlsasuaCharacter::OnSaltar);
            EIC->BindAction(IA_Saltar, ETriggerEvent::Completed, this, &AAlsasuaCharacter::OnSaltarFin);
        }

        // Correr: Started → intención de sprint, Completed → soltar.
        if (IA_Correr)
        {
            EIC->BindAction(IA_Correr, ETriggerEvent::Started,   this, &AAlsasuaCharacter::OnCorrer);
            EIC->BindAction(IA_Correr, ETriggerEvent::Completed, this, &AAlsasuaCharacter::OnCorrerFin);
        }

        // Agacharse: Started alterna crouch/uncrouch.
        if (IA_Agacharse)
            EIC->BindAction(IA_Agacharse, ETriggerEvent::Started, this, &AAlsasuaCharacter::OnAgacharse);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  OnMover: movimiento relativo a la cámara (adelante/lateral).
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::OnMover(const FInputActionValue& Valor)
{
    const FVector2D Eje = Valor.Get<FVector2D>();   // X = derecha, Y = adelante
    if (Controller == nullptr || Eje.IsNearlyZero())
        return;

    // Usar solo el yaw del controlador para calcular las direcciones planares.
    const FRotator RotYaw(0.f, Controller->GetControlRotation().Yaw, 0.f);
    const FVector Adelante = FRotationMatrix(RotYaw).GetUnitAxis(EAxis::X);
    const FVector Derecha  = FRotationMatrix(RotYaw).GetUnitAxis(EAxis::Y);

    AddMovementInput(Adelante, Eje.Y);
    AddMovementInput(Derecha,  Eje.X);
}

// ─────────────────────────────────────────────────────────────────────────────
//  OnMirar: rotar cámara (yaw/pitch).
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::OnMirar(const FInputActionValue& Valor)
{
    const FVector2D Eje = Valor.Get<FVector2D>();   // X = yaw, Y = pitch
    if (Controller == nullptr)
        return;

    AddControllerYawInput(Eje.X);
    AddControllerPitchInput(Eje.Y);
}

// ─────────────────────────────────────────────────────────────────────────────
//  OnSaltar / OnSaltarFin.
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::OnSaltar(const FInputActionValue& /*Valor*/)
{
    Jump();
}

void AAlsasuaCharacter::OnSaltarFin(const FInputActionValue& /*Valor*/)
{
    StopJumping();
}

// ─────────────────────────────────────────────────────────────────────────────
//  OnCorrer / OnCorrerFin: marcar la INTENCIÓN de esprintar.
//  La velocidad real y el gating por aguante se resuelven en Tick.
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::OnCorrer(const FInputActionValue& /*Valor*/)
{
    bWantsToSprint = true;
}

void AAlsasuaCharacter::OnCorrerFin(const FInputActionValue& /*Valor*/)
{
    bWantsToSprint = false;
}

// ─────────────────────────────────────────────────────────────────────────────
//  OnAgacharse: alternar agacharse (Crouch gestiona la cápsula automáticamente).
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::OnAgacharse(const FInputActionValue& /*Valor*/)
{
    if (bIsCrouching)
    {
        UnCrouch();
        bIsCrouching = false;
    }
    else
    {
        Crouch();
        bIsCrouching = true;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  NotifyJumpApex: se llama en el punto más alto del salto (bNotifyApex = true).
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::NotifyJumpApex()
{
    Super::NotifyJumpApex();
    // Hook para el AnimBP/efectos: aquí se puede lanzar el blend de caída.
}

// ─────────────────────────────────────────────────────────────────────────────
//  Landed: al tocar suelo tras un salto/caída.
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::Landed(const FHitResult& Hit)
{
    Super::Landed(Hit);
    // Hook para el AnimBP/efectos: aquí se puede lanzar la anim de aterrizaje.
}

// ─────────────────────────────────────────────────────────────────────────────
//  ActualizarVelocidadMovimiento: aplica la velocidad según el estado actual.
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::ActualizarVelocidadMovimiento()
{
    UCharacterMovementComponent* Mov = GetCharacterMovement();
    if (Mov == nullptr)
        return;

    // Solo se esprinta si: hay intención, queda aguante y el personaje se mueve.
    const bool bMoviendose = Mov->Velocity.SizeSquared2D() > FMath::Square(10.f);
    bIsRunning = bWantsToSprint && CurrentStamina > 0.f && bMoviendose && !bIsCrouching;

    // La velocidad agachado la gestiona MaxWalkSpeedCrouched; solo tocamos MaxWalkSpeed.
    Mov->MaxWalkSpeedCrouched = MaxCrouchSpeed;
    Mov->MaxWalkSpeed = bIsRunning ? MaxSprintSpeed : MaxWalkSpeed;
}

// ─────────────────────────────────────────────────────────────────────────────
//  ActualizarAguante: consume aguante al esprintar, lo regenera en caso contrario.
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::ActualizarAguante(float DeltaSeconds)
{
    if (bIsRunning)
    {
        CurrentStamina = FMath::Max(0.f, CurrentStamina - StaminaDrainRate * DeltaSeconds);
        // Si se agota, cancelar la intención para forzar recuperación.
        if (CurrentStamina <= 0.f)
            bWantsToSprint = false;
    }
    else
    {
        CurrentStamina = FMath::Min(MaxStamina, CurrentStamina + StaminaRegenRate * DeltaSeconds);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  AimOffset: hooks para el blendspace de apuntado del AnimBP.
// ─────────────────────────────────────────────────────────────────────────────
float AAlsasuaCharacter::GetAimOffsetYaw() const
{
    // Diferencia entre hacia dónde mira la cámara/control y hacia dónde está el cuerpo.
    const FRotator Delta = (GetBaseAimRotation() - GetActorRotation()).GetNormalized();
    return Delta.Yaw;   // [-180, 180]
}

float AAlsasuaCharacter::GetAimOffsetPitch() const
{
    const FRotator Aim = GetBaseAimRotation().GetNormalized();
    return FMath::ClampAngle(Aim.Pitch, -90.f, 90.f);
}
