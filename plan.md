# Plan de Desarrollo – Altsasu Manifa

## Estado del Proyecto

Juego de acción en tercera persona ambientado en el escenario real de Alsasua (Navarra), construido con Unity HDRP.

---

## Completado ✅

### Escenario
- [x] Escenario de Alsasua importado desde nube de puntos CloudCompare (`.obj`): suelo, puntos en superficie y puntos fuera de superficie
- [x] Textura de color del escenario (`Alsasua_Color.png`)
- [x] Escena principal `OutdoorsScene.unity`
- [x] Escenas de demo y prueba (`Starter Demo Test`, `Free Version Method 2`)

### Personaje Jugador
- [x] Modelo del personaje (`jill.fbx`) con texturas difusa, normal y especular
- [x] Movimiento del jugador (`PlayerMotor.cs`)
- [x] Estadísticas del jugador (`PlayerStats.cs`)
- [x] Sistema de salud (`Health.cs`)
- [x] Pasos al caminar (`Footsteps.cs`)
- [x] Bloqueo de ratón (`MouseLock.cs`)
- [x] Control en tercera persona (`ThirdPersonCharacter.cs`, `ThirdPersonUserControl.cs`)
- [x] Animaciones de locomoción (estilo GTA, correr, idle)
- [x] Intercambio de hombro de cámara (`ShoulderSwap.cs`)
- [x] Mochila del jugador (modelo 3D con texturas)

### Armas
- [x] Sistema de armas (`Weapons.cs`, `WeaponTake.cs`, `WeaponEquip.cs`)
- [x] Modelos 3D: Beretta Pigeon S, CA94, M4, Mp5, RPG, Granada, m249, Escopeta, Silenciador
- [x] Prefabs: Granada, Cohete RPG, Explosión (suelo y aérea)
- [x] Sistema de granadas y cohetes (`GrenadesRocket.cs`)
- [x] Explosiones (`Explosion.cs`)
- [x] Marcador de impacto (Hitmarker Canvas prefab)
- [x] Decals de agujeros de bala (`BulletHoleDecal.cs`)
- [x] Flash de boca de fuego (MuzzleFlash)
- [x] Sonidos de armas (disparo silencioso, recarga M4)

### Vehículos
- [x] Modelo de coche deportivo (Best Sports CARS Pro)
- [x] Controlador de coche (`CarController.cs`, `CarUserControl.cs`)
- [x] Daño del coche (`CarDamage.cs`)
- [x] Audio del coche (`CarAudio.cs`, `Engine_07.wav`)
- [x] Cámara del coche (`CarCam.cs`)
- [x] Auto-enderezado (`CarSelfRighting.cs`)
- [x] Radio del coche (`CarRadio.cs`)
- [x] Gestor de coches (`CarManager.cs`)
- [x] Efectos de ruedas, luces de freno, huellas de derrape

### Enemigos / IA
- [x] Modelo zombie (`Z Walker.prefab`) con animaciones (correr, morir)
- [x] Prefab de muerte (`Death Prefab.prefab`)
- [x] Spawner de prueba (`TestSpawner.cs`)
- [x] Buscador de objetivos (`TargetFinder.cs`)
- [x] Control IA de personaje (`AICharacterControl.cs`)

### Cámara
- [x] Cámara en órbita (`OrbitCamera.cs`)
- [x] Anticipación de cámara (`LookAhead.cs`)

### Interfaz (GUI)
- [x] Sistema GUI (`GUISystem.cs`)
- [x] Mira / Crosshair
- [x] Sistema de dinero (`MakeMoney.cs`)
- [x] Modelo de moneda (MoneyModel)

### Otros
- [x] Sistema de entrada (Input System Actions)
- [x] Efecto de cámara lenta (`Slomo.cs`)
- [x] Auto-destrucción de objetos (`AutoDestroy.cs`)
- [x] Mantenimiento de posición (`PositionKeep.cs`)
- [x] Estatua de caballo (modelo 3D con texturas)
- [x] Cielos (Skyboxes)
- [x] Sistema de partículas (humo, agua, fuego, afterburner)
- [x] Perfiles de post-procesado HDRP

---

## Pendiente 🔲

### Escenario y Mundo
- [ ] Poblar el escenario con edificios, calles y elementos urbanos de Alsasua
- [ ] Añadir colisiones y NavMesh al escenario importado de CloudCompare
- [ ] Iluminación y skybox definitivos para exterior diurno/nocturno
- [ ] Optimización de la malla del escenario (LODs, oclusión)

### Jugabilidad
- [ ] Sistema de misiones / objetivos
- [ ] Sistema de inventario de armas completo
- [ ] Apuntado en primera persona (ADS - Aim Down Sights)
- [ ] Sistema de cobertura
- [ ] Animaciones de recarga y disparo conectadas al personaje

### Enemigos / IA
- [ ] Comportamiento de IA más complejo (patrullas, alarmas, persecución)
- [ ] Más tipos de enemigos
- [ ] Sistema de respawn de enemigos

### Vehículos
- [ ] IA de conductores
- [ ] Más modelos de vehículos

### Sonido
- [ ] Música ambiental y efectos de sonido del entorno
- [ ] Sonidos de pasos según material del suelo
- [ ] Sonidos de impacto por tipo de superficie

### Interfaz
- [ ] Menú principal
- [ ] Pantalla de pausa
- [ ] HUD completo (salud, munición, minimapa)
- [ ] Pantalla de game over / victoria

### Técnico
- [ ] Optimización general de rendimiento
- [ ] Gestión de escenas (carga/descarga)
- [ ] Sistema de guardado
