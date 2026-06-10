// Assets/Scripts/SistemaDiaNocheReal.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DÍA/NOCHE DINÁMICO — runtime
//
//  Rota el sol según una hora del día configurable (en tiempo real o acelerado).
//  Cambia color/intensidad para mediodía, atardecer y noche.
//  Activa el modo de noche en farolas y faros de vehículos.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public class SistemaDiaNocheReal : MonoBehaviour
{
    [Header("═══ TIEMPO ═══")]
    [Range(0f, 24f)] public float horaInicial = 11f;
    [Tooltip("Cuántos segundos reales = 1 hora de juego. 60 = un día son 24 minutos.")]
    public float segundosPorHoraJuego = 60f;
    [Tooltip("Si false, hora fija (debug).")]
    public bool tiempoCorre = true;

    [Header("═══ REFERENCIAS ═══")]
    public Light sol;
    public Volume volumeMaster;

    [Header("═══ COLORES SEGÚN HORA ═══")]
    public Gradient colorSol;
    public AnimationCurve intensidadSol;
    public Gradient colorAmbiente;

    [Header("═══ NOCHE ═══")]
    [Range(0f, 24f)] public float horaAnochecer = 20f;
    [Range(0f, 24f)] public float horaAmanecer  = 7.5f;

    float _horaActual;
    public float HoraActual => _horaActual;
    public bool EsDeNoche => _horaActual >= horaAnochecer || _horaActual < horaAmanecer;

    void Awake()
    {
        _horaActual = horaInicial;

        if (sol == null)
        {
            var luces = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in luces)
                if (l.type == LightType.Directional) { sol = l; break; }
        }

        if (colorSol == null || colorSol.colorKeys == null || colorSol.colorKeys.Length == 0)
            colorSol = GradienteDefaultSol();
        if (intensidadSol == null || intensidadSol.keys.Length == 0)
            intensidadSol = CurvaIntensidadDefault();
        if (colorAmbiente == null || colorAmbiente.colorKeys == null || colorAmbiente.colorKeys.Length == 0)
            colorAmbiente = GradienteDefaultAmbiente();
    }

    void Update()
    {
        if (tiempoCorre)
            _horaActual += Time.deltaTime / segundosPorHoraJuego;
        if (_horaActual >= 24f) _horaActual -= 24f;
        Aplicar();
    }

    void Aplicar()
    {
        float t = _horaActual / 24f;

        if (sol != null)
        {
            // Ángulo solar: a las 12:00 → 90° altitud. 0/24 → -90° (bajo horizonte).
            // sin(((hora-6)/12)*PI) da 0 a 6am, +1 a mediodía, 0 a 6pm, -1 a medianoche
            float altitud = Mathf.Sin(((_horaActual - 6f) / 12f) * Mathf.PI) * 90f;
            float azimuth = ((_horaActual - 6f) / 12f) * 180f; // -90 a +90 de este a oeste pasando por sur
            sol.transform.rotation = Quaternion.Euler(altitud, azimuth - 10f, 0f);

            sol.color = colorSol.Evaluate(t);
            float i = intensidadSol.Evaluate(t);
            // HDRP expects physical units on Light.intensity now; set Light directly.
            sol.intensity = i * 100f;

            // Apagar luz si está bajo horizonte
            sol.enabled = altitud > -2f;
        }

        // Ambiente
        Color amb = colorAmbiente.Evaluate(t);
        RenderSettings.ambientSkyColor       = amb * 1.0f;
        RenderSettings.ambientEquatorColor   = amb * 0.75f;
        RenderSettings.ambientGroundColor    = amb * 0.5f;
    }

    // ─────────────────────────────────────────────────────────────────────

    static Gradient GradienteDefaultSol()
    {
        var g = new Gradient();
        g.SetKeys(new GradientColorKey[] {
            new GradientColorKey(new Color(0.05f, 0.05f, 0.15f), 0.00f),       // 00h - noche
            new GradientColorKey(new Color(0.2f, 0.1f, 0.2f),    0.20f),       // 5h
            new GradientColorKey(new Color(1.0f, 0.6f, 0.35f),   0.27f),       // 6:30 amanecer
            new GradientColorKey(new Color(1.0f, 0.97f, 0.90f),  0.5f),        // 12h mediodía
            new GradientColorKey(new Color(1.0f, 0.7f, 0.35f),   0.83f),       // 20h atardecer
            new GradientColorKey(new Color(0.3f, 0.2f, 0.4f),    0.88f),       // 21h crepúsculo
            new GradientColorKey(new Color(0.05f, 0.05f, 0.15f), 1.00f),       // 24h
        }, new GradientAlphaKey[] {
            new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1)
        });
        return g;
    }

    static Gradient GradienteDefaultAmbiente()
    {
        var g = new Gradient();
        g.SetKeys(new GradientColorKey[] {
            new GradientColorKey(new Color(0.10f, 0.12f, 0.20f), 0.00f),
            new GradientColorKey(new Color(0.55f, 0.65f, 0.80f), 0.28f),
            new GradientColorKey(new Color(0.65f, 0.75f, 0.90f), 0.50f),
            new GradientColorKey(new Color(0.70f, 0.55f, 0.40f), 0.83f),
            new GradientColorKey(new Color(0.10f, 0.12f, 0.20f), 1.00f),
        }, new GradientAlphaKey[] {
            new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1)
        });
        return g;
    }

    static AnimationCurve CurvaIntensidadDefault()
    {
        // En lux HDRP físico
        return new AnimationCurve(
            new Keyframe(0.00f,     0f),    // 00h
            new Keyframe(0.25f,     0f),    // 6h
            new Keyframe(0.28f, 10000f),    // 6:42 amanece
            new Keyframe(0.50f,130000f),    // 12h mediodía
            new Keyframe(0.83f, 15000f),    // 20h atardecer
            new Keyframe(0.88f,     0f),    // 21h
            new Keyframe(1.00f,     0f));   // 24h
    }
}
