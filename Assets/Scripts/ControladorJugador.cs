// Assets/Scripts/ControladorJugador.cs
// Controlador jugador en TERCERA PERSONA — Spring Arm · Mixamo o Cuerpo procedural
//
// PERSONAJE MIXAMO (recomendado):
//   1. Ve a mixamo.com (cuenta Adobe gratuita)
//   2. Descarga un personaje en "FBX for Unity"
//   3. Descarga estas animaciones (FBX for Unity, sin skin):
//        · Idle           → guarda como Anim_Idle.fbx
//        · Walking        → guarda como Anim_Andar.fbx
//        · Running        → guarda como Anim_Correr.fbx
//        · Crouching Idle → guarda como Anim_Agachado.fbx
//        · Crouching Walk → guarda como Anim_AgachadoAndar.fbx
//        · Jump           → guarda como Anim_Saltar.fbx
//        · Rifle Aiming Idle → guarda como Anim_Apuntar.fbx
//        · Dying          → guarda como Anim_Morir.fbx
//   4. Importa todo en Assets/Personajes/
//   5. Crea un Animator Controller con los parámetros que se listan abajo
//   6. Arrastra el prefab del personaje y el controller al Inspector de este componente
//
// PARÁMETROS DEL ANIMATOR CONTROLLER:
//   Float:   VelocidadMovimiento  (0=parado · 0.5=andar · 1=correr)
//   Bool:    EstaAgachado
//   Bool:    EstaApuntando
//   Bool:    EstaEnSuelo
//   Trigger: Saltar
//   Trigger: Disparar
//   Trigger: Morir
//
// Si no hay prefab asignado → se genera el cuerpo procedural de bloques (fallback).
//
// Controles:
//   WASD       – Mover           SHIFT – Correr
//   SPACE      – Saltar          C     – Agacharse
//   RMB (hold) – Apuntar (zoom)  LMB   – Disparar
//   F          – Colocar bomba   G     – Detonar última
//   ESC        – Liberar cursor

using UnityEngine;
using UnityEngine.InputSystem;
#if CESIUM_FOR_UNITY
using CesiumForUnity;
#endif

