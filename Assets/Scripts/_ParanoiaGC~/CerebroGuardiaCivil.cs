// Assets/Scripts/_ParanoiaGC~/CerebroGuardiaCivil.cs  (STAGING/DRAFT — carpeta ~ no se compila)
// ─────────────────────────────────────────────────────────────────────────────
//  Cerebro de Guardia Civil: variante MÁS AGRESIVA que la Policía Foral, cuya
//  agresividad (rango de detección + velocidad) ESCALA con la paranoia global.
//  Solo está activo mientras el NPC está "convertido" (lo habilita
//  ConvertibleGuardiaCivil). Arresta publicando PlayerArrestedEvent.
//
//  Facción propia (GuardiaCivil) distinta de la Policía Foral. Movimiento por
//  NavMeshAgent si existe; si no, MoveTowards básico (barato).
// ─────────────────────────────────────────────────────────────────────────────
using UnityEngine;
using UnityEngine.AI;

public class CerebroGuardiaCivil : MonoBehaviour
{
    [Header("Base (a paranoia mínima)")]
    public float rangoBase   = 14f;
    public float velocidadBase = 3.2f;
    [Header("Máximo (a paranoia crítica)")]
    public float rangoMax    = 28f;
    public float velocidadMax = 5.5f;
    [Header("Arresto")]
    public float distanciaArresto = 2.2f;
    public float cooldownArresto  = 6f;

    NavMeshAgent _agent;
    float _ultimoArresto = -999f;

    void Awake() => _agent = GetComponent<NavMeshAgent>();

    void OnEnable()  { if (_agent) _agent.isStopped = false; }
    void OnDisable() { if (_agent) _agent.isStopped = true; }

    void Update()
    {
        // Solo persigue si hay búsqueda activa (wanted) — si no, patrulla pasiva.
        var wanted = ServiceLocator.Get<IWantedSystem>();
        int nivel = wanted?.NivelBusqueda ?? 0;

        float agr = Agresividad();                          // 0..1 según paranoia
        float rango = Mathf.Lerp(rangoBase, rangoMax, agr);
        float vel   = Mathf.Lerp(velocidadBase, velocidadMax, agr);

        Vector3 jug = GeoDataAlsasua.JugadorPos();
        if (jug == Vector3.zero) return;
        float d = GeoDataAlsasua.Dist2D(transform.position, jug);

        if (nivel <= 0 || d > rango) return;                // no detectado: nada

        // Perseguir
        if (_agent && _agent.isOnNavMesh) { _agent.speed = vel; _agent.SetDestination(jug); }
        else
        {
            Vector3 obj = new Vector3(jug.x, transform.position.y, jug.z);
            transform.position = Vector3.MoveTowards(transform.position, obj, vel * Time.deltaTime);
            Vector3 mir = obj - transform.position; mir.y = 0;
            if (mir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(
                transform.rotation, Quaternion.LookRotation(mir), 6f * Time.deltaTime);
        }

        // Arrestar
        if (d <= distanciaArresto && Time.time - _ultimoArresto > cooldownArresto)
        {
            _ultimoArresto = Time.time;
            EventBus.Publish(new PlayerArrestedEvent { posicion = jug, policia = "GuardiaCivil" });
            AlsasuaLogger.Info("GC", $"{name}: arresto (paranoia {Paranoia():F0}).");
        }
    }

    static float Paranoia() => SistemaApoyoPopular.Instance != null ? SistemaApoyoPopular.Instance.paranoia : 0f;

    /// <summary>0 por debajo del umbral, 1 en crítica.</summary>
    float Agresividad()
    {
        float p = Paranoia();
        float u0 = 70f, u1 = 90f;
        if (SistemaParanoiaGuardiaCivil.Instance?.config != null)
        { u0 = SistemaParanoiaGuardiaCivil.Instance.config.umbralInicio;
          u1 = SistemaParanoiaGuardiaCivil.Instance.config.umbralCritico; }
        return Mathf.Clamp01((p - u0) / Mathf.Max(0.01f, u1 - u0));
    }
}
