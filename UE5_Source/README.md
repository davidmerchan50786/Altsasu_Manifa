# AlsasuaManifa — Módulo C++ de UE 5.4 (personaje + GameMode)

Puerto a **Unreal Engine 5.4** del personaje jugable y el GameMode del proyecto
**Altsasu Manifa**. Genera el andamiaje C++ para integrar Enhanced Input,
Motion Matching (GASP), una cámara AAA en tercera persona y stubs listos para GAS.

> **Versión objetivo: UE 5.4 exacta.** El `.uproject` fija `EngineAssociation` a
> `5.4` y el `Build.cs` compila en **C++20** con Unity build desactivado (IWYU).

## Contenido

```
UE5_Source/
├── AlsasuaManifa.uproject            # proyecto UE 5.4 con los plugins habilitados
├── Config/
│   ├── DefaultGame.ini               # GameMode/pawn/PC/HUD por defecto
│   └── DefaultInput.ini              # Enhanced Input a nivel de motor
└── Source/AlsasuaManifa/
    ├── AlsasuaManifa.Build.cs        # dependencias del módulo (Cpp20, bUseUnity=false)
    ├── AlsasuaManifa.h / .cpp        # módulo primario de juego
    ├── Public/
    │   ├── AlsasuaCharacter.h        # personaje (cámara + input + trayectoria + ALS)
    │   ├── AlsasuaAnimInstance.h     # AnimInstance base para el AnimBP (GASP)
    │   ├── AlsasuaPlayerController.h # sensibilidad de ratón, pausa, modo de entrada
    │   ├── AlsasuaHUD.h              # crea el widget UMG principal
    │   ├── AlsasuaSaveGame.h         # guardado/carga de partida (USaveGame)
    │   ├── IInteractuable.h          # interfaz de objetos interactuables
    │   └── AlsasuaGameMode.h         # GameMode (pawn + PC + HUD)
    └── Private/
        ├── AlsasuaCharacter.cpp
        ├── AlsasuaAnimInstance.cpp
        ├── AlsasuaPlayerController.cpp
        ├── AlsasuaHUD.cpp
        ├── AlsasuaSaveGame.cpp
        └── AlsasuaGameMode.cpp
```

## Plugins (ya habilitados en `AlsasuaManifa.uproject`)

`EnhancedInput`, `PoseSearch`, `MotionTrajectory`, `AnimationWarping`,
`AnimationLocomotionLibrary`, `Chooser`, `GameplayAbilities`.

Si integras este `Source/` en un `.uproject` existente, copia la sección
`"Plugins"` del `.uproject` de este repo o habilítalos desde
**Editar → Plugins** y reinicia el editor.

## Compilación (UE 5.4)

1. Coloca `AlsasuaManifa.uproject` y la carpeta `Source/` en la raíz del proyecto.
2. Clic derecho sobre el `.uproject` → **Generate Visual Studio project files**.
3. Compila desde el IDE o con:
   ```
   "E:\Epic Games\UE_5.4\Engine\Build\BatchFiles\Build.bat" ^
       AlsasuaManifaEditor Win64 Development ^
       -Project="<ruta>\AlsasuaManifa.uproject" -WaitMutex
   ```

## Cableado en el editor (sin recompilar)

1. Crear un Blueprint `BP_JugadorAlsasua` derivado de `AAlsasuaCharacter`.
2. Asignar la malla **SK_Mannequin** al `Mesh` heredado y su `Anim Class` al
   AnimBP de Motion Matching (GASP).
3. Crear los assets de entrada:
   - `IMC_Jugador` (Input Mapping Context)
   - `IA_Mover` (Vector2D), `IA_Mirar` (Vector2D), `IA_Saltar` (bool),
     `IA_Correr` (bool), `IA_Agacharse` (bool), `IA_Interactuar` (bool)
4. En `BP_JugadorAlsasua`, asignar esos assets en las propiedades **Input**
   (`IMC_Jugador`, `IA_Mover`, `IA_Mirar`, `IA_Saltar`, `IA_Correr`, `IA_Agacharse`,
   `IA_Interactuar`).
5. Ajustar (opcional) las velocidades **Movimiento** (`MaxWalkSpeed` 300 /
   `MaxSprintSpeed` 600 / `MaxCrouchSpeed` 150) y los **Atributos** de
   Salud/Aguante desde el panel de detalles.
6. En `DefaultEngine.ini`, fijar el GameMode por defecto:

```ini
[/Script/EngineSettings.GameMapsSettings]
GlobalDefaultGameMode=/Script/AlsasuaManifa.AlsasuaGameMode
```

> El `AAlsasuaGameMode` fija `DefaultPawnClass = AAlsasuaCharacter` y
> `HUDClass = AHUD` en C++; ambos pueden sobreescribirse por Blueprint.

## `DefaultInput.ini` (habilitar Enhanced Input a nivel de motor)

Enhanced Input necesita que las clases de input por defecto apunten a sus
versiones "Enhanced". Añade esto a `Config/DefaultInput.ini`:

```ini
[/Script/Engine.InputSettings]
DefaultPlayerInputClass=/Script/EnhancedInput.EnhancedPlayerInput
DefaultInputComponentClass=/Script/EnhancedInput.EnhancedInputComponent
```

## Conectar el AnimBP con `AlsasuaAnimInstance` y Motion Matching

El personaje crea un `UCharacterTrajectoryComponent` (plugin **MotionTrajectory**,
estable en UE 5.4) que se actualiza por sí mismo cada frame. Para el grafo de
animación se incluye una clase base en C++, **`UAlsasuaAnimInstance`**, que ya
cachea el personaje y expone todas las variables listas para usar.

