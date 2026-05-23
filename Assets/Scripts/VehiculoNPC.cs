// Assets/Scripts/VehiculoNPC.cs
// Vehículo NPC que sigue waypoints, frena ante obstáculos y recibe daño

using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
public class VehiculoNPC : VehiculoBase
{
    // ═══════════════════════════════════════════════════════════════════════
    //  MOVIMIENTO
    // ═══════════════════════════════════════════════════════════════════════

    [Header("═══ MOVIMIENTO NPC ═══")]
    // velocidadMax viene de VehiculoBase (SerializeField, default 10 → ajustar a 8 en Inspector)
    [Tooltip("Aceleración desde parado hasta velocidadMax (m/s²). Valores bajos = arranque gradual.")]
    [SerializeField] private float aceleracion     = 4f;
    [Tooltip("Velocidad angular máxima de giro del vehículo (grados/segundo).")]
    [SerializeField] private float velocidadGiro   = 120f;  // grados/seg
    [Tooltip("Distancia horizontal al waypoint para considerarlo alcanzado y avanzar al siguiente (m).")]
    [SerializeField] private float distanciaWP     = 4f;    // distancia para cambiar waypoint
    [Tooltip("Si está activo, al llegar al último waypoint el vehículo vuelve al primero (ruta en bucle).")]
    [SerializeField] private bool  bucleWaypoints  = true;

    [Header("═══ WAYPOINTS ═══")]
    [Tooltip("Arrastra aquí los GameObjects que marcan la ruta del coche")]
    [SerializeField] private List<Transform> waypoints = new List<Transform>();

    [Header("═══ OBSTÁCULOS ═══")]
    [Tooltip("Distancia de detección de obstáculos con el raycast frontal. El vehículo frenará si hay algo a este rango (m).")]
    [SerializeField] private float distanciaFreno  = 6f;
    [Tooltip("Ángulo total del cono de detección (°). Se usan 3 rayos: centro ±(ángulo/2). 30° = buena detección de giros.")]
    [SerializeField] private float anguloDeteccion = 30f;
    [Tooltip("Capas que pueden bloquear el avance del vehículo. Por defecto (~0) detecta todo.")]
    [SerializeField] private LayerMask capasObstaculo;

    // ═══════════════════════════════════════════════════════════════════════
    //  SALUD
    // ═══════════════════════════════════════════════════════════════════════

    // vida, vidaMax, destruido, IDamageable → heredados de VehiculoBase
    // SerializeField para ajustar vida en Inspector: seleccionar el GO y editar "Vida Maxima"

    [Header("═══ MODELO 3D ═══")]
    [Tooltip("Prefab del modelo visual del vehículo (ej. Interceptor.prefab). " +
             "Si se asigna, se instancia como hijo y se desactivan sus colliders. " +
             "Si está vacío, se usa la malla básica de cajas.")]
    [SerializeField] private GameObject prefabModelo;

    // ═══════════════════════════════════════════════════════════════════════
    //  COLOR DEL COCHE (aleatorio)
    // ═══════════════════════════════════════════════════════════════════════

    private static Color[] coloresCoche =
    {
        new Color(0.8f, 0.1f, 0.1f),   // rojo
        new Color(0.1f, 0.2f, 0.7f),   // azul
        new Color(0.1f, 0.5f, 0.2f),   // verde
        new Color(0.9f, 0.9f, 0.9f),   // blanco
        new Color(0.15f, 0.15f, 0.15f),// negro
        new Color(0.7f, 0.6f, 0.1f),   // amarillo oscuro
        new Color(0.5f, 0.5f, 0.5f),   // gris
    };

    // ═══════════════════════════════════════════════════════════════════════
    //  ESTADO INTERNO
    // ═══════════════════════════════════════════════════════════════════════

    // _rb heredado de VehiculoBase
    private int       wpActual    = 0;
    private float     velocidadActual = 0f;
    private bool      frenando    = false;
    // BUG 17 FIX: cachear el renderer principal para no buscarlo en cada impacto/daño.
    private Renderer             rendererPrincipal;
    // FIX LEAK: MPB reutilizable para asignar y modificar colores sin crear instancias de Material.
    private MaterialPropertyBlock _mpbCoche;

    // ═══════════════════════════════════════════════════════════════════════
    //  UNITY
    // ═══════════════════════════════════════════════════════════════════════

