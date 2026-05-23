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

    // ── Estado ────────────────────────────────────────────────────────────
    private enum Estado { Idle, Caminando, Huyendo }
    private Estado _estado = Estado.Idle;
    private float  _timerEstado;

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
        }
    }

    // ── API pública ───────────────────────────────────────────────────────

    /// <summary>El GameManager llama esto cuando hay un disparo cerca.</summary>
    public void AlertarDisparo(Vector3 origenDisparo)
    {
        if (Vector3.Distance(transform.position, origenDisparo) > radioEscucha) return;
        HuirDe(origenDisparo);
        CambiarEstado(Estado.Huyendo);
    }

    // ── Cuerpo procedural ─────────────────────────────────────────────────

    protected override void CrearCuerpoFallback()
    {
        Color[] colores = {
            new Color(0.2f,0.3f,0.6f), new Color(0.6f,0.2f,0.2f),
            new Color(0.2f,0.6f,0.3f), new Color(0.5f,0.5f,0.15f),
            new Color(0.15f,0.15f,0.15f), new Color(0.8f,0.7f,0.6f)
        };
        Color ropa = colores[Random.Range(0, colores.Length)];
        Color piel = new Color(0.85f, 0.72f, 0.58f);

        var raiz = new GameObject("_Cuerpo");
        raiz.transform.SetParent(transform, false);

        Parte(raiz, "Tronco",  new Vector3(0f,   1.0f, 0f), new Vector3(0.35f, 0.55f, 0.2f),  ropa);
        Parte(raiz, "Cabeza",  new Vector3(0f,   1.7f, 0f), new Vector3(0.22f, 0.22f, 0.22f), piel);
        Parte(raiz, "PiernaI", new Vector3(-0.1f,0.38f,0f), new Vector3(0.14f, 0.75f, 0.14f), ropa);
        Parte(raiz, "PiernaD", new Vector3( 0.1f,0.38f,0f), new Vector3(0.14f, 0.75f, 0.14f), ropa);
        Parte(raiz, "BrazoI",  new Vector3(-0.25f,1.05f,0f),new Vector3(0.12f, 0.5f, 0.12f),  ropa);
        Parte(raiz, "BrazoD",  new Vector3( 0.25f,1.05f,0f),new Vector3(0.12f, 0.5f, 0.12f),  ropa);
    }

    private static void Parte(GameObject raiz, string nombre, Vector3 localPos, Vector3 escala, Color color)
    {
        var go  = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = nombre;
        go.transform.SetParent(raiz.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale    = escala;
        var mat = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        else mat.color = color;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.Destroy(go.GetComponent<Collider>());
    }
}
