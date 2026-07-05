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
#include "Components/SkeletalMeshComponent.h"
#include "Engine/LocalPlayer.h"
#include "Engine/World.h"
#include "Engine/EngineTypes.h"
#include "CollisionQueryParams.h"
#include "Kismet/GameplayStatics.h"

// Enhanced Input (UE5.4).
#include "EnhancedInputComponent.h"
#include "EnhancedInputSubsystems.h"
#include "InputMappingContext.h"
#include "InputAction.h"

// Sistemas propios.
#include "IInteractuable.h"
#include "AlsasuaSaveGame.h"

// Componente de trayectoria de GASP (plugin MotionTrajectory, UE5.4). Registra
// y predice la trayectoria para PoseSearch y se actualiza por sí mismo.
#include "CharacterTrajectoryComponent.h"

// ─────────────────────────────────────────────────────────────────────────────
//  Constructor: componentes y parámetros de movimiento.
// ─────────────────────────────────────────────────────────────────────────────
AAlsasuaCharacter::AAlsasuaCharacter()
{
    // Tick para regenerar/consumir aguante, actualizar velocidad y variables de anim.
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

    // Emitir el estado inicial para que el HUD arranque con los valores correctos.
    OnHealthChanged.Broadcast(CurrentHealth, MaxHealth);
    OnStaminaChanged.Broadcast(CurrentStamina, MaxStamina);

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
//  Tick: aguante, velocidad, variables de animación y detección de salientes.
//  (La trayectoria de GASP se actualiza sola; NO se llama a su TickComponent.)
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::Tick(float DeltaSeconds)
{
    Super::Tick(DeltaSeconds);

    ActualizarAguante(DeltaSeconds);
    ActualizarVelocidadMovimiento();
    ActualizarVariablesAnimacion();
    CheckTraversal();
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

        // Interactuar: Started lanza el line trace de interacción.
        if (IA_Interactuar)
            EIC->BindAction(IA_Interactuar, ETriggerEvent::Started, this, &AAlsasuaCharacter::OnInteractuar);
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
//  OnInteractuar: line trace hacia delante y llamada a IInteractuable::Interactuar.
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::OnInteractuar(const FInputActionValue& /*Valor*/)
{
    UWorld* World = GetWorld();
    if (World == nullptr)
        return;

    // Traza desde la cámara/vista hacia delante hasta AlcanceInteraccion.
    FVector Origen;
    FRotator RotVista;
    GetActorEyesViewPoint(Origen, RotVista);
    const FVector Fin = Origen + RotVista.Vector() * AlcanceInteraccion;

    FCollisionQueryParams Params;
    Params.AddIgnoredActor(this);

    FHitResult Impacto;
    if (World->LineTraceSingleByChannel(Impacto, Origen, Fin, ECC_Visibility, Params))
    {
        AActor* Objetivo = Impacto.GetActor();
        // Si el actor golpeado implementa la interfaz, invocar Interactuar.
        if (Objetivo && Objetivo->Implements<UInteractuable>())
        {
            IInteractuable::Execute_Interactuar(Objetivo, this);
        }
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
    const float AnteriorAguante = CurrentStamina;

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

    // Emitir el delegado solo si el valor cambió de forma apreciable (evita spam).
    if (!FMath::IsNearlyEqual(AnteriorAguante, CurrentStamina))
    {
        OnStaminaChanged.Broadcast(CurrentStamina, MaxStamina);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  ActualizarVariablesAnimacion: Speed2D, Direction, Gait y bIsInAir para el AnimBP.
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::ActualizarVariablesAnimacion()
{
    const UCharacterMovementComponent* Mov = GetCharacterMovement();
    if (Mov == nullptr)
        return;

    const FVector Velocidad = GetVelocity();

    // Velocidad planar (ignora la componente vertical).
    Speed2D = Velocidad.Size2D();

    // Dirección del movimiento respecto al forward del actor [-180,180].
    // (Cálculo manual del ángulo con signo; equivale a UKismetAnimationLibrary::CalculateDirection.)
    if (Speed2D > 1.f)
    {
        const FVector Forward = GetActorForwardVector().GetSafeNormal2D();
        const FVector VelNorm = Velocidad.GetSafeNormal2D();
        const float AnguloRad = FMath::Acos(FMath::Clamp(FVector::DotProduct(Forward, VelNorm), -1.f, 1.f));
        float Angulo = FMath::RadiansToDegrees(AnguloRad);
        // Signo según el lado (producto vectorial Z).
        if (FVector::CrossProduct(Forward, VelNorm).Z < 0.f)
        {
            Angulo = -Angulo;
        }
        Direction = Angulo;
    }
    else
    {
        Direction = 0.f;
    }

    // Estado aéreo.
    bIsInAir = Mov->IsFalling();

    // Marcha de locomoción según velocidad y estado.
    if (Speed2D <= 3.f)
    {
        Gait = EMovementGait::Idle;
    }
    else if (bIsRunning)
    {
        Gait = EMovementGait::Sprint;
    }
    else if (Speed2D > UmbralCorrer)
    {
        Gait = EMovementGait::Run;
    }
    else
    {
        Gait = EMovementGait::Walk;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  CheckTraversal: sphere trace hacia delante+arriba para detectar un saliente.
//  Actualiza bCanVault (lo consume el AnimBP/lógica de vault).
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::CheckTraversal()
{
    UWorld* World = GetWorld();
    if (World == nullptr)
    {
        bCanVault = false;
        return;
    }

    // Solo tiene sentido detectar salientes en el suelo y moviéndose hacia delante.
    if (bIsInAir || Speed2D < 10.f)
    {
        bCanVault = false;
        return;
    }

    // Origen a la altura del pecho; destino hacia delante.
    const FVector Origen = GetActorLocation() + FVector(0.f, 0.f, 20.f);
    const FVector Fin    = Origen + GetActorForwardVector() * DistanciaDeteccionSaliente;

    FCollisionQueryParams Params;
    Params.AddIgnoredActor(this);
    const FCollisionShape Esfera = FCollisionShape::MakeSphere(RadioDeteccionSaliente);

    FHitResult Impacto;
    // Golpe frontal → hay un obstáculo que potencialmente se puede trepar.
    const bool bChoqueFrontal = World->SweepSingleByChannel(
        Impacto, Origen, Fin, FQuat::Identity, ECC_Visibility, Esfera, Params);

    if (!bChoqueFrontal)
    {
        bCanVault = false;
        return;
    }

    // Comprobar que hay una superficie plana arriba del obstáculo (borde superable).
    const FVector OrigenArriba = Impacto.ImpactPoint + FVector(0.f, 0.f, 100.f)
                                 + GetActorForwardVector() * 10.f;
    const FVector FinArriba    = OrigenArriba - FVector(0.f, 0.f, 120.f);

    FHitResult ImpactoTecho;
    const bool bHaySuelo = World->LineTraceSingleByChannel(
        ImpactoTecho, OrigenArriba, FinArriba, ECC_Visibility, Params);

    // Vault válido si hay borde y su altura es asumible (por debajo del pecho + margen).
    bCanVault = bHaySuelo &&
                (ImpactoTecho.ImpactPoint.Z - GetActorLocation().Z) < 120.f;
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

// ─────────────────────────────────────────────────────────────────────────────
//  Setters de atributos (emiten los delegados para el HUD).
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::SetCurrentHealth(float NuevaSalud)
{
    const float Anterior = CurrentHealth;
    CurrentHealth = FMath::Clamp(NuevaSalud, 0.f, MaxHealth);
    if (!FMath::IsNearlyEqual(Anterior, CurrentHealth))
    {
        OnHealthChanged.Broadcast(CurrentHealth, MaxHealth);
    }
}

void AAlsasuaCharacter::SetCurrentStamina(float NuevoAguante)
{
    const float Anterior = CurrentStamina;
    CurrentStamina = FMath::Clamp(NuevoAguante, 0.f, MaxStamina);
    if (!FMath::IsNearlyEqual(Anterior, CurrentStamina))
    {
        OnStaminaChanged.Broadcast(CurrentStamina, MaxStamina);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  GetFootIKLocation: line trace hacia abajo desde el socket del pie.
// ─────────────────────────────────────────────────────────────────────────────
FVector AAlsasuaCharacter::GetFootIKLocation(bool bLeftFoot) const
{
    const USkeletalMeshComponent* Malla = GetMesh();
    UWorld* World = GetWorld();
    if (Malla == nullptr || World == nullptr)
        return GetActorLocation();

    const FName Socket = bLeftFoot ? SocketPieIzquierdo : SocketPieDerecho;
    const FVector PosicionPie = Malla->GetSocketLocation(Socket);

    // Trazar hacia abajo desde el pie para encontrar el suelo.
    const FVector Origen = FVector(PosicionPie.X, PosicionPie.Y, GetActorLocation().Z);
    const FVector Fin    = Origen - FVector(0.f, 0.f, DistanciaTrazaFootIK
                                            + GetCapsuleComponent()->GetScaledCapsuleHalfHeight());

    FCollisionQueryParams Params;
    Params.AddIgnoredActor(this);

    FHitResult Impacto;
    if (World->LineTraceSingleByChannel(Impacto, Origen, Fin, ECC_Visibility, Params))
    {
        return Impacto.ImpactPoint;
    }
    // Sin suelo: devolver la posición actual del pie (sin ajuste de IK).
    return PosicionPie;
}

// ─────────────────────────────────────────────────────────────────────────────
//  PlayFootstepSound: reproduce el sonido de pisada (desde un AnimNotify).
// ─────────────────────────────────────────────────────────────────────────────
void AAlsasuaCharacter::PlayFootstepSound()
{
    if (FootstepSound)
    {
        UGameplayStatics::PlaySoundAtLocation(this, FootstepSound, GetActorLocation());
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Guardado / carga de partida (delegan en UAlsasuaSaveGame).
// ─────────────────────────────────────────────────────────────────────────────
bool AAlsasuaCharacter::GuardarPartida()
{
    return UAlsasuaSaveGame::GuardarPersonaje(this);
}

bool AAlsasuaCharacter::CargarPartida()
{
    return UAlsasuaSaveGame::CargarPersonaje(this);
}