    protected override void OnAwakeVehiculo()
    {
        _rb.constraints   = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        _rb.linearDamping = 1.5f;
        _vida = vidaMaxima; // sobreescribir el valor base con el del NPC (80 vs 100)

        if (capasObstaculo == 0) capasObstaculo = ~0;

        if (prefabModelo != null) InstanciarModeloPrefab();
        else                      CrearCuerpoBasico_Inline();

        rendererPrincipal = GetComponentInChildren<Renderer>();

        if (rendererPrincipal != null)
        {
            Color c = coloresCoche[Random.Range(0, coloresCoche.Length)];
            _mpbCoche = new MaterialPropertyBlock();
            _mpbCoche.SetColor("_BaseColor", c);
            _mpbCoche.SetColor("_Color",     c);
            rendererPrincipal.SetPropertyBlock(_mpbCoche);
        }
    }

    // ─── Modelo 3D externo ───────────────────────────────────────────────

    /// <summary>
    /// Instancia el prefab visual como hijo del GO del vehículo,
    /// desactiva sus colliders internos (el BoxCollider del padre es el real)
    /// y normaliza la escala para que mida ~4.5 m de largo.
    /// </summary>
    private void InstanciarModeloPrefab()
    {
        try
        {
            var go = Object.Instantiate(prefabModelo, transform);
            go.name = "_ModeloVehiculo";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            // Normalizar escala: medir bbox y ajustar a longitudObjetivo
            float longitudObjetivo = 4.5f;
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                float dim = Mathf.Max(b.size.x, b.size.z);
                if (dim > 0.001f)
                    go.transform.localScale = Vector3.one * (longitudObjetivo / dim);
            }

            // Desactivar colliders del modelo (el BoxCollider del padre es el físico)
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                col.enabled = false;

            AlsasuaLogger.Info("VehiculoNPC", $"{name}: modelo 3D '{prefabModelo.name}' instanciado.");
        }
        catch (System.Exception ex)
        {
            AlsasuaLogger.Warn("VehiculoNPC", $"{name}: error instanciando prefab '{prefabModelo.name}' — {ex.Message}. Usando malla básica.");
            prefabModelo = null;
            CrearCuerpoBasico_Inline();
        }
    }

    /// <summary>
    /// Crea la malla simple de cajas/cilindros en el GO actual (fallback sin prefab).
    /// Idéntico a CrearCocheBasico pero como método de instancia para reutilizar desde Awake().
    /// </summary>
    private void CrearCuerpoBasico_Inline()
    {
        // Carrocería
        var cuerpo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cuerpo.transform.SetParent(transform);
        cuerpo.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        cuerpo.transform.localScale    = new Vector3(1.8f, 0.8f, 4.2f);
        { var c = cuerpo.GetComponent<Collider>(); if (c != null) { if (Application.isPlaying) Object.Destroy(c); else Object.DestroyImmediate(c); } }

        // Techo
        var techo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        techo.transform.SetParent(transform);
        techo.transform.localPosition = new Vector3(0f, 1.05f, -0.2f);
        techo.transform.localScale    = new Vector3(1.6f, 0.55f, 2.4f);
        { var c = techo.GetComponent<Collider>(); if (c != null) { if (Application.isPlaying) Object.Destroy(c); else Object.DestroyImmediate(c); } }

        // Ruedas (4)
        float[] posX = { -1.0f, 1.0f, -1.0f, 1.0f };
        float[] posZ = {  1.3f, 1.3f, -1.3f,-1.3f };
        for (int i = 0; i < 4; i++)
        {
            var rueda = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rueda.transform.SetParent(transform);
            rueda.transform.localPosition = new Vector3(posX[i], 0.22f, posZ[i]);
            rueda.transform.localScale    = new Vector3(0.35f, 0.22f, 0.35f);
            rueda.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var mpbR = new MaterialPropertyBlock();
            mpbR.SetColor("_BaseColor", new Color(0.1f, 0.1f, 0.1f));
            mpbR.SetColor("_Color",     new Color(0.1f, 0.1f, 0.1f));
            rueda.GetSafe<Renderer>()?.SetPropertyBlock(mpbR);
            { var c = rueda.GetComponent<Collider>(); if (c != null) { if (Application.isPlaying) Object.Destroy(c); else Object.DestroyImmediate(c); } }
        }

        // Asegurar BoxCollider en el GO raíz
        if (GetComponent<BoxCollider>() == null)
        {
            var boxCol = gameObject.AddComponent<BoxCollider>();
            boxCol.center = new Vector3(0, 0.45f, 0);
            boxCol.size   = new Vector3(1.8f, 1.4f, 4.2f);
        }
    }

    private void FixedUpdate()
    {
        if (_destruido || waypoints.Count == 0) return;

        DetectarObstaculos();
        MoverHaciaWaypoint();
        CambiarWaypointSiLlegamos();
        AplicarGravedadTerrenoVehiculo();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MOVIMIENTO
    // ═══════════════════════════════════════════════════════════════════════

    private void MoverHaciaWaypoint()
    {
        if (wpActual >= waypoints.Count) return;

        Transform destino = waypoints[wpActual];
        if (destino == null) { wpActual++; return; }

        Vector3 dir = (destino.position - transform.position);
        dir.y = 0f;

        // Girar hacia el destino
        if (dir.magnitude > 0.1f)
        {
            Quaternion rotObjetivo = Quaternion.LookRotation(dir.normalized);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, rotObjetivo,
                velocidadGiro * Time.fixedDeltaTime);
        }

        // Acelerar / frenar
        float velObjetivo = frenando ? 0f : velocidadMax;
        velocidadActual = Mathf.MoveTowards(velocidadActual, velObjetivo,
                                             aceleracion * Time.fixedDeltaTime);

        _rb.MovePosition(_rb.position + transform.forward * velocidadActual * Time.fixedDeltaTime);
    }

    private void CambiarWaypointSiLlegamos()
    {
        if (wpActual >= waypoints.Count) return;

        // BUG FIX: evitar NullReferenceException si el waypoint fue destruido o no fue asignado
        if (waypoints[wpActual] == null) { wpActual++; return; }

        float dist = Vector3.Distance(
            new Vector3(transform.position.x, 0, transform.position.z),
            new Vector3(waypoints[wpActual].position.x, 0, waypoints[wpActual].position.z));

        if (dist < distanciaWP)
        {
            wpActual++;
            if (wpActual >= waypoints.Count)
            {
                if (bucleWaypoints) wpActual = 0;
                else { velocidadActual = 0f; enabled = false; }
            }
        }
    }

    // FIX ASCENSO CESIUM: cuando los tiles de Cesium cargan un colisionador sobre el vehículo,
    // el Rigidbody es empujado hacia arriba sin freno (no hay restricción Y).
    // Este método detecta si el coche ha subido más de 0.4 m por encima del suelo real
    // (medido con raycast) y aplica un impulso hacia abajo para corregirlo.
    private void AplicarGravedadTerrenoVehiculo()
    {
        // Raycast desde el centro del coche hacia abajo — hasta 4 m (tiles y suelo)
        Vector3 origen = _rb.position + Vector3.up * 0.8f;
        if (Physics.Raycast(origen, Vector3.down, out RaycastHit hit, 5f,
            ~LayerMask.GetMask("Ignore Raycast"), QueryTriggerInteraction.Ignore))
        {
            float deltaY = _rb.position.y - hit.point.y;

            if (deltaY > 0.4f)
            {
                // El coche está más de 0.4 m sobre el suelo → añadir impulso hacia abajo
                // proporcional a cuánto ha subido. Damping extra evita oscilación.
                var vel = _rb.linearVelocity;
                if (vel.y > 0f) vel.y = 0f;                    // cancelar velocidad ascendente
                vel.y -= Mathf.Min(deltaY * 3f, 8f);           // impulso proporcional hacia abajo
                _rb.linearVelocity = vel;
            }
            else if (deltaY < -0.1f)
            {
                // El coche se ha hundido (nuevo tile más alto debajo) → snapear hacia arriba
                Vector3 pos = _rb.position;
                pos.y = hit.point.y + 0.05f;
                rb.MovePosition(pos);
                var vel = _rb.linearVelocity;
                vel.y = 0f;
                _rb.linearVelocity = vel;
            }
        }
    }

    private void DetectarObstaculos()
    {
        // BUG 19 FIX: anguloDeteccion estaba declarado como SerializeField pero NUNCA se usaba.
        // Ahora se usa para un cono de 3 rayos (centro + flancos) que mejora la detección
        // de obstáculos angulados respecto al vehículo.
        Vector3 origen   = transform.position + Vector3.up * 0.5f;
        Quaternion izq   = Quaternion.Euler(0f, -anguloDeteccion * 0.5f, 0f);
        Quaternion der   = Quaternion.Euler(0f,  anguloDeteccion * 0.5f, 0f);

        frenando = Physics.Raycast(origen, transform.forward,       distanciaFreno, capasObstaculo)
                || Physics.Raycast(origen, izq * transform.forward, distanciaFreno, capasObstaculo)
                || Physics.Raycast(origen, der * transform.forward, distanciaFreno, capasObstaculo);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DAÑO
    // ═══════════════════════════════════════════════════════════════════════

    protected override void OnDanoRecibido(int cantidad, Vector3 origen, TipoDano tipo)
    {
        // Oscurecer carrocería proporcionalmente al daño acumulado (sin crear instancias de Material)
        if (rendererPrincipal != null && _mpbCoche != null)
        {
            rendererPrincipal.GetPropertyBlock(_mpbCoche);
            Color colorActual = _mpbCoche.GetColor("_BaseColor");
            Color colorOscuro = Color.Lerp(colorActual, Color.black,
                                           0.3f * ((float)(vidaMaxima - _vida) / vidaMaxima));
            _mpbCoche.SetColor("_BaseColor", colorOscuro);
            _mpbCoche.SetColor("_Color",     colorOscuro);
            rendererPrincipal.SetPropertyBlock(_mpbCoche);
        }
    }

    protected override void IniciarDestruccion() => Destruir();

    // Evento global — las misiones lo escuchan para contar destrucciones
    public static event System.Action<VehiculoNPC> OnVehiculoDestruido;

    private void Destruir()
    {
        _destruido = true;
        velocidadActual = 0f;
        OnVehiculoDestruido?.Invoke(this);

        // Explosión del vehículo
        SistemaExplosion.Explotar(transform.position + Vector3.up, 8f, 400f, 80);

        // Oscurecer completamente — FIX: usar MaterialPropertyBlock para no crear instancias
        // de material por cada sub-renderer del vehículo (carrocería + techo + 4 ruedas = 6).
        // MaterialPropertyBlock es per-renderer, sin creación de Material ni GC.
        var pb = new MaterialPropertyBlock();
        pb.SetColor("_BaseColor", new Color(0.08f, 0.06f, 0.05f));
        pb.SetColor("_Color",     new Color(0.08f, 0.06f, 0.05f));  // fallback para shader Standard
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.SetPropertyBlock(pb);

        // Desactivar física controlada
        _rb.constraints = RigidbodyConstraints.None;
        // FIX TEST T21-T23: Destroy() no puede llamarse en edit-mode (tests unitarios).
        // En play-mode usamos Destroy con delay de 15 s para que la explosión sea visible.
        // En edit-mode (tests) usamos DestroyImmediate sincrono para limpiar correctamente.
        if (Application.isPlaying) Destroy(gameObject, 15f);
        else DestroyImmediate(gameObject);
        enabled = false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  UTILIDAD ESTÁTICA: crear coche con forma básica por código
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Crea un VehiculoNPC en la posición indicada con waypoints de prueba.
    /// Útil para instanciar desde SetupEscenaAlsasua.
    /// </summary>
    public static GameObject CrearCocheBasico(Vector3 posicion, List<Transform> ruta)
    {
        var go = new GameObject("CocheNPC");
        go.transform.position = posicion;

        // Carrocería
        var cuerpo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cuerpo.transform.SetParent(go.transform);
        cuerpo.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        cuerpo.transform.localScale    = new Vector3(1.8f, 0.8f, 4.2f);
        { var c = cuerpo.GetComponent<Collider>(); if (c != null) { if (Application.isPlaying) Object.Destroy(c); else Object.DestroyImmediate(c); } }

        // Techo
        var techo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        techo.transform.SetParent(go.transform);
        techo.transform.localPosition = new Vector3(0f, 1.05f, -0.2f);
        techo.transform.localScale    = new Vector3(1.6f, 0.55f, 2.4f);
        { var c = techo.GetComponent<Collider>(); if (c != null) { if (Application.isPlaying) Object.Destroy(c); else Object.DestroyImmediate(c); } }

        // Ruedas (4)
        float[] posX = { -1.0f, 1.0f, -1.0f, 1.0f };
        float[] posZ = { 1.3f, 1.3f, -1.3f, -1.3f };
        for (int i = 0; i < 4; i++)
        {
            var rueda = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rueda.transform.SetParent(go.transform);
            rueda.transform.localPosition = new Vector3(posX[i], 0.22f, posZ[i]);
            rueda.transform.localScale    = new Vector3(0.35f, 0.22f, 0.35f);
            rueda.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            // FIX LEAK: 4 accesos a .material en el bucle crean 4 instancias de Material
            // que nunca se destruyen. Usar MaterialPropertyBlock por rueda → cero instancias.
            var mpbRueda = new MaterialPropertyBlock();
            mpbRueda.SetColor("_BaseColor", new Color(0.1f, 0.1f, 0.1f));
            mpbRueda.SetColor("_Color",     new Color(0.1f, 0.1f, 0.1f));
            rueda.GetSafe<Renderer>()?.SetPropertyBlock(mpbRueda);
            { var c = rueda.GetComponent<Collider>(); if (c != null) { if (Application.isPlaying) Object.Destroy(c); else Object.DestroyImmediate(c); } }
        }

        // Colisionador principal
        var boxCol = go.AddComponent<BoxCollider>();
        boxCol.center = new Vector3(0, 0.45f, 0);
        boxCol.size   = new Vector3(1.8f, 1.4f, 4.2f);

        // Rigidbody y script
        var vehiculo = go.AddComponent<VehiculoNPC>();
        vehiculo.waypoints = ruta;
        // vidaMaxima se ajusta en el Inspector; vida se inicializa en Awake de VehiculoBase

        return go;
    }
}
