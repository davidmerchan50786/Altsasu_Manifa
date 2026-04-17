# Documentación Técnica — Alsasua Manifa

## Índice

1. [Scripts principales](#scripts-principales)
2. [Sistema de IA (NPCs)](#sistema-de-ia-npcs)
3. [Sistema de físicas](#sistema-de-físicas)
4. [Sistema de rendering (HDRP)](#sistema-de-rendering-hdrp)
5. [Sistema de audio](#sistema-de-audio)
6. [Sistema de input](#sistema-de-input)

---

## Scripts principales

A continuación se describen todos los scripts del proyecto agrupados por responsabilidad.

---

### Jugador

#### `PlayerMotor.cs`
**Ruta:** `Assets/#Tools/Resources/Scripts/PlayerMotor.cs`

Controla el movimiento del personaje jugador. Requiere un componente `Animator`. Lee el input de movimiento a través de `JoystickToEvents` y lo traduce a valores de velocidad y dirección que alimentan al sistema de locomoción por animación.

| Campo / Propiedad | Tipo | Descripción |
|---|---|---|
| `target` | `Transform` | Transform de referencia de la cámara (para orientación) |
| `val` | `float` (privado) | Multiplicador de velocidad: 2 (andar) / 6 (correr) |

**Flujo de ejecución:**
1. `Update()` detecta `Jump` y `LeftShift`.
2. Llama a `JoystickToEvents.Do()` para obtener `speed` y `direction`.
3. Pasa esos valores a `Locomotion.Do()` que actualiza los parámetros del Animator.

---

#### `PlayerStats.cs`
**Ruta:** `Assets/#Xtra/PlayerStats.cs`

Gestiona las estadísticas vitales del jugador y actualiza la interfaz gráfica (sliders y textos de UI Legacy).

| Campo | Tipo | Descripción |
|---|---|---|
| `Health` / `MaxHealth` | `float` | Vida actual y máxima |
| `Hunger` / `Thirst` | `float` | Hambre y sed actuales |
| `HungerRate` / `ThirstRate` | `float` | Velocidad de descenso por segundo |
| `HungerBar` / `ThirstBar` / `HealtBar` | `Slider` | Barras de UI |
| `HealthText` / `HungerText` / `ThirstText` | `Text` | Etiquetas numéricas |

**Métodos:**
- `TextBarLink()` — sincroniza los valores numéricos con los elementos de UI.
- `Needs()` — decrementa `Hunger` y `Thirst` en función del `deltaTime`.

---

#### `ShoulderSwap.cs`
**Ruta:** `Assets/#Xtra/ShoulderSwap.cs`

Alterna la posición de la cámara entre el hombro derecho e izquierdo del personaje al pulsar `E`. El valor `Side` cicla 0 → 1 → 0.

---

#### `MouseLock.cs`
**Ruta:** `Assets/#Xtra/MouseLock.cs`

Bloquea y oculta el cursor del ratón durante el juego. Ver también la entrada en [Utilidades](#utilidades).

---

### Armas

#### `Weapons.cs`
**Ruta:** `Assets/#Tools/Resources/Scripts/Weapons.cs`

Sistema de armas completo para el jugador. Soporta tres tipos de disparo:

| Tipo | Animación (`WAnimation`) | Mecánica |
|---|---|---|
| Arma de fuego | `"Equip"` | Raycast desde el centro de la pantalla con dispersión (`Imprecision`) |
| Granada | `"Throw"` | Rigidbody lanzado con fuerza acumulada (carga al mantener Fire1) |
| Lanzacohetes | `"Bazooka"` | Rigidbody lanzado con fuerza fija de 5000 |

**Clase interna `WeaponsSetup`** (serializable, una instancia por arma):

| Campo | Tipo | Descripción |
|---|---|---|
| `WeapObj` | `Transform` | GameObject visible del arma |
| `leftHandle` / `rightHandle` | `Transform` | Puntos IK para las manos |
| `crossTexture` | `Texture2D` | Textura del punto de mira |
| `MuzzleFlash` | `GameObject` | Efecto de fogón |
| `WeaponSound` | `AudioClip` | Sonido de disparo |
| `WAnimation` | `string` | Estado del Animator para equipar |
| `WIk` | `string` | Estado del Animator para IK de manos |
| `Bullets` / `Magazine` | `int` | Balas actuales / cargadores restantes |
| `MaxBulletInMagazine` | `int` | Balas por cargador |
| `FireRate` | `float` | Cadencia (segundos entre disparos) |
| `DamageValue` | `float` | Daño aplicado a objetos con tag `"Enemy"` |
| `Power` | `float` | Fuerza de impacto sobre Rigidbodies |
| `Imprecision` | `float` | Radio de dispersión del raycast (píxeles) |
| `RigidBodyPrefab` | `Rigidbody` | Prefab del proyectil físico (granadas/cohetes) |

**Tags de superficie para impactos:** `Dirt`, `Metal`, `Wood`, `Glass`, `Water`, `Blood`, `Ground`, `Enemy`.

**IK de manos:** implementado en `OnAnimatorIK()`, activa IK en el layer 2 del Animator cuando el arma está en su estado de disparo (`WIk`).

---

#### `WeaponEquip.cs`
**Ruta:** `Assets/#Xtra/WeaponEquip.cs`

Equipa un arma concreta desde la interfaz (botones de menú). Busca el tag `"WeaponHand"` en la jerarquía y activa el arma indicada por `Index`.

---

#### `WeaponTake.cs`
**Ruta:** `Assets/#Tools/Resources/Scripts/WeaponTake.cs`

Permite al jugador recoger armas del escenario mediante un trigger. Muestra el coste en el HUD (`GUISystem.CostShow`) y descuenta dinero al pulsar `Enter`. Si el jugador no tiene suficiente dinero la compra no se realiza.

---

#### `GrenadesRocket.cs`
**Ruta:** `Assets/#Tools/Resources/Scripts/GrenadesRocket.cs`

Componente del proyectil físico (granada o cohete). Se destruye:
- Al colisionar (si `OnCollision = true`).
- Pasado el tiempo `timeLeft` (por defecto 2,5 s).

Al destruirse instancia el prefab de explosión (`explosionPrefab`), ajusta el filtro de paso bajo del audio en función de la distancia al `AudioListener` y destruye el GameObject.

---

#### `Explosion.cs`
**Ruta:** `Assets/#Tools/Resources/Scripts/Explosion.cs`

Aplica fuerza de explosión radial (`AddExplosionForce`) a todos los `Rigidbody` dentro de un radio configurable (`radius`) en el momento de su instanciación (`Start()`).

---

### NPCs / IA

#### `Health.cs`
**Ruta:** `Assets/#Xtra/Health.cs`

Gestiona la salud de los NPCs. Cuando `CurrentHealth < 0` activa la animación `"Death"` y destruye los componentes que permiten el movimiento (`AICharacterControl`, `ThirdPersonCharacter`, `Rigidbody`, `CapsuleCollider`, `NavMeshAgent`) antes de activar `AutoDestroy`.

| Campo | Tipo | Descripción |
|---|---|---|
| `CurrentHealth` | `float` | Vida actual del NPC |
| `DeathPrefab` | `GameObject` | Prefab opcional instanciado al morir |
| `Pos` | `Transform` | Posición para instanciar el prefab de muerte |

---

#### `AICharacterControl.cs`
**Ruta:** `Assets/#Xtra/Standard Assets/Characters/ThirdPersonCharacter/Scripts/AICharacterControl.cs`

Controlador de IA basado en `NavMeshAgent`. En cada frame localiza al jugador por tag `"Player"` y le persigue. Delega el movimiento físico en `ThirdPersonCharacter`.

| Propiedad | Tipo | Descripción |
|---|---|---|
| `agent` | `NavMeshAgent` | Agente de navegación |
| `character` | `ThirdPersonCharacter` | Componente de movimiento |
| `target` | `Transform` | Objetivo actual (jugador) |

---

#### `TestSpawner.cs`
**Ruta:** `Assets/#Xtra/TestSpawner.cs`

Instancia un prefab (`Spawnee`) en la posición `SpawnPos`. Se invoca desde código o desde un evento de UI/Timeline.

---

### Vehículos

#### `CarManager.cs`
**Ruta:** `Assets/#Xtra/CarManager.cs`

Gestiona la entrada y salida del vehículo conducible. Al entrar (trigger con tag `"Player"`) desactiva la cámara y el personaje del jugador, activa la cámara del coche y habilita los componentes del vehículo (`CarController`, `CarUserControl`, `CarAudio`). Al pulsar `J` el proceso se invierte y el jugador reaparece en `ExitPos`.

| Campo | Tipo | Descripción |
|---|---|---|
| `MainCam` | `GameObject` | Cámara principal del jugador |
| `Player` | `GameObject` | GameObject del jugador |
| `CarCamera` | `GameObject` | Cámara del vehículo |
| `Car` | `GameObject` | GameObject del vehículo |
| `ExitPos` | `Transform` | Punto de aparición al salir del coche |
| `FalsePlayer` | `GameObject` | Representación visual del jugador dentro del coche |
| `Radio` | `CarRadio` | Referencia a la radio del coche |

---

#### `CarDamage.cs`
**Ruta:** `Assets/CarDamage.cs`

Aplica 10 000 puntos de daño a cualquier NPC con tag `"Enemy"` que entre en el trigger del vehículo (atropello).

---

#### `CarRadio.cs`
**Ruta:** `Assets/#Xtra/CarRadio.cs`

Alterna el volumen de la radio del coche al pulsar `T`. Cicla entre 0 (apagado) y 0.5 (encendido).

---

#### `CarCam.cs`
**Ruta:** `Assets/#Tools/CarCamera/CarCam.cs`

Cámara que sigue al vehículo con suavizado. Se desempareja de la jerarquía en `Start()` para moverse independientemente. En `FixedUpdate()` interpola la posición con `Lerp` y la rotación con `Slerp` siguiendo el vector de velocidad del `Rigidbody` del coche.

---

### Cámara

#### `OrbitCamera.cs`
**Ruta:** `Assets/#Tools/Resources/Scripts/OrbitCamera.cs`

Cámara en tercera persona. En estado normal sigue a `targetOrbit` con campo de visión de 60 grados. Al mantener `Fire2` (apuntar) cambia a `targetShoot` con campo de visión de 20 grados (zoom) y dibuja la mira (`crosshairTexture`) en pantalla.

| Campo | Tipo | Descripción |
|---|---|---|
| `_distance` | `float` | Distancia al objetivo |
| `_xSpeed` / `_ySpeed` | `float` | Sensibilidad horizontal / vertical |
| `_MinY` / `_MaxY` | `float` | Límites verticales de la cámara |
| `crosshairTexture` | `Texture2D` | Textura de la mira |
| `crosshairScale` | `float` | Escala de la mira en pantalla |

---

### HUD y economía

#### `GUISystem.cs`
**Ruta:** `Assets/#Tools/Resources/Scripts/GUISystem.cs`

HUD clásico (IMGUI) que muestra:
- Dinero del jugador (`MoneyStyle`) al entrar en zona trigger `"MoneyShow"`.
- Coste de un arma (`CostStyle`) al acercarse.
- Munición actual / cargadores restantes (`WeaponInfo`) cuando hay un arma equipada.

---

#### `MakeMoney.cs`
**Ruta:** `Assets/#Tools/Resources/Scripts/MakeMoney.cs`

Pickup de dinero. Al tocarlo el jugador suma `Value` a `GUISystem.Money` y el objeto se destruye.

---

### Utilidades

#### `AutoDestroy.cs`
Destruye el GameObject transcurridos `TimeToDestroy` segundos desde `Start()`. Usado en efectos de partículas, cuerpos de NPCs muertos y proyectiles.

#### `Footsteps.cs`
Reproduce un `AudioSource` al entrar en un trigger de suelo. Úsalo en los pies del personaje con colliders de pequeño radio.

#### `PositionKeep.cs`
Fuerza la posición y rotación de `GameOb` para que coincidan con `PositionToKeep` y `RotationToKeep` en cada frame. Útil para anclar objetos decorativos a un punto de referencia. Se desactiva pulsando `N`.

#### `MouseLock.cs`
Bloquea el cursor al iniciar la escena. Documentado en detalle en la sección [Jugador](#jugador).

#### `Slomo.cs` / `TargetFinder.cs`
Scripts stub listos para implementar cámara lenta y búsqueda de objetivos respectivamente.

---

### Herramientas de editor

#### `AutoGrass.cs`
**Ruta:** `Assets/#Tools/Editor/AutoGrass.cs`  
**Menú:** `Doctrina → AutoGrass`

Ventana de editor que pinta automáticamente hierba sobre un terreno Unity en función de su mapa de texturas. Permite seleccionar el índice de textura de origen, el índice del detalle de hierba destino, la densidad y un rango de aleatorización.

---

## Sistema de IA (NPCs)

El sistema de IA se apoya en tres componentes encadenados:

```
NavMeshAgent  ←→  AICharacterControl  ←→  ThirdPersonCharacter
```

1. **`NavMeshAgent`** calcula el camino óptimo hacia el jugador en cada frame mediante el NavMesh pregenerado de la escena.
2. **`AICharacterControl`** consulta la distancia restante del agente: si es mayor que `stoppingDistance` pasa `agent.desiredVelocity` a `ThirdPersonCharacter.Move()`; si no, pasa `Vector3.zero`.
3. **`ThirdPersonCharacter`** traduce ese vector de movimiento en fuerzas físicas sobre el `Rigidbody` del NPC y actualiza los parámetros del `Animator`.

### Ciclo de vida del NPC

```
Spawn (TestSpawner) → Persecución (AICharacterControl) → Daño (Weapons raycast / CarDamage)
  → Health.CurrentHealth < 0 → Animación "Death" → Destrucción de componentes → AutoDestroy
```

### Generación del NavMesh

El NavMesh debe regenerarse desde **Window → AI → Navigation → Bake** cada vez que se modifique la geometría de la escena (edificios, obstáculos, terreno). Asegúrate de marcar los objetos estáticos como `Navigation Static` en el Inspector.

---

## Sistema de físicas

### Jugador
El jugador usa un `CharacterController` gestionado por `ThirdPersonCharacter` (Standard Assets). El movimiento se calcula en espacio de mundo y aplica gravedad personalizada.

### NPCs
Los NPCs poseen `Rigidbody` + `CapsuleCollider`. Al morir, ambos componentes se destruyen para evitar que el cadáver interactúe con la física de forma inesperada.

### Proyectiles (granadas y cohetes)
Implementados como `Rigidbody` prefabs con `GrenadesRocket`. La física del motor (gravedad, colisiones) se encarga del vuelo. Al impactar, `Explosion.cs` aplica `AddExplosionForce()` en esfera de radio `radius`.

### Vehículos
Basados en `CarController` (Standard Assets Vehicles). Usa cuatro `WheelColliders` con suspensión configurable. `CarSelfRighting` endereza el coche si vuelca. `SkidTrail` y `WheelEffects` generan marcas y partículas de derrape.

---

## Sistema de rendering (HDRP)

El proyecto usa **High Definition Render Pipeline (HDRP) 17.3.0** para Unity 6.

### Perfiles de calidad

| Perfil | Ruta | Descripción |
|---|---|---|
| HDRP Performant | `Assets/Settings/HDRP Performant.asset` | Ajustes de bajo consumo (hardware modesto) |
| HDRP Balanced | `Assets/Settings/HDRP Balanced.asset` | Equilibrio rendimiento/calidad (por defecto) |
| HDRP High Fidelity | `Assets/Settings/HDRP High Fidelity.asset` | Máxima calidad visual |

### Cielo y niebla

Configurado en `Assets/Settings/SkyandFogSettingsProfile.asset`. HDRP usa **Physical Sky** (cielo físico con sol, luna y atmósfera) y **Volumetric Fog** para niebla realista. Ajusta los parámetros en el Volume de la escena (componente `Sky and Fog Volume`).

### Iluminación

- Se recomienda usar **Baked Global Illumination** con el Enlighten Bake Backend para la iluminación estática de la escena.
- Las luces dinámicas (antorchas, coches) deben configurarse como `Mixed` o `Realtime`.
- Los **Reflection Probes** deben colocarse en interiores y bajo soportales para capturar reflexiones locales.

### Materiales

Los materiales del personaje `Ch20_nonPBR.fbx` incluyen mapas de Diffuse, Glossiness, Normal y Specular. Para integrarlos correctamente en HDRP convierte los materiales al shader `HDRP/Lit` desde **Edit → Rendering → Materials → Convert All Built-in Materials to HDRP**.

---

## Sistema de audio

| Script | Componente | Descripción |
|---|---|---|
| `Weapons.cs` | `AudioSource` en el jugador | Disparo (`WeaponSound`), recarga (`ReloadSound`), arma vacía (`WeaponEmpty`) |
| `GrenadesRocket.cs` | `AudioSource` en el prefab de explosión | Sonido de explosión con filtro de paso bajo por distancia |
| `Footsteps.cs` | `AudioSource` en el trigger del pie | Sonido de paso al tocar el suelo |
| `CarRadio.cs` | `AudioSource` en el coche | Radio del vehículo (volumen 0 / 0.5) |
| `CarAudio.cs` (Standard Assets) | `AudioSource` múltiple | Motor del vehículo (aceleración, marcha baja/alta) |

### Mixer recomendado

Crea un **Audio Mixer** con grupos: `Master → SFX → Weapons`, `Master → SFX → Footsteps`, `Master → Vehicles`, `Master → Music`. Asigna cada `AudioSource` al grupo correspondiente para controlar volúmenes globales de forma independiente.

---

## Sistema de input

El proyecto incluye dos sistemas de input en paralelo:

| Sistema | Archivo de configuración | Uso actual |
|---|---|---|
| Input System (nuevo) | `Assets/InputSystem_Actions.inputactions` | Disponible, pendiente de integración completa |
| Input Manager (legacy) | `ProjectSettings/InputManager.asset` | Activo: `Fire1`, `Fire2`, `Jump`, `Mouse X`, `Mouse Y`, `Horizontal`, `Vertical` |

Los scripts actuales (`Weapons.cs`, `PlayerMotor.cs`, etc.) usan la API legacy `Input.GetButton()` / `Input.GetKey()`. La migración al nuevo Input System requeriría sustituir esas llamadas por acciones del `InputSystem_Actions`.
