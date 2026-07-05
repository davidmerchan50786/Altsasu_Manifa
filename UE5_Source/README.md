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

## Notas de diseño

- **Cámara**: `SpringArm` (longitud 400, pitch −30°, lag posicional y rotacional)
  + `Camera`. La rotación la aporta el brazo de resorte (`bUsePawnControlRotation`).
- **Movimiento**: relativo a cámara (yaw del controlador). `bOrientRotationToMovement`
  activado para que el warping de Motion Matching oriente la malla.
- **Velocidades**: andar 300 / correr 500 / sprint 600 cm/s. Correr alterna
  `MaxWalkSpeed` entre 300 y 600 (mantener `IA_Correr`).
- **Motion Matching**: `UMotionTrajectoryComponent` alimenta la búsqueda de poses
  de PoseSearch; el AnimBP lee la trayectoria para elegir la animación.