[RequireComponent(typeof(CharacterController))]
public class ControladorJugador : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════
    //  MOVIMIENTO
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ MOVIMIENTO ═══")]
    [Tooltip("Velocidad de desplazamiento caminando (m/s).")]
    [SerializeField] private float velocidadAndar    = 4.0f;
    [Tooltip("Velocidad de desplazamiento corriendo con SHIFT (m/s).")]
    [SerializeField] private float velocidadCorrer   = 8.5f;
    [Tooltip("Velocidad de desplazamiento en posición agachada (m/s).")]
    [SerializeField] private float velocidadAgachar  = 2.0f;
    [Tooltip("Impulso vertical al saltar. Determina la altura máxima del salto.")]
    [SerializeField] private float fuerzaSalto       = 5.5f;
    [Tooltip("Aceleración gravitatoria (m/s²). Valor negativo. -20 da saltos más 'pesados'.")]
    [SerializeField] private float gravedad          = -20f;
    [Tooltip("Factor de suavizado de la inercia horizontal (Lerp t por segundo). Mayor valor = arranque/parada más bruscos.")]
    [SerializeField] private float suavidadMovimiento = 9f;
    [Tooltip("Factor de suavizado de la rotación del jugador (Lerp t por segundo). Mayor valor = giro más rápido.")]
    [SerializeField] private float suavidadGiro      = 14f;

    // ═══════════════════════════════════════════════════════════════════════
    //  CÁMARA TERCERA PERSONA (Spring Arm)
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ CÁMARA 3ª PERSONA ═══")]
    [Tooltip("Distancia del Spring Arm en modo exploración normal (m).")]
    [SerializeField] private float distanciaBrazo   = 4.5f;    // dist. normal
    [Tooltip("Distancia del Spring Arm al apuntar con RMB, más cercana para mejor puntería (m).")]
    [SerializeField] private float distanciaApuntar = 2.8f;    // dist. al apuntar (RMB)
    [Tooltip("Altura del pivot de la cámara respecto a los pies del jugador (m). 1.5 ≈ altura del hombro.")]
    [SerializeField] private float alturaHombro     = 1.5f;    // pivot sobre el jugador
    [Tooltip("Sensibilidad horizontal del ratón (°/px × 0.05). 2.8 ≈ 0.14°/px.")]
    [SerializeField] private float sensibilidadX    = 2.8f;
    [Tooltip("Sensibilidad vertical del ratón (°/px × 0.05). 2.2 ≈ 0.11°/px.")]
    [SerializeField] private float sensibilidadY    = 2.2f;
    [Tooltip("Límite inferior del ángulo vertical de la cámara (°). Negativo = mirar hacia abajo.")]
    [SerializeField] private float limVertMin       = -25f;
    [Tooltip("Límite superior del ángulo vertical de la cámara (°). Positivo = mirar hacia arriba.")]
    [SerializeField] private float limVertMax       =  65f;
    [Tooltip("Factor de suavizado del Spring Arm (Lerp t por segundo). Mayor valor = cámara más pegada al objetivo.")]
    [SerializeField] private float suavidadCamara   = 12f;
    [Tooltip("Campo de visión (FOV) en modo exploración (°). Rango típico 60–75°.")]
    [SerializeField] private float fovNormal        = 68f;
    [Tooltip("Campo de visión (FOV) al apuntar con RMB (°). Menor valor = más zoom.")]
    [SerializeField] private float fovApuntando     = 48f;
    [Tooltip("Ángulo vertical inicial de la cámara al arrancar (°). 5° = casi horizontal, ligeramente hacia abajo.")]
    [SerializeField] private float anguloVertDefault =  5f;    // 5° = casi horizontal

    // ═══════════════════════════════════════════════════════════════════════
    //  SALUD
    // ═══════════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════════
    //  PERSONAJE MIXAMO
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ PERSONAJE MIXAMO ═══")]
    [Tooltip("Prefab FBX descargado de mixamo.com.\n" +
             "Si está vacío se genera el cuerpo procedural de bloques (para prototipado).")]
    [SerializeField] private GameObject prefabPersonaje;

    [Tooltip("Animator Controller con los estados de movimiento.\n" +
             "Parámetros Float: VelocidadMovimiento (0=parado, 0.5=andar, 1=correr)\n" +
             "Parámetros Bool: EstaAgachado, EstaApuntando, EstaEnSuelo\n" +
             "Triggers: Saltar, Disparar, Morir")]
    [SerializeField] private RuntimeAnimatorController controladorAnimaciones;

    [Tooltip("Offset de posición del modelo respecto al CharacterController (ajustar si flota o se hunde)")]
    [SerializeField] private Vector3 offsetModelo = Vector3.zero;

    [Tooltip("Escala del modelo (algunos FBX de Mixamo vienen a 0.01 — ponlo a 1 si es correcto)")]
    [SerializeField] private float escalaModelo = 1f;

    // ═══════════════════════════════════════════════════════════════════════
    //  SALUD
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ SALUD ═══")]
    [Tooltip("Puntos de vida actuales del jugador. Llega a 0 → muerte.")]
    [SerializeField] private int vida    = 100;
    [Tooltip("Puntos de vida máximos. Curar() no puede superar este valor.")]
    [SerializeField] private int vidaMax = 100;

    public int  Vida       => vida;
    public bool EstaMuerto => vida <= 0;
    // BUG 15b FIX: exponer la cámara de 3ª persona para SistemaExplosion.SacudirCamara().
    // ConfigurarCamara() desacopla camaraTP del jugador → GetComponentInChildren<Camera>()
    // siempre devuelve null desde fuera. Esta propiedad da acceso directo sin búsqueda.
    public Camera CamaraTP => camaraTP;

    // Estado de movimiento expuesto para SistemaDisparo (dispersión dinámica) y HUDJugador
    public bool  EstaAgachadoP   => estaAgachado;
    public bool  EstaCorriendoP  => estaCorriendo;
    public bool  EstaEnSueloP    => estaEnSuelo;
    public bool  EstaApuntandoP  => modoApuntar;
    /// <summary>Velocidad horizontal real del CharacterController (m/s).</summary>
    public float VelocidadHoriz  => velHoriz.magnitude;

    // Propiedades para HUDJugador (Canvas UI)
    public int   VidaMax     => vidaMax;
    public float RatioVida   => (float)vida / Mathf.Max(1, vidaMax);
    /// <summary>Intensidad del flash de daño (0 = sin flash, >0 = activo). Decrece por sí solo.</summary>
    public float FlashDano   => timerDano;
    /// <summary>Texto de estado actual del jugador para el HUD.</summary>
    public string TextoEstado => EstaMuerto                          ? "─ MUERTO ─"
                               : estaAgachado                        ? "AGACHADO"
                               : modoApuntar                         ? "APUNTANDO"
                               : estaCorriendo                       ? "CORRIENDO"
                               : inputMovimiento.magnitude > 0.05f   ? "ANDANDO"
                               : "EN GUARDIA";

    // ═══════════════════════════════════════════════════════════════════════
    //  ESTADO INTERNO
    // ═══════════════════════════════════════════════════════════════════════

    private CharacterController cc;
    private SistemaDisparo      sistemaDisparo;
    private SistemaBombas       sistemaBombas;

    // Cámara
    private Camera    camaraTP;
    private Transform pivotCam;         // sigue al jugador a alturaHombro

    private float anguloH = 0f;
    private float anguloV;              // inicializado con anguloVertDefault

    // Movimiento
    private Vector2 inputMovimiento;
    private Vector3 velHoriz;
    private Vector3 velVert;
    private float   yawJugador = 0f;

    // Estados
    private bool estaEnSuelo;
    private bool estaAgachado;
    private bool estaCorriendo;
    private bool modoApuntar;

    // Daño visual (flash pantalla)
    private float timerDano = 0f;

    // Pasos de audio
    private float _timerPaso = 0f;

    // ── Animator Mixamo ──────────────────────────────────────────────────
    // Referencia al Animator del personaje instanciado (null si se usa cuerpo procedural)
    private Animator animPersonaje;

    // IDs de parámetros del Animator cacheados como hash → evita string lookup cada frame.
    // Animator.StringToHash() es O(1) y el resultado es un int constante.
    private static readonly int AnimVelocidad  = Animator.StringToHash("VelocidadMovimiento");
    private static readonly int AnimAgachado   = Animator.StringToHash("EstaAgachado");
    private static readonly int AnimApuntando  = Animator.StringToHash("EstaApuntando");
    private static readonly int AnimEnSuelo    = Animator.StringToHash("EstaEnSuelo");
    private static readonly int AnimSaltar     = Animator.StringToHash("Saltar");
    private static readonly int AnimDisparar   = Animator.StringToHash("Disparar");
    private static readonly int AnimMorir      = Animator.StringToHash("Morir");

    // BUG 2 FIX: lista de materiales creados con new Material() para destruirlos
    // explícitamente en OnDestroy() — Unity no los libera automáticamente.
    private readonly System.Collections.Generic.List<Material> _matsCreados =
        new System.Collections.Generic.List<Material>();

    // PERF FIX: LayerMask calculado UNA sola vez en Awake() y cacheado.
    // LayerMask.GetMask() hace string lookups en cada llamada → en LateUpdate (60fps)
    // eran 60 lookups/seg innecesarios. Ahora es un simple int.
    private int _maskSpringArm;

    // ═══════════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ═══════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        cc             = GetComponent<CharacterController>();
        sistemaDisparo = GetComponent<SistemaDisparo>();
        sistemaBombas  = GetComponent<SistemaBombas>();

        cc.height = 1.8f;
        cc.center = new Vector3(0f, 0.9f, 0f);

        yawJugador = transform.eulerAngles.y;
        anguloH    = yawJugador;
        anguloV    = anguloVertDefault;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        // PERF FIX: cachear la LayerMask aquí (una vez) para ActualizarCamara() en LateUpdate
        _maskSpringArm = ~LayerMask.GetMask("Player", "Ignore Raycast");
    }

    private void Start()
    {
        // Start() se ejecuta DESPUÉS de todos los Awake() → SistemaDisparo ya
        // ha cacheado la cámara-hijo antes de que la desacoplemos aquí.
        ConfigurarCamara();
        CrearCuerpoJugador();

        // El jugador parte a Y=10 sobre el origen del GeoReference (Herriko Plaza, Alsasua).
        // El CharacterController + gravedad lo bajan hasta el SueloBase (Y=0) o hasta
        // los physics meshes de Cesium conforme se generan (~2-3 s).
        // No se necesita snap por raycast: ese enfoque era frágil cuando los tiles
        // aún no habían generado sus colliders, o cuando el primer hit era un tejado.
        velVert = Vector3.zero;
        AlsasuaLogger.Info("Jugador",
            "Inicio en Herriko Plaza (Alsasua) Y=10 — gravedad bajará al jugador al nivel de calle.");
    }

    private void Update()
    {
        if (EstaMuerto) return;
        LeerInput();
        GestionarCursor();
        MoverJugador();
        Gravedad();
        ActualizarAnimaciones();
        if (timerDano > 0f) timerDano -= Time.deltaTime;
    }

    private void LateUpdate()
    {
        if (!EstaMuerto && camaraTP != null) ActualizarCamara();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CONFIGURAR CÁMARA
    // ═══════════════════════════════════════════════════════════════════════

    private void ConfigurarCamara()
    {
        // Pivot — sigue automáticamente al jugador (es hijo)
        var pivotGO = new GameObject("_PivotCam");
        pivotCam = pivotGO.transform;
        pivotCam.SetParent(transform, false);
        pivotCam.localPosition = new Vector3(0f, alturaHombro, 0f);

        // Buscar cámara entre los hijos (CamaraFPS del setup) o Camera.main
        camaraTP = GetComponentInChildren<Camera>();
        if (camaraTP == null) camaraTP = Camera.main;
        if (camaraTP == null)
        {
            var camGO = new GameObject("CamaraTP");
            camaraTP  = camGO.AddComponent<Camera>();
            if (FindFirstObjectByType<AudioListener>() == null)
                camGO.AddComponent<AudioListener>();
            AlsasuaLogger.Warn("Jugador", "Cámara no encontrada — se creó CamaraTP.");
        }

        // Desacoplar del jugador para orbitar libremente.
        // Se re-parenta bajo CesiumGeoreference para que CesiumGlobeAnchor funcione.
#if CESIUM_FOR_UNITY
        var georef = Object.FindFirstObjectByType<CesiumForUnity.CesiumGeoreference>();
        camaraTP.transform.SetParent(georef != null ? georef.transform : null, worldPositionStays: true);
        // BUG 33 FIX: añadir CesiumGlobeAnchor a la cámara del jugador si no lo tiene ya.
        if (georef != null && camaraTP.GetComponent<CesiumForUnity.CesiumGlobeAnchor>() == null)
            camaraTP.gameObject.AddComponent<CesiumForUnity.CesiumGlobeAnchor>();
#else
        camaraTP.transform.SetParent(null, worldPositionStays: true);
#endif
        camaraTP.nearClipPlane = 0.3f;          // FIX: consistente con ControladorPostProcesado (ratio z-buffer 1:400k)
        camaraTP.farClipPlane  = 1_000_000f;   // 1000 km — Cesium renderiza tiles lejanos
        camaraTP.fieldOfView   = fovNormal;

        // Posición inicial detrás del jugador
        Quaternion rotInit = Quaternion.Euler(anguloV, anguloH, 0f);
        camaraTP.transform.position = pivotCam.position + rotInit * Vector3.back * distanciaBrazo;
        camaraTP.transform.LookAt(pivotCam.position);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PERSONAJE — MIXAMO O PROCEDURAL
    // ═══════════════════════════════════════════════════════════════════════

    private void CrearCuerpoJugador()
    {
        // ── Opción A: Personaje Mixamo ───────────────────────────────────────
        // FIX: FBX con fileIdsGeneration:2 puede deserializarse como tipo no-GameObject
        // aunque el campo sea [SerializeField] private GameObject. Envolvemos toda la
        // opción A en try-catch para caer al cuerpo procedural sin bloquear Start().
        if (prefabPersonaje != null)
        try
        {
            // Destruir modelo anterior si existía (p.ej. al reconfigurar en runtime)
            var anterior = transform.Find("_PersonajeMixamo");
            if (anterior != null) Destroy(anterior.gameObject);

            var go = Instantiate(prefabPersonaje, transform);
            go.name = "_PersonajeMixamo";
            go.transform.localPosition = offsetModelo;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale    = Vector3.one * escalaModelo;

            // Desactivar colisionadores del modelo — CharacterController gestiona las colisiones.
            // Sin esto el personaje puede teletransportarse o bloquearse en la geometría.
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                col.enabled = false;

            // Buscar Animator en el prefab (puede estar en la raíz o en un hijo)
            animPersonaje = go.GetComponent<Animator>()
                         ?? go.GetComponentInChildren<Animator>();

            if (animPersonaje != null)
            {
                // Asignar controller si está asignado en Inspector
                if (controladorAnimaciones != null)
                    animPersonaje.runtimeAnimatorController = controladorAnimaciones;

                // CRÍTICO: desactivar Root Motion — el CharacterController maneja el movimiento.
                // Con Root Motion activado, el Animator mueve el personaje directamente por su
                // curva de animación → conflicto con cc.Move() → deslizamiento o bloqueo.
                animPersonaje.applyRootMotion = false;

                // Culling Mode: solo animar cuando el personaje es visible en cámara (optimización).
                animPersonaje.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                AlsasuaLogger.Info("Jugador", $"✓ Personaje Mixamo '{prefabPersonaje.name}' instanciado con Animator.");
            }
            else
            {
                AlsasuaLogger.Warn("Jugador", "El prefab no tiene Animator. " +
                                  "Añade un Animator Controller al FBX en el Inspector de Import.");
            }

            return; // No crear el cuerpo procedural
        }
        catch (System.InvalidCastException ice)
        {
            AlsasuaLogger.Warn("Jugador",
                $"prefabPersonaje no es un GameObject válido (FBX sin Prefab asignado) — " +
                $"usando cuerpo procedural. Crea un Prefab desde el FBX en el Editor. " +
                $"Detalle: {ice.Message}");
            prefabPersonaje = null; // evitar reintentos en llamadas futuras
            // cae a Opción B ↓
        }

        // ── Opción B: Cuerpo procedural (fallback sin prefab) ───────────────
        if (transform.Find("_Cuerpo") != null) return;  // ya existe

        var raiz = new GameObject("_Cuerpo");
        raiz.transform.SetParent(transform, false);

        // Paleta de colores militares
        Color negro  = new Color(0.10f, 0.10f, 0.12f);   // uniforme oscuro
        Color verde  = new Color(0.18f, 0.21f, 0.16f);   // verde militar
        Color bota   = new Color(0.08f, 0.06f, 0.05f);   // cuero negro
        Color piel   = new Color(0.72f, 0.56f, 0.42f);   // tono piel
        Color metal  = new Color(0.18f, 0.18f, 0.20f);   // acero

        // ── Piernas ─────────────────────────────────────────────────────────
        Box(raiz, "PiernaI",  new Vector3(-0.13f, 0.44f, 0f),  new Vector3(0.19f, 0.70f, 0.20f), negro);
        Box(raiz, "PiernaD",  new Vector3( 0.13f, 0.44f, 0f),  new Vector3(0.19f, 0.70f, 0.20f), negro);
        Box(raiz, "BotaI",    new Vector3(-0.13f, 0.08f, 0.04f),new Vector3(0.21f, 0.15f, 0.32f), bota);
        Box(raiz, "BotaD",    new Vector3( 0.13f, 0.08f, 0.04f),new Vector3(0.21f, 0.15f, 0.32f), bota);

        // ── Pelvis y torso ──────────────────────────────────────────────────
        Box(raiz, "Pelvis",   new Vector3(0f, 0.84f, 0f),   new Vector3(0.48f, 0.28f, 0.22f), negro);
        Box(raiz, "Torso",    new Vector3(0f, 1.22f, 0f),   new Vector3(0.52f, 0.52f, 0.24f), negro);

        // ── Chaleco / armadura táctica (encima del torso) ───────────────────
        Box(raiz, "Chaleco",  new Vector3(0f, 1.24f, 0.04f),new Vector3(0.46f, 0.46f, 0.09f), verde);
        Box(raiz, "PlacaF",   new Vector3(0f, 1.28f, 0.09f),new Vector3(0.30f, 0.22f, 0.04f), new Color(0.25f,0.28f,0.22f));

        // ── Mochila táctica ─────────────────────────────────────────────────
        Box(raiz, "Mochila",  new Vector3(0f, 1.22f, -0.19f),new Vector3(0.34f, 0.40f, 0.18f), verde);

        // ── Brazos ──────────────────────────────────────────────────────────
        Box(raiz, "BrazoI",   new Vector3(-0.34f, 1.18f, 0f),new Vector3(0.15f, 0.46f, 0.16f), negro);
        Box(raiz, "BrazoD",   new Vector3( 0.34f, 1.18f, 0f),new Vector3(0.15f, 0.46f, 0.16f), negro);
        Sphere(raiz, "ManoI", new Vector3(-0.34f, 0.93f, 0f), 0.09f, piel);
        Sphere(raiz, "ManoD", new Vector3( 0.34f, 0.93f, 0f), 0.09f, piel);

        // ── Cuello y cabeza ─────────────────────────────────────────────────
        Sphere(raiz, "Cuello", new Vector3(0f, 1.55f, 0f), 0.09f, negro);
        Sphere(raiz, "Cabeza", new Vector3(0f, 1.68f, 0f), 0.13f, piel);

        // ── Casco ───────────────────────────────────────────────────────────
        var casco = Sphere(raiz, "Casco",  new Vector3(0f, 1.76f, 0f), 0.17f, verde);
        casco.transform.localScale = new Vector3(0.36f, 0.30f, 0.36f);  // achatado

        Box(raiz, "ViseraCasco", new Vector3(0f, 1.71f, 0.14f),
            new Vector3(0.30f, 0.05f, 0.08f), new Color(0.08f, 0.08f, 0.08f));

        // ── Rifle (mano derecha, visible al jugar) ───────────────────────────
        Box(raiz, "RifleCuerpo", new Vector3( 0.35f, 1.12f, 0.25f),
            new Vector3(0.06f, 0.09f, 0.55f), metal);
        Box(raiz, "RifleCañon",  new Vector3( 0.35f, 1.14f, 0.60f),
            new Vector3(0.03f, 0.03f, 0.22f), new Color(0.06f, 0.06f, 0.06f));
        Box(raiz, "RifleCargador",new Vector3(0.35f, 1.04f, 0.28f),
            new Vector3(0.04f, 0.12f, 0.06f), metal);
    }

    // Crea un Material compatible con URP (evita el magenta por shader incorrecto)
    // BUG 1 FIX: guard contra shader==null — si URP/Lit está stripeado de la build,
    //            intentar URP/Unlit y luego Standard antes de lanzar excepción.
    // BUG 2 FIX: method de instancia (no static) para poder añadir cada material
    //            a _matsCreados y destruirlos explícitamente en OnDestroy().
    private Material MatURP(Color color)
    {
        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard")
                  ?? Shader.Find("HDRP/Unlit") ?? Shader.Find("Standard")
                  ?? Shader.Find("Standard");

        if (shader == null)
        {
            AlsasuaLogger.Error("Jugador", "MatURP: ningún shader compatible encontrado. " +
                               "Incluye 'Universal Render Pipeline/Lit' en Always Included Shaders.");
            // Fallback de emergencia — el material saldrá magenta pero no lanzará excepción.
            shader = Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("UI/Default");
            if (shader == null) return null;
        }

        var mat = new Material(shader) { color = color };
        _matsCreados.Add(mat); // BUG 2 FIX: rastrear para destruir en OnDestroy()
        return mat;
    }

    // BUG 2 FIX: liberar los materiales del cuerpo al destruir el jugador.
    private void OnDestroy()
    {
        foreach (var m in _matsCreados)
            if (m != null) { if (Application.isPlaying) Object.Destroy(m); else Object.DestroyImmediate(m); }
        _matsCreados.Clear();
    }

    // Helpers para crear piezas del cuerpo (sin colisionador, con sombras)
    private GameObject Box(GameObject raiz, string n, Vector3 lp, Vector3 ls, Color c)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = n;
        go.transform.SetParent(raiz.transform, false);
        go.transform.localPosition = lp;
        go.transform.localScale    = ls;
        var r = go.GetComponent<Renderer>();
        r.material              = MatURP(c);
        r.shadowCastingMode     = UnityEngine.Rendering.ShadowCastingMode.On;
        r.receiveShadows        = true;
        { var c2 = go.GetComponent<Collider>(); if (c2 != null) { if (Application.isPlaying) Object.Destroy(c2); else Object.DestroyImmediate(c2); } }
        return go;
    }

    private GameObject Sphere(GameObject raiz, string n, Vector3 lp, float radio, Color c)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = n;
        go.transform.SetParent(raiz.transform, false);
        go.transform.localPosition = lp;
        go.transform.localScale    = Vector3.one * (radio * 2f);
        var r = go.GetComponent<Renderer>();
        r.material              = MatURP(c);
        r.shadowCastingMode     = UnityEngine.Rendering.ShadowCastingMode.On;
        r.receiveShadows        = true;
        { var c2 = go.GetComponent<Collider>(); if (c2 != null) { if (Application.isPlaying) Object.Destroy(c2); else Object.DestroyImmediate(c2); } }
        return go;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  INPUT
    // ═══════════════════════════════════════════════════════════════════════

    private void LeerInput()
    {
        var kb = Keyboard.current;
        var m  = Mouse.current;
        var gp = UnityEngine.InputSystem.Gamepad.current;

        // ── TECLADO + RATÓN ────────────────────────────────────────────────
        if (kb != null)
        {
            inputMovimiento = new Vector2(
                (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f),
                (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));

            estaCorriendo = kb.leftShiftKey.isPressed;

            if (kb.spaceKey.wasPressedThisFrame && estaEnSuelo) Saltar();
            if (kb.cKey.wasPressedThisFrame)                    ToggleAgacharse();
            if (kb.fKey.wasPressedThisFrame && sistemaBombas != null) sistemaBombas.ColocarBomba();
            if (kb.gKey.wasPressedThisFrame && sistemaBombas != null) sistemaBombas.DetonarUltima();
            if (kb.eKey.wasPressedThisFrame) TentarEntrarVehiculo();
            if (kb.escapeKey.wasPressedThisFrame)
            { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        if (m != null)
        {
            // BUG FIX: solo apuntar/disparar cuando el cursor está bloqueado
            // (evita que el clic para relockear el cursor también dispare)
            bool cursorBloqueado = Cursor.lockState == CursorLockMode.Locked;

            modoApuntar = cursorBloqueado && m.rightButton.isPressed;

            // Cámara: solo orbitar con cursor bloqueado
            if (cursorBloqueado)
            {
                Vector2 delta = m.delta.ReadValue();
                anguloH += delta.x * sensibilidadX * 0.05f;
                anguloV -= delta.y * sensibilidadY * 0.05f;
                anguloV  = Mathf.Clamp(anguloV, limVertMin, limVertMax);
            }

            // Disparar solo con cursor bloqueado
            if (cursorBloqueado && m.leftButton.isPressed && sistemaDisparo != null)
            {
                sistemaDisparo.Disparar();
                if (m.leftButton.wasPressedThisFrame)
                    animPersonaje?.SetTrigger(AnimDisparar);
            }
        }

        // ── GAMEPAD ────────────────────────────────────────────────────────
        // Stick izq = mover · Stick der = cámara · A = saltar · B = agachar
        // RB = apuntar · RT = disparar · X = entrar vehículo · Start = pausa
        if (gp != null)
        {
            Vector2 stickIzq = gp.leftStick.ReadValue();
            Vector2 stickDer = gp.rightStick.ReadValue();

            // Movimiento con stick izquierdo (zona muerta 0.15)
            if (stickIzq.magnitude > 0.15f)
                inputMovimiento = stickIzq;
            else if (kb == null || Keyboard.current == null)
                inputMovimiento = Vector2.zero;

            // Correr: presionar stick izquierdo
            if (gp.leftStickButton.isPressed) estaCorriendo = true;

            // Cámara con stick derecho
            if (stickDer.magnitude > 0.08f)
            {
                anguloH += stickDer.x * sensibilidadX * 2.5f * Time.deltaTime;
                anguloV -= stickDer.y * sensibilidadY * 2.5f * Time.deltaTime;
                anguloV  = Mathf.Clamp(anguloV, limVertMin, limVertMax);
            }

            // Acciones
            if (gp.buttonSouth.wasPressedThisFrame && estaEnSuelo) Saltar();     // A
            if (gp.buttonEast.wasPressedThisFrame)  ToggleAgacharse();            // B
            if (gp.buttonWest.wasPressedThisFrame)  TentarEntrarVehiculo();       // X
            if (gp.startButton.wasPressedThisFrame)
            { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }    // Start

            // Apuntar y disparar
            modoApuntar = gp.leftShoulder.isPressed || gp.leftTrigger.ReadValue() > 0.3f; // LB / LT
            if ((gp.rightShoulder.isPressed || gp.rightTrigger.ReadValue() > 0.3f)        // RB / RT
                && sistemaDisparo != null)
            {
                sistemaDisparo.Disparar();
                if (gp.rightShoulder.wasPressedThisFrame || gp.rightTrigger.ReadValue() > 0.3f)
                    animPersonaje?.SetTrigger(AnimDisparar);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ENTRADA A VEHÍCULO  (Gorka Pillar 1 — "Is Near Vehicle" traducido)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Busca el ControladorVehiculoJugador más cercano dentro de 3.5 m y entra en él.
    /// Gorka usa un Blueprint node "Get Overlapping Actors" con filtro de clase.
    /// Aquí: OverlapSphere físico + GetComponent — misma lógica, código C# puro.
    /// </summary>
    private void TentarEntrarVehiculo()
    {
        const float radioInteraccion = 3.5f;
        var cols = Physics.OverlapSphere(transform.position, radioInteraccion);

        ControladorVehiculoJugador cochesMasCercano = null;
        float distMin = float.MaxValue;

        foreach (var col in cols)
        {
            var veh = col.GetComponent<ControladorVehiculoJugador>()
                   ?? col.GetComponentInParent<ControladorVehiculoJugador>();
            if (veh == null || veh.JugadorDentro) continue;

            float d = Vector3.Distance(transform.position, veh.transform.position);
            if (d < distMin) { distMin = d; cochesMasCercano = veh; }
        }

        if (cochesMasCercano != null)
        {
            AlsasuaLogger.Info("Jugador", $"Entrando en vehículo '{cochesMasCercano.name}'.");
            cochesMasCercano.EntraJugador(this);
        }
    }

    private void GestionarCursor()
    {
        if (Cursor.lockState != CursorLockMode.Locked
            && Mouse.current != null
            && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MOVIMIENTO DEL JUGADOR
    // ═══════════════════════════════════════════════════════════════════════

    private void MoverJugador()
    {
        // Dirección relativa a la cámara (horizontal)
        Vector3 forward = Quaternion.Euler(0f, anguloH, 0f) * Vector3.forward;
        Vector3 right   = Quaternion.Euler(0f, anguloH, 0f) * Vector3.right;
        Vector3 moveDir = (forward * inputMovimiento.y + right * inputMovimiento.x);
        moveDir = Vector3.ClampMagnitude(moveDir, 1f);

        float vel = estaAgachado ? velocidadAgachar
                  : estaCorriendo ? velocidadCorrer
                  : velocidadAndar;

        velHoriz = Vector3.Lerp(velHoriz, moveDir * vel, suavidadMovimiento * Time.deltaTime);
        cc.Move(velHoriz * Time.deltaTime);
        ActualizarPasos();

        // ── Rotar jugador ───────────────────────────────────────────────────
        if (modoApuntar)
        {
            // Al apuntar: siempre encarar la dirección de cámara (anguloH)
            yawJugador = Mathf.LerpAngle(yawJugador, anguloH, suavidadGiro * 2f * Time.deltaTime);
        }
        else if (moveDir.magnitude > 0.05f)
        {
            // Al moverse sin apuntar: rotar hacia la dirección de movimiento
            float yawObj = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            yawJugador = Mathf.LerpAngle(yawJugador, yawObj, suavidadGiro * Time.deltaTime);
        }

        transform.rotation = Quaternion.Euler(0f, yawJugador, 0f);
    }

    private void Gravedad()
    {
        // FIX ASCENSO CESIUM: suplementar cc.isGrounded con un raycast corto hacia abajo.
        // Cuando CesiumGlobeAnchor reorienta el jugador en cada tick, el CharacterController
        // puede reportar !isGrounded durante 1-2 frames aunque el jugador esté sobre el suelo.
        // Un raycast de 0.5 m hacia abajo (origen a 0.2 m de la planta) cubre esa ventana.
        bool groundRay = Physics.Raycast(
            transform.position + Vector3.up * 0.2f,
            Vector3.down, 0.5f,
            ~LayerMask.GetMask("Player", "Ignore Raycast"),
            QueryTriggerInteraction.Ignore);
        estaEnSuelo = cc.isGrounded || groundRay;

        if (estaEnSuelo)
        {
            // BUG 5 FIX: asignar -2f Y salir — no sumar gravedad este frame.
            if (velVert.y < 0f) velVert.y = -2f;

            // FIX ASCENSO CESIUM: si velVert.y es positivo sin que el jugador haya saltado
            // (Saltar() lo pone a ≈15 f), es que algo externo (CesiumGlobeAnchor, tile spawn)
            // ha empujado el transform hacia arriba. Resetear a -2 para pegar al suelo.
            else if (velVert.y > 0f && velVert.y < fuerzaSalto * 0.3f)
                velVert.y = -2f;
        }
        else
        {
            // Solo aplicar gravedad cuando estamos en el aire
            velVert.y += gravedad * Time.deltaTime;
        }

        cc.Move(velVert * Time.deltaTime);
    }

    private void Saltar()
    {
        velVert.y = Mathf.Sqrt(fuerzaSalto * -2f * gravedad);
        animPersonaje?.SetTrigger(AnimSaltar);
    }

    private void ToggleAgacharse()
    {
        estaAgachado = !estaAgachado;
        float h = estaAgachado ? 0.9f : 1.8f;
        cc.height = h;
        cc.center = new Vector3(0f, h * 0.5f, 0f);
        if (pivotCam != null)
            pivotCam.localPosition = new Vector3(0f, estaAgachado ? alturaHombro * 0.55f : alturaHombro, 0f);
    }

    // ── Pasos de audio ───────────────────────────────────────────────────
    private void ActualizarPasos()
    {
        if (!estaEnSuelo || velHoriz.magnitude < 0.5f) { _timerPaso = 0f; return; }

        float intervalo = estaCorriendo ? 0.35f : 0.55f;
        _timerPaso -= Time.deltaTime;
        if (_timerPaso > 0f) return;

        AudioManager.I?.Play(
            estaCorriendo ? AudioManager.Clip.PasoCorrer : AudioManager.Clip.PasoNormal,
            transform.position);
        _timerPaso = intervalo;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CÁMARA TERCERA PERSONA — SPRING ARM CON COLISIÓN
    // ═══════════════════════════════════════════════════════════════════════

    private void ActualizarCamara()
    {
        if (pivotCam == null) return;

        float dist = modoApuntar ? distanciaApuntar : distanciaBrazo;

        // Desplazamiento lateral (al apuntar, cámara ligeramente a la derecha)
        Vector3 offsetLateral = modoApuntar
            ? Quaternion.Euler(0f, anguloH, 0f) * new Vector3(0.45f, 0f, 0f)
            : Vector3.zero;

        Vector3   pivotPos  = pivotCam.position + offsetLateral;
        Quaternion rotBrazo = Quaternion.Euler(anguloV, anguloH, 0f);
        Vector3   posBuscada = pivotPos + rotBrazo * (Vector3.back * dist);

        // ── Spring Arm: detectar geometría entre pivot y cámara ─────────────
        Vector3 posFinal = posBuscada;
        if (Physics.Linecast(pivotPos, posBuscada, out RaycastHit hit, _maskSpringArm))
        {
            // Acercar la cámara al punto de colisión (+ pequeño offset)
            posFinal = hit.point + (pivotPos - posBuscada).normalized * 0.18f;
        }

        // ── Posición suavizada ───────────────────────────────────────────────
        camaraTP.transform.position = Vector3.Lerp(
            camaraTP.transform.position, posFinal, suavidadCamara * Time.deltaTime);

        // ── Orientación: la cámara mira hacia el pivot (cabeza del jugador) ──
        camaraTP.transform.LookAt(pivotPos + Vector3.up * 0.10f);

        // ── FOV dinámico: zoom suave al apuntar ──────────────────────────────
        float fovObj = modoApuntar ? fovApuntando : fovNormal;
        camaraTP.fieldOfView = Mathf.Lerp(camaraTP.fieldOfView, fovObj, 10f * Time.deltaTime);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DAÑO Y SALUD
    // ═══════════════════════════════════════════════════════════════════════

    // Evento para SistemaPolish y otros sistemas
    public static event System.Action<int> OnDanoRecibido;

    public void RecibirDano(int cantidad)
    {
        vida      = Mathf.Max(0, vida - cantidad);
        timerDano = 0.35f;   // activar flash rojo en pantalla
        OnDanoRecibido?.Invoke(cantidad);
        if (vida <= 0) Morir();
    }

    public void Curar(int cantidad) =>
        vida = Mathf.Min(vidaMax, vida + cantidad);

    private void Morir()
    {
        AlsasuaLogger.Info("Jugador", "¡Has muerto!");
        animPersonaje?.SetTrigger(AnimMorir);

        // Si el jugador muere dentro de un vehículo → expulsarlo para evitar
        // estado corrupto (CharacterController desactivado, renderer invisible,
        // transform hijo del coche). ForzarSalida() es seguro si no está dentro.
        GetComponentInParent<ControladorVehiculoJugador>()?.ForzarSalida();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        enabled          = false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ANIMACIONES MIXAMO
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Empuja los parámetros del estado de movimiento al Animator del personaje Mixamo.
    /// Se llama cada frame desde Update(). Si no hay Animator (cuerpo procedural), no hace nada.
    /// </summary>
    private void ActualizarAnimaciones()
    {
        if (animPersonaje == null) return;

        // ── Velocidad normalizada para el Blend Tree de locomoción ──────────
        // 0.0 = parado (Idle)
        // 0.5 = andando (Walking) — al andar sin correr
        // 1.0 = corriendo (Running)
        // Usamos velHoriz.magnitude para que el blend refleje la velocidad REAL
        // (no el input — si hay inercia, la transición es más suave).
        float speedNorm;
        float speed = velHoriz.magnitude;
        if (estaCorriendo)
            speedNorm = Mathf.InverseLerp(0f, velocidadCorrer, speed);
        else
            // Rango 0→andar mapea a 0→0.5 para que correr sea siempre >0.5
            speedNorm = Mathf.InverseLerp(0f, velocidadAndar, speed) * 0.5f;

        // dampTime=0.1s → transición suave entre estados sin pop brusco
        animPersonaje.SetFloat(AnimVelocidad, speedNorm, 0.1f, Time.deltaTime);

        // ── Estados booleanos ────────────────────────────────────────────────
        animPersonaje.SetBool(AnimAgachado,  estaAgachado);
        animPersonaje.SetBool(AnimApuntando, modoApuntar);
        animPersonaje.SetBool(AnimEnSuelo,   estaEnSuelo);
    }

    // OnGUI() eliminado — HUDJugador.cs gestiona el HUD vía Canvas uGUI (sin legacy OnGUI).
}
