// Assets/Scripts/NPCCivil.cs
// ═══════════════════════════════════════════════════════════════════════════
//  NPC civil — camina por la ciudad, huye cuando hay disparos.
//
//  Comportamientos:
//    · Idle:     espera 2-5s en el sitio
//    · Caminando: se mueve a un punto aleatorio en radio 50m
//    · Huyendo:  si escucha un disparo (<30m), corre en dirección contraria
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class NPCCivil : NPCBase
{
    [Header("Movimiento civil")]
    public float radioDeambulacion = 50f;
    public float velocidadAndar    = 1.4f;
    public float velocidadHuida    = 4.2f;

    [Header("Reacción")]
    public float radioEscucha = 30f;

    [Header("Paranoia — GC infiltrado")]
    [Tooltip("Radio al que se evalúa si este NPC es un GC disfrazado (requiere paranoia alta).")]
    public float radioInfiltracion = 8f;
    [Tooltip("Segundos entre evaluaciones de EsGCDisfrazado para no llamarlo cada frame.")]
    public float intervaloCheckInfiltrado = 3f;

    // ── Estado ────────────────────────────────────────────────────────────
    private enum Estado { Idle, Caminando, Huyendo, GCRevelado }
    private Estado _estado = Estado.Idle;
    private float  _timerEstado;
    private float  _timerCheckInfiltrado;
    private bool   _esInfiltrado;   // true = se reveló como GC este ciclo de vida

    // ════════════════════════════════════════════════════════════════════════

    protected override void Awake()
    {
        velocidadBase   = velocidadAndar;
        velocidadMaxima = velocidadHuida;
        base.Awake();
    }

    protected override void OnStart()
    {
        _timerEstado = Random.Range(0f, 3f); // offset para no sincronizarse todos
    }

    protected override void AlActivarAgente() => CambiarEstado(Estado.Idle);

    // ════════════════════════════════════════════════════════════════════════
    //  MÁQUINA DE ESTADOS
    // ════════════════════════════════════════════════════════════════════════

    protected override void ActualizarComportamiento()
    {
        _timerEstado -= Time.deltaTime;

        switch (_estado)
        {
            case Estado.Idle:
                if (_timerEstado <= 0f) CambiarEstado(Estado.Caminando);
                break;

            case Estado.Caminando:
                if (!_agente.pathPending && _agente.remainingDistance < 0.8f)
                    CambiarEstado(Estado.Idle);
                if (_timerEstado <= 0f)
                    CambiarEstado(Estado.Caminando);
                break;

            case Estado.Huyendo:
                if (_timerEstado <= 0f) CambiarEstado(Estado.Caminando);
                if (!_agente.pathPending && _agente.remainingDistance < 0.5f)
                    CambiarEstado(Estado.Idle);
                break;
        }

        // Separación del jugador (evita que el NPC bloquee la cámara)
        if (_jugador != null && Vector3.Distance(transform.position, _jugador.position) < 2f)
            HuirDe(_jugador.position);

        // ── Detección de GC infiltrado ────────────────────────────────────
        // Solo evaluar si el jugador está cerca y no se ha revelado ya.
        // Se hace con un timer para no llamar EsGCDisfrazado() cada frame.
        if (!_esInfiltrado && _jugador != null && _estado != Estado.GCRevelado)
        {
            _timerCheckInfiltrado -= Time.deltaTime;
            if (_timerCheckInfiltrado <= 0f)
            {
                _timerCheckInfiltrado = intervaloCheckInfiltrado;
                float dist = Vector3.Distance(transform.position, _jugador.position);
                if (dist < radioInfiltracion)
                {
                    var apoyo = SistemaApoyoPopular.Instance;
                    if (apoyo != null && apoyo.EsGCDisfrazado(gameObject))
                        RevelarComoGC();
                }
            }
        }
    }

    private void CambiarEstado(Estado nuevo)
    {
        _estado = nuevo;
        switch (nuevo)
        {
            case Estado.Idle:
                _agente.isStopped = true;
                _timerEstado = Random.Range(2f, 5f);
                break;

            case Estado.Caminando:
                _agente.isStopped = false;
                _agente.speed = velocidadAndar;
                _timerEstado  = 15f;
                Vector3 destino = PuntoAleatorioNavMesh(transform.position, radioDeambulacion);
                if (destino != Vector3.zero) _agente.SetDestination(destino);
                break;

            case Estado.Huyendo:
                _agente.isStopped = false;
                _agente.speed     = velocidadHuida;
                _timerEstado      = 8f;
                break;

            case Estado.GCRevelado:
                // El NPC revelado huye rápido y no vuelve a Idle
                _agente.isStopped = false;
                _agente.speed     = velocidadHuida * 1.5f;
                if (_jugador != null) HuirDe(_jugador.position);
                break;
        }
    }

    // ── Revelar como GC disfrazado ────────────────────────────────────────

    private void RevelarComoGC()
    {
        _esInfiltrado = true;
        _estado = Estado.GCRevelado;
        CambiarEstado(Estado.GCRevelado);

        // Feedback visual inmediato: el "civil" queda en rojo para indicar el reveal
        var renderers = GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            var mat = new Material(r.sharedMaterial);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", new Color(0.8f, 0.1f, 0.1f));
            else
                mat.color = new Color(0.8f, 0.1f, 0.1f);
            r.sharedMaterial = mat;
        }

        // Sube el wanted (llamó a 