// Assets/Scripts/SistemaReaccionVital.cs
using UnityEngine;
using UnityEngine.AI;
using System.Collections;

public enum EstadoVital { Tranquilo, Panico_Huyendo, Ardiendo, Cenizas }

[AddComponentMenu("Alsasua V12/Cerebro Biomecánico de Supervivencia")]
public class SistemaReaccionVital : MonoBehaviour
{
    private NavMeshAgent agente;
    private NavegacionAnimalIA navBase;
    public EstadoVital estadoActual = EstadoVital.Tranquilo;
    
    private float tiempoCombustion = 0f;
    private float maxCombustion = 4.5f;
    private GameObject fuegoAdherido;

    public void RecibirImpactoBalistico()
    {
        if (estadoActual == EstadoVital.Cenizas) return;
        
        // V13: Balística de Supervivencia
        DetectarPeligro(transform.position); // Grita y huye
        
        // Desconectar animación/rutina y activar físicas crudas para tropezar
        if (agente != null) { agente.isStopped = true; agente.enabled = false; }
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.mass = 80f; // Peso humano
        
        // V17 AUDIT FIX: Iniciar ciclo de checkeo para que el NPC se recupere si no arde
        StartCoroutine(RecuperacionRagdollSegura());
    }

    private IEnumerator RecuperacionRagdollSegura()
    {
        yield return new WaitForSeconds(3.5f); // Tiempo promedio de vuelo balístico y estabilización en el suelo
        if (estadoActual == EstadoVital.Ardiendo || estadoActual == EstadoVital.Cenizas) yield break;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        // V17 AUDITORÍA: Validar si ha caído en una zona ruteable o en un tejado inalcanzable.
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
        {
            transform.position = hit.position; // Teleportación sutil al grid real del NavMesh para evitar bugs C++
            if (agente != null)
            {
                agente.enabled = true;
                DetectarPeligro(transform.position + Random.insideUnitSphere * 10f); // Reiniciar huida
            }
        }
        else
        {
            // Ha caído fuera del mapa. Para evitar la excepción fatal "SetDestination can only be called on an active agent"
            // Desintegramos la IA por completo simulando muerte por aplastamiento/caída masiva.
            ConvertirEnCenizas();
        }
    }

    private void Start()
    {
        agente = GetComponent<NavMeshAgent>();
        navBase = GetComponent<NavegacionAnimalIA>();
    }

    public void DetectarPeligro(Vector3 origenAmenaza)
    {
        if (estadoActual == EstadoVital.Tranquilo || estadoActual == EstadoVital.Panico_Huyendo)
        {
            estadoActual = EstadoVital.Panico_Huyendo;
            
            // Profiling V12: Safe-check de CPU si no hay NavMesh (Evita cuello de botella masivo)
            if (agente != null && agente.isOnNavMesh)
            {
                // Calcular vector de huida
                Vector3 direccionHuida = (transform.position - origenAmenaza).normalized;
                Vector3 puntoDestino = transform.position + direccionHuida * 50f;

                // Encontrar punto seguro real y correr al doble de velocidad
                if (NavMesh.SamplePosition(puntoDestino, out NavMeshHit hit, 20f, NavMesh.AllAreas))
                {
                    agente.speed *= 2.5f;
                    agente.SetDestination(hit.position);
                    if(navBase != null) navBase.enabled = false; // Anular patrulla errática
                }
            }
        }
    }

    public void PrenderFuegoVivo()
    {
        if (estadoActual == EstadoVital.Ardiendo || estadoActual == EstadoVital.Cenizas) return;

        estadoActual = EstadoVital.Ardiendo;
        
        // El NPC enloquece y corre más rápido aún, gimiendo
        if (agente != null && agente.isOnNavMesh)
        {
            agente.speed *= 1.5f;
            Vector3 correrLoko = transform.position + new Vector3(Random.Range(-30f, 30f), 0, Random.Range(-30f, 30f));
            if (NavMesh.SamplePosition(correrLoko, out NavMeshHit locoHit, 20f, NavMesh.AllAreas))
            {
                agente.SetDestination(locoHit.position);
            }
        }

        // Instanciar malla de fuego pegada al cuerpo
        fuegoAdherido = new GameObject("Cuerpo_En_Llamas\_Gore");
        fuegoAdherido.transform.SetParent(this.transform);
        fuegoAdherido.transform.localPosition = Vector3.up * 1f;

        ParticleSystem ps = fuegoAdherido.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = maxCombustion;
        main.loop = false;  // Termina la vida al mismo tiempo que las cenizas
        main.startLifetime = 1f;
        main.startSize = 2f;
        main.startColor = new Color(1f, 0.3f, 0f, 0.9f); // Naranja brillante fuego puro
        var em = ps.emission; em.rateOverTime = 60f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 1.5f;
        ps.Play();
    }

    private void Update()
    {
        if (estadoActual == EstadoVital.Ardiendo)
        {
            tiempoCombustion += Time.deltaTime;

            if (tiempoCombustion >= maxCombustion)
            {
                ConvertirEnCenizas();
            }
        }
        else if (estadoActual == EstadoVital.Cenizas)
        {
            // V12: Contracción celular hasta desaparecer
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.zero, Time.deltaTime * 2f);
            if (transform.localScale.y < 0.05f)
            {
                // Dejar mancha de carbón (Opcional, usando instancia de humo estático)
                GameObject cenizasMancha = GameObject.CreatePrimitive(PrimitiveType.Quad);
                cenizasMancha.name = "Mancha_Carbonizada_Piso";
                cenizasMancha.transform.position = transform.position + Vector3.up * 0.1f;
                cenizasMancha.transform.rotation = Quaternion.Euler(90, 0, Random.Range(0f, 360f));
                cenizasMancha.transform.localScale = new Vector3(2f, 2f, 1f);
                Destroy(cenizasMancha.GetComponent<Collider>());
                cenizasMancha.GetComponent<Renderer>().material.color = Color.black; // Decal de carbón

                Destroy(gameObject); // El ente deja de existir físicamente
            }
        }
    }

    private void ConvertirEnCenizas()
    {
        estadoActual = EstadoVital.Cenizas;
        
        // Quitar IA e inmovilizar al muerto
        if (agente != null) { agente.isStopped = true; agente.enabled = false; }
        
        // Tintar el material principal proceduralmente de negro tizón
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach(Renderer r in renderers)
        {
            r.material.color = Color.black; 
        }

        // Simular colapso gravitatorio
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.mass = 20f; // Frágil

        // Suelta un último estertor visual
        SintetizadorGore.EsparcirSangre(transform.position, 0.5f);
    }
}
