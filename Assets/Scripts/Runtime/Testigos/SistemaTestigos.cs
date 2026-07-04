// Assets/Scripts/_Testigos~/SistemaTestigos.cs  (STAGING/DRAFT — carpeta ~ no se compila)
// ─────────────────────────────────────────────────────────────────────────────
//  "Aquí nos conocemos todos": ante un delito, los vecinos que lo VEN deciden si
//  te delatan (sube wanted/paranoia) o te cubren. La probabilidad de chivarse baja
//  con el APOYO popular (calle alta = nadie te vende). Espejo de la coartada.
//
//  El código de delito llama:  SistemaTestigos.ReportarDelito(lugar, gravedad)
//  (en los mismos puntos donde ya hacéis SumarParanoia: SistemaConsecuencias,
//   SistemaDestruccion, robo de coche, pintada agresiva, etc.). gravedad ~ 0..1.
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.Generic;
using UnityEngine;

public class SistemaTestigos : MonoBehaviour
{
    public static SistemaTestigos Instance { get; private set; }

    [Tooltip("Capa de muros/obstáculos para la línea de visión del testigo.")]
    public LayerMask capaObstaculos;
    public float rangoTestigo = 18f;
    public float alturaOjos   = 1.6f;
    public float retardoReporte = 3f;

    [Header("Probabilidad de chivarse según apoyo")]
    [Range(0f, 1f)] public float probApoyo0   = 0.9f;   // apoyo 0 → casi todos chivan
    [Range(0f, 1f)] public float probApoyo100  = 0.05f;  // apoyo 100 → casi nadie

    [Tooltip("Panel único de tuning. Si se asigna, manda sobre rango/retardo/probabilidad.")]
    public SintoniaAltsasu sintonia;

    static readonly List<TestigoNPC> _testigos = new();
    public static void Registrar(TestigoNPC t)   { if (!_testigos.Contains(t)) _testigos.Add(t); }
    public static void Desregistrar(TestigoNPC t) => _testigos.Remove(t);

    void Awake() { if (Instance != null && Instance != this) { Destroy(this); return; } Instance = this; }

    // Se suscribe al evento de delito → reacciona sin que el código de delito
    // dependa de este sistema (desacoplado vía EventBus).
    void OnEnable()  => EventBus.Subscribe<DelitoEvent>(OnDelito);
    void OnDisable() => EventBus.Unsubscribe<DelitoEvent>(OnDelito);
    void OnDelito(DelitoEvent e) => Procesar(e.lugar, Mathf.Clamp01(e.gravedad));

    /// <summary>Atajo directo (además del evento). gravedad ~ 0..1.</summary>
    public static void ReportarDelito(Vector3 lugar, float gravedad)
        => Instance?.Procesar(lugar, Mathf.Clamp01(gravedad));

    void Procesar(Vector3 lugar, float gravedad)
    {
        float apoyo = SistemaApoyoPopular.Instance != null ? SistemaApoyoPopular.Instance.apoyo : 0f;
        float rango   = sintonia != null ? sintonia.testigoRango          : rangoTestigo;
        float retardo = sintonia != null ? sintonia.testigoRetardoReporte : retardoReporte;
        float probChivar = sintonia != null
            ? sintonia.TestigoProbChivar(apoyo, gravedad)
            : Mathf.Lerp(probApoyo0, probApoyo100, apoyo / 100f) * Mathf.Lerp(0.3f, 1f, gravedad);

        Vector3 ojoLugar = lugar + Vector3.up * alturaOjos;
        for (int i = 0; i < _testigos.Count; i++)
        {
            var t = _testigos[i];
            if (t == null || t.Ocupado) continue;
            if (GeoDataAlsasua.Dist2D(t.transform.position, lugar) > rango) continue;

            Vector3 ojoTestigo = t.transform.position + Vector3.up * alturaOjos;
            if (Physics.Linecast(ojoTestigo, ojoLugar, capaObstaculos)) continue;   // muro de por medio → no vio

            if (Random.value < probChivar) t.Chivarse(lugar, gravedad, retardo);
            else                            t.Cubrir();
        }
    }
}
