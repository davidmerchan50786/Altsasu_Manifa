// Assets/Scripts/Core/ArchitectureIndex.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ÍNDICE DE ARQUITECTURA — Altsasu Manifa v5
//  Leer este archivo es suficiente para entender cómo está organizado el juego.
//
//  ┌─────────────────────────────────────────────────────────────────────────
//  │  REGLAS DE CAPAS (obligatorias)
//  │
//  │  Core ──▶ nadie depende de él salvo las otras capas
//  │  World ──▶ solo conoce Core (sin Gameplay, sin UI)
//  │  Gameplay ──▶ conoce Core y puede escuchar eventos de World
//  │  UI ──▶ conoce Core, escucha eventos de Gameplay y World
//  │  Audio ──▶ conoce Core, escucha eventos de todas las capas
//  │
//  │  Comunicación entre capas: ServiceLocator + eventos estáticos (EventManager)
//  └─────────────────────────────────────────────────────────────────────────
//
//  ══ CAPA CORE ════════════════════════════════════════════════════════════
//  AltsasuCore          — Coordinador de boot (4 fases). Singleton central.
//  ServiceLocator       — Registro y acceso a interfaces de servicio.
//  EventManager         — Índice centralizado de todos los eventos del juego.
//  SingletonMono<T>     — Base class Singleton seguro para MonoBehaviour.
//  AlsasuaLogger        — Logger categorizado (sin Debug.Log directo en prod).
//  GeoDataAlsasua       — Constantes geográficas de Alsasua (OX, OZ, alturas).
//  SceneBootstrapper    — Bootstrap de emergencia para Play sin prefabs.
//  ExtensionesSeguras   — Extension methods de nulidad y arrays.
//
//  Interfaces de servicio (ServiceLocator):
//  IWantedSystem        — Nivel de búsqueda policial (0-5★). GM implementa.
//  IEconomyService      — Dinero y puntuación del jugador. GM implementa.
//  ISpawnService        — Spawn/destrucción de entidades dinámicas. GM implementa.
//
//  Interfaces de contrato (entre capas):
//  IDamageable          — Todo lo que puede recibir daño.
//  IAgente              — Contrato NPC para SistemaIA.
//  IInteractable        — Objetos con los que el jugador puede interactuar.
//
//  Persistencia:
//  SistemaGuardado      — Guardado JSON en 3 slots + auto-guardado cada 3 min.
//  SistemaLogros        — Logros desbloqueables con notificación en HUD.
//  SistemaOpciones      — Volumen, calidad, controles (PlayerPrefs).
//  SistemaDiagnostico   — Health-check de sistemas en runtime.
//
//  ══ CAPA WORLD ════════════════════════════════════════════════════════════
//  GeneradorMundoOSM    — Carga buildings/roads/trees JSON → indexa en SistemaZonas.
//  SistemaZonas         — Zone streaming: activa/destruye meshes en radio al jugador.
//  SistemaTerreno       — Terreno Unity: pintar carreras, splatmaps.
//  SistemaNavMesh       — Horneado dinámico de NavMesh al cargar zonas.
//  SistemaEdificiosAAA  — Construcción de edificios con calidad AAA (HDRP + LOD).
//  SistemaSueloAAA      — Suelo procedural con splatmaps y detalles.
//  SistemaClima         — Ciclo día/noche, lluvia, niebla.
//  AlsasuaTreeStreamer  — Streaming de árboles con pool de instancias.
//  MeshBuilder          — Utilidades de generación de meshes (banda, edificio).
//  GestorMaterialesAlsasua — Registro y lookup de materiales HDRP compartidos.
//
//  ══ CAPA GAMEPLAY ═════════════════════════════════════════════════════════
//
//  NPC/
//  NPCBase              — Base abstract de NPC: NavMesh, animator, rotación.
//  NPCCivil             — Civil: Idle → Caminar → Huir al disparo.
//  PoliciaForalIA       — Policía: Patrulla → Sospecha → Persigue → Ataca.
//  NPCGuard             — Guardia estático de zona (ejemplo de NPC nuevo).
//  SistemaIA            — Registro global de IAgente + alertas de zona.
//  SistemaDeteccionIA   — Batch de raycasts de visión (20Hz, Job System).
//
//  Combat/
//  SistemaDisparo       — Raycast disparo + pool de decals + dispersión dinámica.
//  SistemaArmasExtendido — Spray, Tirachinas, Molotov, Bandera, BombaLapa.
//  SistemaBombas        — Colocación y detonación de bombas con timer.
//  SistemaBarricadas    — Barricadas físicas colocables en calzada.
//  SistemaDestruccion   — Explosión física de coches + residuos.
//  SistemaExplosion     — VFX + daño radial de explosiones.
//  SistemaImpactos      — Decals de impacto por tipo de material.
//  SistemaGrafitis      — Pintadas y pegatinas en superficies del mundo.
//
//  Missions/
//  SistemaMisiones      — Engine de misiones: lista de objetivos + condiciones.
//  MisionesAltsasua     — Cadena M03-M12 (pintada → manifestación → clímax).
//  MisionesSec          — Misiones secundarias opcionales (MS01-MS05).
//  SistemaManifestacion — Multitud boids: manifestantes pacíficos + disturbios.
//  SistemaApoyoPopular  — Métricas: apoyo (0-100), honor, paranoia ciudadana.
//
//  Vehicles/
//  VehiculoBase         — Base abstract: Rigidbody, IDamageable, velocidadMax.
//  VehiculoNPC          — Tráfico IA con waypoints.
//  ControladorVehiculoJugador — Vehículo del jugador: WheelColliders, cámara, E.
//  ControladorJugador   — Movimiento tercera persona + Spring Arm + salud.
//  SistemaTrafico       — Spawn y gestión de tráfico IA.
//
//  ══ CAPA UI ═══════════════════════════════════════════════════════════════
//  GameManagerAltsasua  — Wanted level, spawn policía/enemigos, economía, respawn.
//                         Implementa: IWantedSystem, IEconomyService, ISpawnService.
//  HUDCanvas            — HUD uGUI: vida, dinero, wanted, apoyo, minimapa, misión.
//  HUDManifestacion     — HUD específico de la manifestación (barra apoyo grande).
//  MenuPausa            — Menú pause con opciones básicas.
//  MenuPrincipal        — Pantalla de inicio y continuar partida.
//  SistemaPolish        — Post-proceso, shake de cámara, viñeta de daño.
//  SistemaTutorial      — Mensajes de tutorial contextuales.
//
//  ══ CAPA AUDIO ════════════════════════════════════════════════════════════
//  AudioManager         — Pool 32 AudioSources, 4 categorías, clips sintéticos.
//  SistemaReverbZonas   — Reverb HDRP por zona (interior/exterior/cueva).
//  ConfiguradorAssetsAAA — Configuración de assets de audio y VFX.
//
// ═══════════════════════════════════════════════════════════════════════════
//
//  DEUDA TÉCNICA IDENTIFICADA (no tocada — preservar comportamiento):
//  - GameManagerAltsasua.SembrarArboles → debería moverse a SistemaVegetacion
//  - GameManagerAltsasua contiene HUD legacy (Text referencias) → debería
//    quedar solo en HUDCanvas
//  - SistemasSimulacion, SistemasInfraestructura → stubs sin clasificar aún
//  - SistemaChunks → stub obsoleto (funcionalidad en SistemaZonas)
//  - PuntosAlsasua en MisionesAltsasua → duplica GeoDataAlsasua.HerrikoPlaza etc.
//
// ═══════════════════════════════════════════════════════════════════════════

// Este archivo no contiene código ejecutable — es documentación viva de la arquitectura.
// La clase vacía es necesaria para que Unity lo compile y lo incluya en el build.
internal static class ArchitectureIndex { }
