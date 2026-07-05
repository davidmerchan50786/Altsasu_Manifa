# AlsasuaManifa — Módulo C++ de UE5 (personaje + GameMode)

Puerto a Unreal Engine 5 (5.4+) del personaje jugable y el GameMode del proyecto
Unity **Altsasu Manifa**. Genera el andamiaje C++ para integrar Enhanced Input,
Motion Matching (GASP) y una cámara AAA en tercera persona.

## Contenido

```
Source/AlsasuaManifa/
├── AlsasuaManifa.Build.cs        # dependencias del módulo
├── AlsasuaManifa.h / .cpp        # módulo primario de juego
├── Public/
│   ├── AlsasuaCharacter.h        # personaje (cámara + input + trayectoria)
│   └── AlsasuaGameMode.h         # GameMode
└── Private/
    ├── AlsasuaCharacter.cpp
    └── AlsasuaGameMode.cpp
```

## Plugins requeridos (habilitar en el `.uproject`)

`EnhancedInput`, `PoseSearch`, `MotionTrajectory`, `AnimationWarping`,
`AnimationLocomotionLibrary`, `Chooser`.

## Cableado en el editor (sin recompilar)

1. Crear un Blueprint `BP_AlsasuaCharacter` derivado de `AAlsasuaCharacter`.
2. Asignar la malla **SK_Mannequin** y el AnimBP de Motion Matching (GASP).
3. Crear los assets de entrada:
   - `IMC_Jugador` (Input Mapping Context)
   - `IA_Mover` (Vector2D), `IA_Mirar` (Vector2D), `IA_Saltar` (bool),
     `IA_Correr` (bool), `IA_Agacharse` (bool)
4. Asignar esos assets en el `BP_AlsasuaCharacter`.
5. En `DefaultEngine.ini`, fijar el GameMode y el pawn por defecto:

```ini
[/Script/EngineSettings.GameMapsSettings]
GlobalDefaultGameMode=/Script/AlsasuaManifa.AlsasuaGameMode
```

> El `AAlsasuaGameMode` fija `DefaultPawnClass = AAlsasuaCharacter` en C++; puede
> sobreescribirse por Blueprint desde la config del proyecto.

## `DefaultInput.ini` (habilitar Enhanced Input a nivel de motor)

Enhanced Input necesita que las clases de input por defecto apunten a sus
versiones "Enhanced". Añade esto a `Config/DefaultInput.ini`:

```ini
[/Script/Engine.InputSettings]
DefaultPlayerInputClass=/Script/EnhancedInput.EnhancedPlayerInput
DefaultInputComponentClass=/Script/EnhancedInput.EnhancedInputComponent
```

## Notas de diseño

- **Cámara**: `SpringArm` (longitud 400, pitch −30°, lag posicional y rotacional)
  + `Camera`. La rotación la aporta el brazo de resorte (`bUsePawnControlRotation`).
- **Movimiento**: relativo a cámara (yaw del controlador). `bOrientRotationToMovement`
  activado y `bUseControllerRotationYaw = false` para que el warping de Motion
  Matching oriente la malla.
- **Velocidades** (todas `EditAnywhere`, configurables por instancia): andar 300 /
  correr 500 / sprint 600 / agachado 200 cm/s.
- **Sprint con aguante**: `IA_Correr` marca la *intención*; en `Tick` solo se
  esprinta si hay aguante y el personaje se mueve. El aguante se consume al
  esprintar y se regenera al parar (`GetAguanteNormalizado()` para el HUD).
- **Agacharse**: `Crouch()`/`UnCrouch()` gestionan la cápsula; `CrouchedHalfHeight`
  y `MaxWalkSpeedCrouched` configurables.
- **Salto**: `bNotifyApex = true` → `NotifyJumpApex()` y `Landed()` como hooks
  para blends/efectos.
- **Motion Matching**: `UCharacterTrajectoryComponent` (GASP, UE5.4+) alimenta la
  búsqueda de poses de PoseSearch; se actualiza solo (no se llama a su tick).
  Accesible desde el AnimBP con `GetMotionTrajectory()`.
- **AimOffset**: `GetAimOffsetYaw()` / `GetAimOffsetPitch()` (BlueprintPure)
  devuelven el delta normalizado control↔actor para el blendspace de apuntado.
- **Listo para GAS**: `Vida` y `Aguante` (con máximos y getters) están como
  `UPROPERTY` a modo de stub. Al integrar GameplayAbilities, migrarlos a un
  `UAttributeSet` y añadir el `UAbilitySystemComponent` (y el módulo
  `GameplayAbilities` al `Build.cs`).
