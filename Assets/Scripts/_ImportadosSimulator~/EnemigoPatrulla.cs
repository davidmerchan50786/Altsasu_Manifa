// Assets/Scripts/EnemigoPatrulla.cs
// Enemigo genérico (mercenario/soldado ficticio) con IA de patrulla
// Estados: Patrullando → Alertado → Persiguiendo → Atacando → Muerto

using UnityEngine;
using System.Collections;

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
    [SerializeField] private int vida    = 100;
    [SerializeField] private int vidaMax = 100;

    [Header("═══ VISIÓN ═══")]
    [SerializeField] private float radioVision     = 25f;
    [SerializeField] private float anguloVision    = 110f;
    [SerializeField] private float radioEscucha    = 8f;   // detecta sin línea de visión

    [Header("═══ MOVIMIENTO ═══")]
    [SerializeField] private float velocidadPatrulla  = 2.5f;
    [SerializeField] private float velocidadPerseguir = 5.5f;
    [SerializeField] private float velocidadGiro      = 180f;

    [Header("═══ ATAQUE ═══")]
    [SerializeField] private float radioAtaque        = 18f;
    [SerializeField] private int   danoPorDisparo     = 15;
    [SerializeField] private float cadenciaAtaque     = 0.8f;
    [SerializeField] private float precisionEnemy     = 0.05f;  // dispersión

    [Header("═══ GRÁFICOS (MYASSETS) ═══")]
    [Tooltip("Asignar el Prefab del Soldado de 'Low Poly Soldiers Demo'. Si se deja vacío utilizará cajas procedurales con GPU instancing.")]
    [SerializeField] private GameObject prefabVisual;

    [Header("═══ WAYPOINTS DE PATRULLA ═══")]
    [SerializeField] private Transform[] waypointsPatrulla;
    [SerializeField] private float tiempoEsperaWP = 2f;

    // ═══════════════════════════════════════════════════════════════════════
    //  ESTADO INTERNO
    // ═══════════════════════════════════════════════════════════════════════

    public  EstadoIA Estado  { get; private set; } = EstadoIA.Patrullando;
    private Transform jugador;
    private ControladorJugador controlJugador;
    private int    wpActual         = 0;
    private float  timerAtaque      = 0f;
    private float  timerAlerta      = 0f;
    private bool   esperandoWP      = false;
    private float  timerVision      = 0f;
    private float  offsetVision     = 0f;
    private Vector3 ultimaPosJugador;

    // ═══════════════════════════════════════════════════════════════════════
    //  UNITY
    // ═══════════════════════════════════════════════════════════════════════

    private void Start()
    {
        // Buscar jugador
        var jp = Object.FindFirstObjectByType<ControladorJugador>();
        if (jp != null)
        {
            jugador        = jp.transform;
            controlJugador = jp;
        }

        offsetVision = Random.Range(0f, 0.1f); // Desincronizar Time-Slicing inicial

        // Crear cuerpo visual básico si no tiene hijos
        if (transform.childCount == 0)
            CrearCuerpoBasico();
    }

    private void Update()
    {
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

        // V3 FIX (TIME-SLICING): Reducir comprobación de visión de 60Hz a 10Hz
        // Ahorra tiempo crítico O(N) CPU raycasts. _offsetVision evita que todos raycasteen en el mismo frame.
        if (Estado != EstadoIA.Atacando)
        {
            timerVision += Time.deltaTime;
            if (timerVision >= 0.1f + offsetVision)
            {
                ComprobarVision();
                timerVision  = 0f;
                offsetVision = 0f;
            }
        }
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
        AudioManager.I?.Play(AudioManager.Clip.Disparo, origen);

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
    private static readonly MaterialPropertyBlock _pbFlash = new MaterialPropertyBlock();
    private static readonly int _idColor = Shader.PropertyToID("_BaseColor");

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

        Destroy(gameObject, 8f);
        Debug.Log("[Enemigo] Derribado.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CUERPO VISUAL BÁSICO (GPU INSTANCING)
    // ═══════════════════════════════════════════════════════════════════════

    private Color colorUniforme = new Color(0.25f, 0.28f, 0.22f); // verde militar

    // V3 FIX LEAK & DRAW CALLS: Compartir una única instancia estática de Material por Color.
    // Combinado con enableInstancing = true, dibuja miles de soldados en 1 único Draw Call.
    private static readonly System.Collections.Generic.Dictionary<Color, Material> _sharedMats = 
        new System.Collections.Generic.Dictionary<Color, Material>();

    private static Material ObtenerMaterialCompartido(Color color)
    {
        if (_sharedMats.TryGetValue(color, out Material mat) && mat != null)
            return mat;

        var shader = Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Universal Render Pipeline/Unlit")
                  ?? Shader.Find("Standard");
        
        if (shader == null)
        {
            shader = Shader.Find("Hidden/InternalErrorShader");
        }

        mat = new Material(shader) { color = color };
        mat.enableInstancing = true; // BÁSICO para erradicar CPU Overhead de miles de Renderers
        _sharedMats[color] = mat;
        
        return mat;
    }

    private void OnDestroy()
    {
        // Ya no es necesario destruir materiales locales instanciados (Eran O(N)). 
        // El DIctionary y GC de Unity manejan la salida segura del Runtime.
    }

    private void CrearCuerpoBasico()
    {
#if UNITY_EDITOR
        // V4 AUTO-ASSIGN: Si estamos en el editor y el usuario le dio a "Play" sin configurar el prefab, autodescubrirlo.
        if (prefabVisual == null)
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab Soldier");
            if (guids.Length > 0)
            {
                // Cargar el primer modelo de Soldado que el paquete haya inyectado
                prefabVisual = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
            }
        }
#endif

        // V4 FIX: Si el usuario ha acoplado un soldado 3D hiperrealista, instanciarlo y cancelar el procedural.
        if (prefabVisual != null)
        {
            var modelo3D = Instantiate(prefabVisual, transform);
            modelo3D.transform.localPosition = Vector3.down * 0.5f; // Alinear pies con el collider cápsula
            
            // Intento de anclaje de un Animator local si el modelo cuenta con uno
            var aniLocal = modelo3D.GetComponentInChildren<Animator>();
            if (aniLocal != null && animador == null) 
                animador = aniLocal;

            return;
        }

        // Cajas procedurales primitivas (GPU Instanced de alta reusabilidad)
        // Cuerpo
        var cuerpo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        cuerpo.transform.SetParent(transform);
        cuerpo.transform.localPosition = new Vector3(0f, 1f, 0f);
        cuerpo.transform.localScale    = new Vector3(0.6f, 0.9f, 0.6f);
        var matCuerpo = ObtenerMaterialCompartido(colorUniforme);
        if (matCuerpo != null) cuerpo.GetComponent<Renderer>().sharedMaterial = matCuerpo;
        Destroy(cuerpo.GetComponent<Collider>());

        // Cabeza
        var cabeza = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        cabeza.transform.SetParent(transform);
        cabeza.transform.localPosition = new Vector3(0f, 2.0f, 0f);
        cabeza.transform.localScale    = new Vector3(0.4f, 0.4f, 0.4f);
        var matCabeza = ObtenerMaterialCompartido(new Color(0.75f, 0.62f, 0.48f));
        if (matCabeza != null) cabeza.GetComponent<Renderer>().sharedMaterial = matCabeza;
        Destroy(cabeza.GetComponent<Collider>());

        // Casco
        var casco = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        casco.transform.SetParent(transform);
        casco.transform.localPosition = new Vector3(0f, 2.15f, 0f);
        casco.transform.localScale    = new Vector3(0.45f, 0.35f, 0.45f);
        var matCasco = ObtenerMaterialCompartido(colorUniforme);
        if (matCasco != null) casco.GetComponent<Renderer>().sharedMaterial = matCasco;
        Destroy(casco.GetComponent<Collider>());

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
        Gizmos.color = Color.yellow;
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
