// AlsasuaCharacter.cpp
// ═══════════════════════════════════════════════════════════════════════════
//  Implementación del personaje jugable principal.
// ═══════════════════════════════════════════════════════════════════════════

#include "AlsasuaCharacter.h"

#include "Camera/CameraComponent.h"
#include "GameFramework/SpringArmComponent.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "GameFramework/Controller.h"
#include "Components/CapsuleComponent.h"

#include "EnhancedInputComponent.h"
#include "EnhancedInputSubsystems.h"
#include "InputMappingContext.h"
#include "InputAction.h"

#include "MotionTrajectoryComponent.h"

// ─────────────────────────────────────────────────────────────────────────────
//  Constructor: componentes y parámetros de movimiento.
// ─────────────────────────────────────────────────────────────────────────────
AAlsasuaCharacter::AAlsasuaCharacter()
{
    // Necesitamos Tick para actualizar la trayectoria de Motion Matching cada frame.
    PrimaryActorTick.bCanEverTick = true;

    // ── Colisión de la cápsula ──────────────────────────────────────────────
    GetCapsuleComponent()->InitCapsuleSize(42.f, 96.f);

    // ── Rotación: el personaje gira hacia el movimiento, no hacia el controlador ──
    // (imprescindible para Motion Matching / GASP: la malla orienta con el warping).
    bUseControllerRotationPitch = false;
    bUseControllerRotationYaw   = false;
    bUseControllerRotationRoll  = false;

    UCharacterMovementComponent* Mov = GetCharacterMovement();
    Mov->bOrientRotationToMovement = true;                       // gira hacia la dirección de avance
    Mov->RotationRate              = FRotator(0.f, 500.f, 0.f);  // velocidad de giro
    Mov->MaxWalkSpeed              = VelocidadCorrer;            // velocidad base al correr
    Mov->MaxWalkSpeedCrouched      = VelocidadAndar;            // velocidad agachado
    Mov->JumpZVelocity             = 500.f;
    Mov->AirControl                = 0.35f;
    Mov->BrakingDecelerationWalking = 2000.f;
    Mov->NavAgentProps.bCanCrouch  = true;                      // permitir agacharse

    // ── Brazo de resorte (cámara AAA con lag) ───────────────────────────────
    SpringArm = CreateDefaultSubobject<USpringArmComponent>(TEXT("SpringArm"));
    SpringArm->SetupAttachment(RootComponent);
    SpringArm->TargetArmLength         = 400.f;                     // distancia de la cámara
    SpringArm->bUsePawnControlRotation = true;                      // gira con el controlador (ratón)
    SpringArm->bEnableCameraLag        = true;                      // suavizado posicional
    SpringArm->bEnableCameraRotationLag = true;                     // suavizado rotacional
    SpringArm->CameraLagSpeed          = 10.f;
    SpringArm->CameraRotationLagSpeed  = 10.f;
    SpringArm->SocketOffset            = FVector(0.f, 0.f, 60.f);   // sube la cámara sobre el hombro
    // Pitch inicial de -30° (mirando ligeramente hacia abajo).
    SpringArm->SetRelativeRotation(FRotator(-30.f, 0.f, 0.f));

    // ── Cámara de seguimiento ───────────────────────────────────────────────
    Camara = CreateDefaultSubobject<UCameraComponent>(TEXT("Camara"));
    Camara->SetupAttachment(SpringArm, USpringArmComponent::SocketName);
    Camara->bUsePawnControlRotation = false;  // la rotación la aporta el brazo de resorte

    // ── Componente de trayectoria (Motion Matching / GASP) ──────────────────
    Trayectoria = CreateDefaultSubobject<UMotionTrajectoryComponent>(TEXT("Trayectoria"));
}

// ─────────────────────────────────────────────────────────────────────────────
//  BeginPlay: registrar el contexto de entrada (IMC_Jugador).
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::BeginPlay()
{
    Super::BeginPlay();

    // Añadir el Input Mapping Context al subsistema de Enhanced Input del jugador local.
    if (APlayerController* PC = Cast<APlayerController>(GetController()))
    {
        if (UEnhancedInputLocalPlayerSubsystem* Subsistema =
                ULocalPlayer::GetSubsystem<UEnhancedInputLocalPlayerSubsystem>(PC->GetLocalPlayer()))
        {
            if (IMC_Jugador)
            {
                Subsistema->AddMappingContext(IMC_Jugador, /*Prioridad=*/0);
            }
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Tick: actualizar la trayectoria que consume PoseSearch.
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::Tick(float DeltaSeconds)
{
    Super::Tick(DeltaSeconds);

    // Mantener la muestra de trayectoria al día para el motion matching de este frame.
    if (Trayectoria)
    {
        Trayectoria->TickComponent(DeltaSeconds, ELevelTick::LEVELTICK_All, nullptr);
    }
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

        // Correr: Started → activa sprint, Completed → vuelve a velocidad normal.
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
//  OnCorrer / OnCorrerFin: alternar velocidad de carrera (sprint ↔ normal).
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::OnCorrer(const FInputActionValue& /*Valor*/)
{
    bEstaCorriendo = true;
    GetCharacterMovement()->MaxWalkSpeed = VelocidadSprint;   // 600
}

void AAlsasuaCharacter::OnCorrerFin(const FInputActionValue& /*Valor*/)
{
    bEstaCorriendo = false;
    GetCharacterMovement()->MaxWalkSpeed = VelocidadAndar;    // 300
}

// ─────────────────────────────────────────────────────────────────────────────
//  OnAgacharse: alternar agacharse.
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::OnAgacharse(const FInputActionValue& /*Valor*/)
{
    if (bEstaAgachado)
    {
        UnCrouch();
        bEstaAgachado = false;
    }
    else
    {
        Crouch();
        bEstaAgachado = true;
    }
}
