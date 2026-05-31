// Assets/Scripts/EnemigoPatrulla.cs
// Enemigo genérico (mercenario/soldado ficticio) con IA de patrulla
// Estados: Patrullando → Alertado → Persiguiendo → Atacando → Muerto

using UnityEngine;
using System.Collections;
using Unity.Profiling;

public class EnemigoPatrulla : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════
    //  ENUM DE ESTADOS
    // ═══════════════════════════════════════════════════════════════════════

    public enum EstadoIA { Patrullando, Alertado, Persiguiendo, Atacando, Muerto }

    // ═══════════════════════════════════════════════════════════════════════
    //  CONFIGURACIÓN
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ VIDA ═══")]
    [Tooltip("Puntos de vida actuales del enemigo.")]
    [SerializeField] private int vida    = 100;

    [Header("═══ VISIÓN ═══")]
    [Tooltip("Radio de detección visual (m). El jugador debe estar dentro de este radio y en el ángulo de visión para ser detectado.")]
    [SerializeField] private float radioVision     = 25f;
    [Tooltip("Ángulo del cono de visión frontal en grados (p.ej. 110 = ±55° desde el frente).")]
    [SerializeField] private float anguloVision    = 110f;
    [Tooltip("Radio de escucha (m). Detecta al jugador sin línea de visión si está dentro de este radio.")]
    [SerializeField] private float radioEscucha    = 8f;

    [Header("═══ MOVIMIENTO ═══")]
    [Tooltip("Velocidad en m/s durante la patrulla normal de waypoints.")]
    [SerializeField] private float velocidadPatrulla  = 2.5f;
    [Tooltip("Velocidad en m/s cuando persigue activamente al jugador detectado.")]
    [SerializeField] private float velocidadPerseguir = 5.5f;
    [Tooltip("Velocidad de giro máxima en grados/segundo.")]
    [SerializeField] private float velocidadGiro      = 180f;

    [Header("═══ ATAQUE ═══")]
    [Tooltip("Radio máximo en metros desde el que el enemigo entra en modo Atacando.")]
    [SerializeField] private float radioAtaque        = 18f;
    [Tooltip("Daño aplicado al jugador por cada impacto de disparo.")]
    [SerializeField] private int   danoPorDisparo     = 15;
    [Tooltip("Segundos mínimos entre disparos consecutivos del enemigo.")]
    [SerializeField] private float cadenciaAtaque     = 0.8f;
    [Tooltip("Dispersión angular de los disparos (0 = perfecta puntería, 0.1 = muy errática).")]
    [SerializeField] private float precisionEnemy     = 0.05f;

    [Header("═══ MODELO 3D ═══")]
    [Tooltip("Prefab del modelo 3D del enemigo (ej. GuardiaCivil.prefab, Jarrai.prefab).\n" +
             "Si está vacío se genera el cuerpo procedural de cápsulas como fallback.")]
    [SerializeField] private GameObject prefabModelo;

    [Header("═══ WAYPOINTS DE PATRULLA ═══")]
    [Tooltip("Puntos de patrulla en orden. El enemigo los recorre en bucle indefinido.")]
    [SerializeField] private Transform[] waypointsPatrulla;
    [Tooltip("Segundos que el enemigo espera quieto al llegar a cada waypoint.")]
    [SerializeField] private float tiempoEsperaWP = 2f;

    // ═══════════════════════════════════════════════════════════════════════
    //  ESTADO INTERNO
    // ═══════════════════════════════════════════════════════════════════════

    // FIX T16 / OBSERVABILIDAD: ProfilerMarker inicializado en constructor estático con
    // try/catch para evitar TypeInitializationException en EditMode tests.
    private static ProfilerMarker _markerUpdate;
    static EnemigoPatrulla()
    {
        try   { _markerUpdate = new ProfilerMarker("EnemigoPatrulla.Update"); }
        catch (System.Exception) { _markerUpdate = default; }
    }

    // FIX CRASH: MaterialPropertyBlock NO puede inicializarse como campo estático (static readonly).
    // Unity lo prohíbe expresamente: CreateImpl no está permitido en constructores de MonoBehaviour.
    // Se inicializa en Awake() como campo de instancia — garantizado antes del primer Update().
    private MaterialPropertyBlock _pbFlash;
    private int _idColor;

    public  EstadoIA Estado  { get; private set; } = EstadoIA.Patrullando;
    private Transform jugador;
    private ControladorJugador controlJugador;
    private int    wpActual         = 0;
    private float  timerAtaque      = 0f;
    private float  timerAlerta      = 0f;
    private bool   esperandoWP      = false;
    private Vector3 ultimaPosJugador;
    private float   _velY = 0f;   // gravedad simple — Cesium no mueve NPCs solos

    // ═══════════════════════════════════════════════════════════════════════
    //  UNITY
    // ═══════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        // FIX TEST T16: crear cuerpo PRIMERO para que los tests de edit mode encuentren
        // Renderers aunque el MaterialPropertyBlock u otros pasos posteriores fallen.
        if (transform.childCount == 0)
        {
            if (prefabModelo != null)
                InstanciarModeloPrefab();
            else
                CrearCuerpoBasico();
        }

        // FIX CRASH: inicializar MaterialPropertyBlock en Awake(), no como campo estático.
        // Unity no permite crear objetos UnityEngine en constructores / inicializadores de campo.
        _pbFlash = new MaterialPropertyBlock();
        _idColor = Shader.PropertyToID("_BaseColor");
    }

    // Instancia el prefab 3D del enemigo (Kenney GuardiaCivil, Jarrai, etc.)
    // y desactiva sus colisionadores propios (el CapsuleCollider del GO raíz gestiona la física).
    private void InstanciarModeloPrefab()
    {
        try
        {
            var go = Object.Instantiate(prefabModelo, transform);
            go.name = "_ModeloEnemigo";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale    = Vector3.one;

            // Desactivar colisionadores del modelo — el CapsuleCollider raíz gestiona la física.
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                col.enabled = false;

            // Si el prefab tiene Animator, dejarlo activo para que reproduzca idle/andar.
            // (No asignamos RuntimeAnimatorController aquí — el prefab ya tiene el suyo.)
            AlsasuaLogger.Info("EnemigoPatrulla", $"{name}: modelo 3D '{prefabModelo.name}' instanciado.");
        }
        catch (System.Exception ex)
        {
            AlsasuaLogger.Warn("EnemigoPatrulla",
                $"{name}: error instanciando prefabModelo '{prefabModelo.name}' — " +
                $"usando cuerpo procedural. Detalle: {ex.Message}");
            prefabModelo = null;
            CrearCuerpoBasico();
        }
    }

    private void Start()
    {
        // Buscar jugador (solo disponible en Play Mode; en edit-mode tests jp será null)
        var jp = Object.FindFirstObjectByType<ControladorJugador>();
        if (jp != null)
        {
            jugador        = jp.transform;
            controlJugador = jp;
        }
    }

    private void Update()
    {
        using var _prof = _markerUpdate.Auto();
        if (Estado == EstadoIA.Muerto) return;

        // BUG 12 FIX: solo decrementar timerAtaque en el estado Atacando.
        // Antes decrementaba siempre → llegaba muy negativo en patrulla →
        // al entrar en Atacando disparaba 0 frames después sin cadencia.
        // Clamp a 0 al salir de Atacando evita desbordamientos.
        if (Estado == EstadoIA.Atacando)
            timerAtaque -= Time.deltaTime;

        switch (Estado)
        {
            case EstadoIA.Patrullando:  ActualizarPatrulla();   break;
            case EstadoIA.Alertado:     ActualizarAlerta();     break;
            case EstadoIA.Persiguiendo: ActualizarPerseguir();  break;
            case EstadoIA.Atacando:     ActualizarAtacando();   break;
        }

        // Comprobar visión siempre
        if (Estado != EstadoIA.Atacando)
            ComprobarVision();

        // Gravedad simple: los NPCs no tienen CharacterController ni Rigidbody.
        // Raycast corto hacia abajo para pegarse al suelo Cesium o SueloBase.
        AplicarGravedad();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ESTADOS DE IA
    // ═══════════════════════════════════════════════════════════════════════

    private void ActualizarPatrulla()
    {
        // BUG FIX: evitar NullReferenceException si el array no está asignado en el Inspector
        if (waypointsPatrulla == null || waypointsPatrulla.Length == 0 || esperandoWP) return;

        var wp = waypointsPatrulla[wpActual];
        if (wp == null) { wpActual = 0; return; }

        MoverHacia(wp.position, velocidadPatrulla);

        if (Vector3.Distance(transform.position, wp.position) < 1.5f)
            StartCoroutine(EsperarEnWP());
    }

    private void ActualizarAlerta()
    {
        timerAlerta -= Time.deltaTime;

        // Girar buscando al jugador
        transform.Rotate(Vector3.up, 60f * Time.deltaTime);

        if (timerAlerta <= 0f)
            CambiarEstado(EstadoIA.Patrullando);
    }

    private void ActualizarPerseguir()
    {
        if (jugador == null) { CambiarEstado(EstadoIA.Alertado); return; }

        float dist = Vector3.Distance(transform.position, jugador.position);

        if (dist <= radioAtaque)
        {
            CambiarEstado(EstadoIA.Atacando);
            return;
        }

        if (dist > radioVision * 1.5f)
        {
            ultimaPosJugador = jugador.position;
            CambiarEstado(EstadoIA.Alertado);
            return;
        }

        MoverHacia(jugador.position, velocidadPerseguir);
    }

    private void ActualizarAtacando()
    {
        if (jugador == null || controlJugador == null || controlJugador.EstaMuerto)
        {
            CambiarEstado(EstadoIA.Patrullando);
            return;
        }

        float dist = Vector3.Distance(transform.position, jugador.position);

        // Si el jugador se aleja, perseguir
        if (dist > radioAtaque * 1.3f)
        {
            CambiarEstado(EstadoIA.Persiguiendo);
            return;
        }

        // Apuntar al jugador
        var dir = (jugador.position - transform.position);
        dir.y   = 0;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(dir.normalized),
                velocidadGiro * Time.deltaTime);

        // Disparar
        if (timerAtaque <= 0f)
        {
            Disparar();
            timerAtaque = cadenciaAtaque;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  VISIÓN
    // ═══════════════════════════════════════════════════════════════════════

    private void ComprobarVision()
    {
        if (jugador == null) return;

        float dist  = Vector3.Distance(transform.position, jugador.position);
        bool  cerca = dist < radioEscucha;

        bool enCampo = false;
        if (dist < radioVision)
        {
            var dirJugador = (jugador.position - transform.position).normalized;
            float angulo   = Vector3.Angle(transform.forward, dirJugador);
            if (angulo < anguloVision / 2f)
            {
                // BUG 10 FIX: usar ~0 (todos los layers) en vez de LayerMask.GetMask("Default").
                // Con "Default" los tiles de Cesium (en su propia capa) no bloqueaban el rayo,
                // permitiendo al enemigo "ver" al jugador a través de edificios fotorrealistas.
                // Restamos 1m a la distancia para no impactar el propio collider del jugador.
                float distObst = Mathf.Max(0f, dist - 1f);
                if (!Physics.Raycast(transform.position + Vector3.up,
                                     dirJugador, distObst,
                                     ~0, QueryTriggerInteraction.Ignore))
                    enCampo = true;
            }
        }

        if ((enCampo || cerca) && Estado == EstadoIA.Patrullando)
            CambiarEstado(EstadoIA.Persiguiendo);
        else if (enCampo && Estado == EstadoIA.Alertado)
            CambiarEstado(EstadoIA.Persiguiendo);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DISPARO ENEMIGO
    // ═══════════════════════════════════════════════════════════════════════

    private void Disparar()
    {
        if (jugador == null) return;

        Vector3 origen    = transform.position + Vector3.up * 1.5f;
        Vector3 dir       = (jugador.position + Vector3.up * 1f - origen).normalized;

        // Dispersión perpendicular a la dirección de disparo
        Vector3 perpH = Vector3.Cross(dir, Vector3.up).normalized;
        if (perpH == Vector3.zero) perpH = Vector3.right;
        Vector3 perpV = Vector3.Cross(dir, perpH).normalized;
        dir += perpH * Random.Range(-precisionEnemy, precisionEnemy)
             + perpV * Random.Range(-precisionEnemy, precisionEnemy);

        // Flash visual + audio
        SistemaDisparo_Flash(origen, dir);
        // AudioManager.I?.Play(AudioManager.Clip.Disparo, origen); // pendiente de integrar

        // Impacto
        if (Physics.Raycast(origen, dir, out RaycastHit hit, radioAtaque * 1.5f))
        {
            var jugadorHit = hit.collider.GetComponentInParent<ControladorJugador>();
            if (jugadorHit != null)
                jugadorHit.RecibirDano(danoPorDisparo);
        }
    }

    private void SistemaDisparo_Flash(Vector3 origen, Vector3 dir)
    {
        var go = new GameObject("FlashEnemigo");
        go.transform.position = origen;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 0.05f;
        main.startSpeed    = 5f;
        main.startSize     = 0.08f;
        main.startColor    = new Color(1f, 0.9f, 0.4f);
        main.maxParticles  = 5;
        var em = ps.emission;
        em.rateOverTime = 0;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 5) });
        ps.Play();
        Destroy(go, 0.15f);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MOVIMIENTO
    // ═══════════════════════════════════════════════════════════════════════

    // Gravedad simple basada en raycast.
    // Se activa cada frame: si hay suelo a menos de 3m abajo nos deslizamos hasta él;
    // si no, acumulamos velocidad de caída libre hasta encontrarlo.
    private void AplicarGravedad()
    {
        Vector3 origen = transform.position + Vector3.up * 0.5f;
        if (Physics.Raycast(origen, Vector3.down, out RaycastHit hit, 3f,
            ~LayerMask.GetMask("Ignore Raycast")))
        {
            _velY = 0f;
            float objetivoY = hit.point.y;
            if (Mathf.Abs(transform.position.y - objetivoY) > 0.01f)
                transform.position = new Vector3(
                    transform.position.x,
                    Mathf.MoveTowards(transform.position.y, objetivoY, 8f * Time.deltaTime),
                    transform.position.z);
        }
        else
        {
            _velY -= 9.81f * Time.deltaTime;
            transform.position += Vector3.up * _velY * Time.deltaTime;
        }
    }

    private void MoverHacia(Vector3 destino, float velocidad)
    {
        var dir = destino - transform.position;
        dir.y   = 0f;

        if (dir.magnitude > 0.1f)
        {
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(dir.normalized),
                velocidadGiro * Time.deltaTime);

            transform.position += transform.forward * velocidad * Time.deltaTime;
        }
    }

    private IEnumerator EsperarEnWP()
    {
        esperandoWP = true;
        yield return new WaitForSeconds(tiempoEsperaWP);
        wpActual    = (wpActual + 1) % waypointsPatrulla.Length;
        esperandoWP = false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CAMBIO DE ESTADO
    // ═══════════════════════════════════════════════════════════════════════

    private void CambiarEstado(EstadoIA nuevo)
    {
        // BUG 12 FIX: al salir del estado Atacando, resetear timerAtaque a 0
        // para que la próxima vez que entre en Atacando espere la cadencia completa.
        if (Estado == EstadoIA.Atacando && nuevo != EstadoIA.Atacando)
            timerAtaque = 0f;

        // FIX OBSERVABILIDAD: loggear cada transición de estado de IA.
        // Sin este log, los bugs de IA son imposibles de rastrear (la máquina de estados
        // cambiaba silenciosamente decenas de veces por segundo sin dejar rastro).
        if (Estado != nuevo)
            AlsasuaLogger.Verbose("EnemigoPatrulla", $"{name}: {Estado} → {nuevo}");

        Estado = nuevo;
        if (nuevo == EstadoIA.Alertado)
        {
            timerAlerta = 5f;
            if (jugador != null) ultimaPosJugador = jugador.position;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DAÑO Y MUERTE
    // ═══════════════════════════════════════════════════════════════════════

    public void RecibirDano(int cantidad)
    {
        if (Estado == EstadoIA.Muerto) return;

        vida -= cantidad;

        // Flash rojo en el cuerpo
        StartCoroutine(FlashDano());

        // Alerta al recibir disparo
        if (Estado == EstadoIA.Patrullando)
            CambiarEstado(EstadoIA.Persiguiendo);

        if (vida <= 0) Morir();
    }

    // BUG FIX: usar MaterialPropertyBlock (por instancia) en lugar de sharedMaterial.color.
    // sharedMaterial.color modificaba el material compartido y afectaba a TODOS los
    // enemigos que usaban ese mismo material → flash rojo en pantalla en todos ellos.
    // MaterialPropertyBlock sobreescribe las propiedades solo para este Renderer concreto,
    // sin crear nuevas instancias de material (cero GC, cero leak).
    // NOTA: _pbFlash e _idColor se declaran arriba y se inicializan en Awake().

    private IEnumerator FlashDano()
    {
        _pbFlash.SetColor(_idColor, Color.red);
        _pbFlash.SetColor("_Color", Color.red);
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.SetPropertyBlock(_pbFlash);

        yield return new WaitForSeconds(0.15f);

        // BUG FIX: no restaurar color si el enemigo ya ha muerto durante el flash
        if (Estado != EstadoIA.Muerto)
        {
            _pbFlash.SetColor(_idColor, colorUniforme);
            _pbFlash.SetColor("_Color", colorUniforme);
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.SetPropertyBlock(_pbFlash);
        }
    }

    private void Morir()
    {
        Estado = EstadoIA.Muerto;

        // BUG FIX: detener todas las coroutines al morir para evitar que
        // FlashDano() restaure el color verde encima del color de "muerto"
        StopAllCoroutines();

        // Caer
        transform.rotation = Quaternion.Euler(90f, transform.eulerAngles.y, 0f);

        // FIX LEAK: r.material.color crea una instancia de Material por renderer (nunca destruida).
        // O(renderers) leaks por muerte de enemigo. MaterialPropertyBlock sobreescribe las propiedades
        // solo para cada renderer concreto, sin crear ninguna instancia → cero leak, cero GC.
        var mpbMuerto = new MaterialPropertyBlock();
        mpbMuerto.SetColor("_BaseColor", new Color(0.2f, 0.1f, 0.1f));
        mpbMuerto.SetColor("_Color",     new Color(0.2f, 0.1f, 0.1f));
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.SetPropertyBlock(mpbMuerto);

        if (Application.isPlaying) Destroy(gameObject, 8f);
        else DestroyImmediate(gameObject);
        AlsasuaLogger.Info("EnemigoPatrulla", $"{name}: derribado.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CUERPO VISUAL BÁSICO
    // ═══════════════════════════════════════════════════════════════════════

    private Color colorUniforme = new Color(0.25f, 0.28f, 0.22f); // verde militar

    // BUG FIX LEAK: lista de materiales creados con new Material() para destruirlos en OnDestroy().
    // MatURP era static → los materiales no podían ser rastreados por instancia → leak garantizado.
    private readonly System.Collections.Generic.List<Material> _matsCreados =
        new System.Collections.Generic.List<Material>();

    // Crea un Material compatible con URP (evita el magenta por shader incorrecto)
    // BUG FIX: null guard — new Material(null) lanza NullReferenceException si ningún shader está disponible.
    // BUG FIX LEAK: método de instancia (no static) para poder rastrear el material en _matsCreados.
    private Material MatURP(Color color)
    {
        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard")
                  ?? Shader.Find("HDRP/Unlit") ?? Shader.Find("Standard")
                  ?? Shader.Find("Standard");
        if (shader == null)
        {
            AlsasuaLogger.Error("EnemigoPatrulla", "MatURP: ningún shader URP/Standard encontrado. " +
                               "Incluye 'Universal Render Pipeline/Lit' en Always Included Shaders.");
            shader = Shader.Find("Hidden/InternalErrorShader");
            if (shader == null) return null;
        }
        var mat = new Material(shader) { color = color };
        _matsCreados.Add(mat);
        return mat;
    }

    // BUG FIX LEAK: destruir los materiales del cuerpo al destruir el enemigo.
    private void OnDestroy()
    {
        foreach (var m in _matsCreados)
            if (m != null) Object.Destroy(m);
        _matsCreados.Clear();
    }

    private void CrearCuerpoBasico()
    {
        // Cuerpo
        var cuerpo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        cuerpo.transform.SetParent(transform);
        cuerpo.transform.localPosition = new Vector3(0f, 1f, 0f);
        cuerpo.transform.localScale    = new Vector3(0.6f, 0.9f, 0.6f);
        var matCuerpo = MatURP(colorUniforme);
        if (matCuerpo != null) cuerpo.GetComponent<Renderer>().sharedMaterial = matCuerpo;
        { var c = cuerpo.GetComponent<Collider>(); if (c != null) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); } }

        // Cabeza
        var cabeza = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        cabeza.transform.SetParent(transform);
        cabeza.transform.localPosition = new Vector3(0f, 2.0f, 0f);
        cabeza.transform.localScale    = new Vector3(0.4f, 0.4f, 0.4f);
        var matCabeza = MatURP(new Color(0.75f, 0.62f, 0.48f));
        if (matCabeza != null) cabeza.GetComponent<Renderer>().sharedMaterial = matCabeza;
        { var c = cabeza.GetComponent<Collider>(); if (c != null) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); } }

        // Casco
        var casco = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        casco.transform.SetParent(transform);
        casco.transform.localPosition = new Vector3(0f, 2.15f, 0f);
        casco.transform.localScale    = new Vector3(0.45f, 0.35f, 0.45f);
        var matCasco = MatURP(colorUniforme);
        if (matCasco != null) casco.GetComponent<Renderer>().sharedMaterial = matCasco;
        { var c = casco.GetComponent<Collider>(); if (c != null) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); } }

        // Colisionador
        var col = gameObject.AddComponent<CapsuleCollider>();
        col.height = 1.8f;
        col.radius = 0.3f;
        col.center = Vector3.up * 0.9f;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  GIZMOS (waypoints visibles en editor)
    // ═══════════════════════════════════════════════════════════════════════

    private void OnDrawGizmosSelected()
    {
        // FIX OBSERVABILIDAD: color del indicador de visión refleja el estado IA actual.
        // En Play mode permite ver de un vistazo qué estado tiene cada enemigo en la escena.
        Color colorEstado = Estado switch
        {
            EstadoIA.Patrullando  => new Color(0.2f, 0.9f, 0.2f, 0.8f),  // verde  = en patrulla
            EstadoIA.Alertado     => new Color(1.0f, 0.9f, 0.0f, 0.8f),  // amarillo = alerta
            EstadoIA.Persiguiendo => new Color(1.0f, 0.5f, 0.0f, 0.8f),  // naranja  = persiguiendo
            EstadoIA.Atacando     => new Color(1.0f, 0.1f, 0.1f, 0.9f),  // rojo     = atacando
            _                     => Color.grey,                           // gris     = muerto
        };
        Gizmos.color = colorEstado;
        Gizmos.DrawWireSphere(transform.position, radioVision);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, radioAtaque);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, radioEscucha);

        if (waypointsPatrulla == null) return;
        Gizmos.color = Color.green;
        for (int i = 0; i < waypointsPatrulla.Length; i++)
        {
            if (waypointsPatrulla[i] == null) continue;
            Gizmos.DrawSphere(waypointsPatrulla[i].position, 0.4f);
            if (i + 1 < waypointsPatrulla.Length && waypointsPatrulla[i + 1] != null)
                Gizmos.DrawLine(waypointsPatrulla[i].position, waypointsPatrulla[i + 1].position);
        }
    }
}
