# Alsasua Manifa — Simulador de Manifestaciones

Videojuego en tercera persona desarrollado con **Unity 6 + HDRP** que recrea el ambiente de las manifestaciones y la vida urbana de **Alsasua (Navarra)**. El jugador explora un escenario en exteriores, interactúa con NPCs controlados por IA, puede conducir vehículos y dispone de un sistema de armas completo.

---

## Índice

1. [Descripción del juego](#descripción-del-juego)
2. [Instalación de Unity y dependencias](#instalación-de-unity-y-dependencias)
3. [Cómo ejecutar el proyecto](#cómo-ejecutar-el-proyecto)
4. [Controles](#controles)
5. [Estructura del proyecto](#estructura-del-proyecto)

---

## Descripción del juego

Alsasua Manifa es un simulador en tercera persona ambientado en el municipio navarro de Alsasua. El título recrea la atmósfera de la localidad: sus calles, plazas y dinámica social, a través de mecánicas de exploración, interacción con personajes no jugadores (NPCs) impulsados por IA con navegación mediante NavMesh, y un sistema de vehículos conducibles.

**Características principales:**

- Escenario exterior completamente renderizado con **Unity HDRP** (iluminación volumétrica, cielo físico, niebla volumétrica).
- NPCs con IA de persecución basada en **NavMesh Agent** de Unity.
- Sistema de armas (armas de fuego con raycast, granadas y lanzacohetes con física de cuerpo rígido).
- Vehículos conducibles con entrada/salida dinámica, radio del coche y daño por colisión.
- HUD con barra de vida, hambre, sed y contador de munición.
- Herramientas de editor personalizadas (Auto-Grass para pintar hierba en el terreno).

---

## Instalación de Unity y dependencias

### Requisitos del sistema

| Componente | Mínimo recomendado |
|---|---|
| SO | Windows 10 / macOS 13 / Ubuntu 22.04 |
| CPU | Intel i7 8ª gen / AMD Ryzen 5 3600 |
| RAM | 16 GB |
| GPU | NVIDIA GTX 1070 / AMD RX 5700 (HDRP requiere DX12 o Vulkan) |
| Almacenamiento | 10 GB libres |
| Pantalla | 1920×1080 |

### 1. Instalar Unity Hub

Descarga e instala **Unity Hub** desde [https://unity.com/download](https://unity.com/download).

### 2. Instalar la versión correcta de Unity

El proyecto usa **Unity 6000.3.10f1**. Para instalarla:

1. Abre Unity Hub → *Installs* → *Install Editor*.
2. Selecciona la versión `6000.3.10f1` (Unity 6).  
   Si no aparece en la lista, búscala en el [Unity Archive](https://unity.com/releases/editor/archive) y abre el enlace `unityhub://` directamente.
3. En los módulos adicionales, incluye al menos:
   - **Windows Build Support (IL2CPP)** o el soporte de plataforma que necesites.
   - **Documentation** (opcional pero recomendado).

### 3. Clonar el repositorio

```bash
git clone https://github.com/davidmerchan50786/Altsasu_Manifa.git
cd Altsasu_Manifa
```

### 4. Abrir el proyecto en Unity Hub

1. Unity Hub → *Projects* → *Add* → selecciona la carpeta raíz del repositorio.
2. Unity Hub detectará automáticamente la versión del editor y ofrecerá abrirlo con `6000.3.10f1`.
3. En la primera apertura Unity importará todos los assets y compilará shaders HDRP (puede tardar varios minutos).

### 5. Dependencias del proyecto (Package Manager)

Las dependencias se resuelven automáticamente desde `Packages/manifest.json` al abrir el proyecto:

| Paquete | Versión |
|---|---|
| High Definition RP (`com.unity.render-pipelines.high-definition`) | 17.3.0 |
| Input System (`com.unity.inputsystem`) | 1.18.0 |
| Timeline (`com.unity.timeline`) | 1.8.10 |
| UI Toolkit / uGUI (`com.unity.ugui`) | 2.0.0 |
| Visual Scripting (`com.unity.visualscripting`) | 1.9.9 |
| Multiplayer Center (`com.unity.multiplayer.center`) | 1.0.1 |

No se requiere ninguna acción manual para instalar paquetes; Unity Package Manager los descarga automáticamente.

---

## Cómo ejecutar el proyecto

1. Con el proyecto abierto en Unity Editor, ve al menú **File → Open Scene**.
2. Selecciona `Assets/OutdoorsScene.unity` (escena principal).
3. Pulsa el botón **▶ Play** en la barra central del Editor para ejecutar en modo reproducción.

### Escenas disponibles

| Escena | Ruta | Descripción |
|---|---|---|
| `OutdoorsScene` | `Assets/OutdoorsScene.unity` | Escena principal del juego |
| `Free Version Method 2` | `Assets/#Scenes/Free Version Method 2.unity` | Prototipo de mecánicas de movimiento |
| `Starter Demo Test` | `Assets/#Scenes/Starter Demo Test.unity` | Escena de prueba de assets |

### Build de producción

1. **File → Build Settings**.
2. Añade `OutdoorsScene` a la lista de escenas.
3. Selecciona la plataforma destino (PC, Mac & Linux Standalone).
4. Pulsa **Build** y elige el directorio de salida.

---

## Controles

### Movimiento del jugador

| Tecla / Input | Acción |
|---|---|
| `W A S D` | Mover al personaje |
| `LeftShift` | Correr |
| `Space` | Saltar |
| `E` | Cambiar hombro de la cámara (izquierda / derecha) |

### Combate

| Tecla / Input | Acción |
|---|---|
| `Botón derecho del ratón` (Fire2) | Apuntar (ADS) — zoom de cámara |
| `Botón izquierdo del ratón` (Fire1) | Disparar (mientras se apunta) |
| `R` | Recargar arma |
| `Enter` | Comprar / recoger arma del suelo |

### Vehículo

| Tecla / Input | Acción |
|---|---|
| Entrar en zona de trigger del coche | Entrar al vehículo automáticamente |
| `W A S D` | Acelerar / girar / frenar |
| `J` | Salir del vehículo |
| `T` | Encender / apagar radio del coche |

### Miscelánea

| Tecla / Input | Acción |
|---|---|
| `N` | Ocultar objeto de posición fija (PositionKeep) |

---

## Estructura del proyecto

```
Altsasu_Manifa/
├── Assets/
│   ├── OutdoorsScene.unity          # Escena principal
│   ├── CarDamage.cs                 # Script de daño por atropello
│   ├── InputSystem_Actions.inputactions  # Mapa de controles (Input System)
│   │
│   ├── #Scenes/                     # Escenas adicionales / prototipos
│   ├── #Sounds/                     # Audio de armas (disparos, recarga)
│   │
│   ├── #Tools/                      # Herramientas y sistemas core
│   │   ├── CarCamera/               # Cámara de vehículo (CarCam.cs)
│   │   ├── Editor/                  # Herramientas de editor (AutoGrass.cs)
│   │   ├── Materials/               # Materiales reutilizables
│   │   └── Resources/
│   │       ├── Scripts/             # Scripts principales del juego
│   │       │   ├── PlayerMotor.cs   # Movimiento del jugador
│   │       │   ├── Weapons.cs       # Sistema de armas
│   │       │   ├── GUISystem.cs     # HUD (munición, dinero)
│   │       │   ├── OrbitCamera.cs   # Cámara en órbita / ADS
│   │       │   ├── Explosion.cs     # Fuerza de explosión
│   │       │   ├── GrenadesRocket.cs# Proyectiles físicos
│   │       │   ├── MakeMoney.cs     # Recogida de dinero
│   │       │   ├── WeaponTake.cs    # Recogida de armas del suelo
│   │       │   ├── LookAhead.cs     # Anticipación de la cámara
│   │       │   └── JoystickToEvents.cs # Input de movimiento
│   │       ├── Particles/           # Sistemas de partículas (impactos, explosiones)
│   │       ├── Weapons/             # Prefabs de armas
│   │       ├── Skyboxes/            # Texturas de cielo
│   │       └── Sounds/              # AudioClips del sistema de armas
│   │
│   ├── #Xtra/                       # Assets de personajes y vehículos
│   │   ├── Health.cs                # Vida y muerte de NPCs
│   │   ├── PlayerStats.cs           # Estadísticas del jugador (vida, hambre, sed)
│   │   ├── CarManager.cs            # Gestión de entrada/salida de vehículo
│   │   ├── CarRadio.cs              # Radio del coche
│   │   ├── Footsteps.cs             # Audio de pasos
│   │   ├── ShoulderSwap.cs          # Cambio de hombro de cámara
│   │   ├── Slomo.cs                 # Cámara lenta (stub)
│   │   ├── AutoDestroy.cs           # Auto-destrucción temporizada
│   │   ├── TestSpawner.cs           # Spawner de NPCs para pruebas
│   │   ├── WeaponEquip.cs           # Equipar arma desde interfaz
│   │   ├── TargetFinder.cs          # Búsqueda de objetivos (stub)
│   │   ├── MouseLock.cs             # Bloqueo del cursor
│   │   ├── Standard Assets/         # Assets estándar de Unity (IA, vehículos)
│   │   │   ├── Characters/          # AICharacterControl, ThirdPersonCharacter
│   │   │   ├── Vehicles/Car/        # CarController, CarUserControl, CarAudio
│   │   │   └── ParticleSystems/     # Explosiones, fuego, humo
│   │   ├── Locomotion Setup/        # Controlador de animación de locomoción
│   │   └── SimplestMeshBaker/       # Herramienta de bake de mallas (Editor)
│   │
│   ├── EscenarioSuelo/              # Assets del escenario (suelo, terreno)
│   ├── Models/                      # Modelos 3D y PositionKeep.cs
│   ├── Settings/                    # Perfiles HDRP (Balanced, High Fidelity, Performant)
│   └── TutorialInfo/                # Readme del template de Unity
│
├── Packages/
│   ├── manifest.json                # Dependencias del proyecto
│   └── packages-lock.json           # Versiones bloqueadas
│
├── ProjectSettings/                 # Configuración del proyecto Unity
│   ├── ProjectVersion.txt           # Versión del editor (6000.3.10f1)
│   ├── GraphicsSettings.asset       # Configuración de renderizado
│   ├── HDRPProjectSettings.asset    # Configuración específica de HDRP
│   ├── InputManager.asset           # Ejes de input (sistema legacy)
│   ├── NavMeshAreas.asset           # Áreas de navegación de IA
│   └── QualitySettings.asset        # Niveles de calidad gráfica
│
└── Altsasu_Manifa.slnx              # Solución de Visual Studio
```
