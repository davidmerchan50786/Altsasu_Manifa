// Assets/Scripts/_ControlesGC~/ControlGuardiaCivil.cs  (STAGING/DRAFT — carpeta ~ no compila)
// ─────────────────────────────────────────────────────────────────────────────
//  Un CONTROL de carretera de la Guardia Civil: corta un punto de paso (calle,
//  puente, salida de la N-1). Cuando está ACTIVO (lo enciende el manager según la
//  paranoia) y el jugador lo cruza con búsqueda activa, le dan el alto:
//    · apoyo alto → la calle te cuela (te dejan pasar)
//    · búsqueda baja → cacheo: sube paranoia y un punto de búsqueda
//    · búsqueda alta (≥ umbralArresto) → ARRESTO (PlayerArrestedEvent)
//  El manager lo activa/desactiva SIEMPRE fuera de cámara (montar un control
//  delante del jugador rompería la inmersión).
// ─────────────────────────────────────────────────────────────────────────────
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ControlGuardiaCivil : MonoBehaviour
{
    [Tooltip("Búsqueda (0-5) a partir de la cual te arrestan en vez de cachearte.")]
    public int  umbralArresto = 3;
    [Tooltip("Hijo visual (barrera/cono/foco). Se enciende solo cuando el control está activo.")]
    public GameObject barrera;
    [Tooltip("Panel único de tuning. Si se asigna, manda sobre umbralArresto y prob. de pasar.")]
    public SintoniaAltsasu sintonia;

    public bool Activo { get; private set; }
    Collider _trigger;
    float _ultimoAlto;   // anti-spam: un alto cada X s

    void Awake()
    {
        _trigger = GetComponent<Collider>();
        _trigger.isTrigger = true;
        Desactivar();
    }

    public void Activar()
    {
        Activo = true;
        if (barrera) barrera.SetActive(true);
        AlsasuaLogger.Info("ControlGC", $"Control activo en {transform.position}");
    }

    public void Desactivar()
    {
        Activo = false;
        if (barrera) barrera.SetActive(false);
    }

    /// <summary>¿El control es visible ahora mismo por la cámara? (para no montarlo en pantalla)</summary>
    public bool VisibleEnCamara()
    {
        var cam = Camera.main;
        if (cam == null) return false;
        Vector3 vp = cam.WorldToViewportPoint(transform.position);
        return vp.z > 0f && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!Activo) return;
        if (Time.time - _ultimoAlto < 4f) return;
        if (other.GetComponentInParent<ControladorJugador>() == null) return;

        var wanted = ServiceLocator.Get<IWantedSystem>();
        int nivel = wanted != null ? wanted.NivelBusqueda : 0;
        if (nivel < 1) return;                       // sin búsqueda, no eres nadie: pasas

        _ultimoAlto = Time.time;
        float apoyo = SistemaApoyoPopular.Instance != null ? SistemaApoyoPopular.Instance.apoyo : 0f;

        // La calle te cuela: prob. de que te dejen pasar crece con el apoyo.
        float probPasar = sintonia != null ? sintonia.ControlProbPasar(apoyo)
                                           : Mathf.Lerp(0.05f, 0.6f, apoyo / 100f);
        if (Random.value < probPasar)
        {
            AlsasuaLogger.Info("ControlGC", "Un picoleto te hace señas… pasa, pasa (apoyo).");
            return;
        }

        int umbral = sintonia != null ? sintonia.controlUmbralArresto : umbralArresto;
        if (nivel >= umbral) Arrestar();
        else                 Cacheo(wanted);
    }

    void Cacheo(IWantedSystem wanted)
    {
        AlsasuaLogger.Info("ControlGC", "¡Alto! Documentación. Te cachean.");
        SistemaApoyoPopular.Instance?.SumarParanoia(8f);
        wanted?.AumentarBusqueda(1);                 // se ponen nerviosos
    }

    void Arrestar()
    {
        AlsasuaLogger.Info("ControlGC", "¡Manos en el capó! Arrestado en el control.");
        EventBus.Publish(new PlayerArrestedEvent { posicion = transform.position, policia = "Control GC" });
    }
}