1. Crea el AnimBP derivándolo de **`AlsasuaAnimInstance`** (no de `AnimInstance`).
   Así hereda, ya calculadas en C++ (`NativeUpdateAnimation`), estas variables
   `BlueprintReadOnly`:
   - `Speed2D`, `Direction`, `Gait` (enum `EMovementGait`), `bIsInAir`,
     `bIsCrouching`, `bIsRunning`, `bCanVault`
   - `AimOffsetYaw`, `AimOffsetPitch`
   - `Trajectory` (el `UCharacterTrajectoryComponent`)
2. En el grafo, arrastra **`Trajectory`** al pin *Trajectory* del nodo **Motion
   Matching** de PoseSearch.
3. Usa `AimOffsetYaw` / `AimOffsetPitch` como entradas del blendspace de apuntado.
4. Usa `Gait` / `Speed2D` / `Direction` para los blends de locomoción y `bCanVault`
   para disparar el montaje de salto/trepado (vault).

> Si prefieres calcularlo en el grafo, el personaje también expone los accesores
> equivalentes: `GetCharacterTrajectory()`, `GetSpeed2D()`, `GetMovementDirection()`,
> `GetMovementGait()`, `IsRunning()`, `IsCrouchingState()`, `CanVault()`,
> `GetAimOffsetYaw()`, `GetAimOffsetPitch()`, `GetStaminaNormalized()`.

## Interacción, Foot IK y pisadas

- **Interacción**: la acción `IA_Interactuar` lanza un *line trace* de
  `AlcanceInteraccion` cm desde la vista; si el actor golpeado implementa la
  interfaz **`IInteractuable`**, se llama a `Interactuar(Jugador)`. Implementa la
  interfaz en cualquier Actor (C++ o Blueprint) para hacerlo interactuable.
- **Foot IK**: `GetFootIKLocation(bLeftFoot)` devuelve el punto de suelo bajo cada
  pie (sockets `foot_l` / `foot_r`) para alimentar nodos *Two Bone IK* en el AnimBP.
- **Pisadas**: asigna `FootstepSound` y llama a `PlayFootstepSound()` desde un
  *AnimNotify* en las animaciones de caminar/correr.

## HUD y delegados de atributos

El personaje emite dos delegados `BlueprintAssignable` cuando cambian sus
atributos: **`OnHealthChanged(Current, Max)`** y **`OnStaminaChanged(Current, Max)`**.
Enlázalos desde el widget UMG (creado por `AAlsasuaHUD`) para actualizar las barras
sin hacer *polling* en Tick. `AAlsasuaHUD` instancia el `HUDWidgetClass` en
`BeginPlay` y lo añade al viewport.

## PlayerController (sensibilidad de ratón y pausa)

`AAlsasuaPlayerController` expone `MouseSensitivityX` / `MouseSensitivityY`
(`EditAnywhere`) y los métodos `SetMouseSensitivity(X, Y)` y `TogglePause()`
(`BlueprintCallable`), además de fijar el modo de entrada solo-juego en `BeginPlay`.

## Guardado de partida

`UAlsasuaSaveGame` persiste salud, aguante, posición y rotación en la ranura
`AlsasuaSave1`. Desde el personaje (o Blueprint) llama a **`GuardarPartida()`** y
**`CargarPartida()`**; internamente usan `UGameplayStatics::SaveGameToSlot` /
`LoadGameFromSlot`.

## Notas de diseño

- **Cámara**: `SpringArm` (longitud 400, pitch −30°, lag posicional 10 y
  rotacional 15) + `Camera`. La rotación la aporta el brazo (`bUsePawnControlRotation`).
- **Movimiento**: relativo a cámara (yaw del controlador). `bOrientRotationToMovement`
  activado y `bUseControllerRotationYaw = false` para que el warping de Motion
  Matching oriente la malla.
- **Velocidades** (todas `EditAnywhere`): `MaxWalkSpeed` 300 / `MaxSprintSpeed`
  600 / `MaxCrouchSpeed` 150 cm/s.
- **Sprint con aguante**: `IA_Correr` marca la *intención*; en `Tick` solo se
  esprinta si hay aguante y el personaje se mueve. El aguante se consume al
  esprintar y se regenera al parar (`GetStaminaNormalized()` para el HUD).
- **Agacharse**: `Crouch()`/`UnCrouch()` gestionan la cápsula;
  `CrouchedHalfHeight` y `MaxWalkSpeedCrouched` configurables;
  `NavAgentProps.bCanCrouch = true`.
- **Salto**: `bNotifyApex = true` → `NotifyJumpApex()` y `Landed()` como hooks
  para blends/efectos.
- **Motion Matching**: `UCharacterTrajectoryComponent` (GASP) alimenta PoseSearch;
  se actualiza solo. Accesible desde el AnimBP con `GetCharacterTrajectory()`.
- **AimOffset**: `GetAimOffsetYaw()` / `GetAimOffsetPitch()` (BlueprintPure)
  devuelven el delta normalizado control↔actor para el blendspace de apuntado.
- **Listo para GAS**: `CurrentHealth`/`MaxHealth` y `CurrentStamina`/`MaxStamina`
  están como `UPROPERTY` a modo de stub, y los módulos `GameplayAbilities`,
  `GameplayTags` y `GameplayTasks` ya están enlazados. Al integrar el sistema,
  migrar estos atributos a un `UAttributeSet` y añadir un
  `UAbilitySystemComponent`.
