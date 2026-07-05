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
└── Source/AlsasuaManifa/
    ├── AlsasuaManifa.Build.cs        # dependencias del módulo (Cpp20, bUseUnity=false)
    ├── AlsasuaManifa.h / .cpp        # módulo primario de juego
    ├── Public/
    │   ├── AlsasuaCharacter.h        # personaje (cámara + input + trayectoria)
    │   └── AlsasuaGameMode.h         # GameMode (pawn + HUD por defecto)
    └── Private/
        ├── AlsasuaCharacter.cpp
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
     `IA_Correr` (bool), `IA_Agacharse` (bool)
4. En `BP_JugadorAlsasua`, asignar esos assets en las propiedades **Input**
   (`IMC_Jugador`, `IA_Mover`, `IA_Mirar`, `IA_Saltar`, `IA_Correr`, `IA_Agacharse`).
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

## Conectar el AnimBP con `UCharacterTrajectoryComponent`

El personaje crea un `UCharacterTrajectoryComponent` (plugin **MotionTrajectory**,
estable en UE 5.4) que se actualiza por sí mismo cada frame.

1. En el AnimBP (Event Graph), obtén el pawn con `TryGetPawnOwner` y castea a
   `AAlsasuaCharacter`.
2. Llama a **`GetCharacterTrajectory()`** para recuperar el componente y, desde
   él, la `Trajectory` (histórica + predicha) que alimenta el nodo **Motion
   Matching** de PoseSearch.
3. Para el AimOffset, usa **`GetAimOffsetYaw()`** y **`GetAimOffsetPitch()`** como
   entradas del blendspace de apuntado.
4. Los flags **`IsRunning()`** / **`IsCrouchingState()`** y
   **`GetStaminaNormalized()`** están disponibles para blends y HUD.

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
