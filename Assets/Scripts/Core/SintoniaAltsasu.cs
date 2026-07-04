// Assets/Scripts/_Tuning~/SintoniaAltsasu.cs  (STAGING/DRAFT — carpeta ~ no compila)
// ─────────────────────────────────────────────────────────────────────────────
//  PANEL ÚNICO DE TUNING del bucle "calor y alivio" de Altsasu. Un solo asset
//  desde el que se balancea TODO: conversión a Guardia Civil, controles de
//  carretera, testigos y coartada. Evita tener los mismos umbrales (70/90/3…)
//  repartidos por cinco scripts y desincronizados.
//
//  Cada manager expone `public SintoniaAltsasu sintonia;` y, si no es null, lee
//  de aquí; si es null, usa sus defaults serializados (cero regresión). Crea el
//  asset con  Assets ▸ Create ▸ Alsasua ▸ Sintonía (calor)  y asígnalo a los
//  managers (ParanoiaGC, ControlesGC, Testigos, Coartada).
//
//  El APOYO popular (0-100) es el dial transversal: sube → menos tricornios,
//  más gente te cuela en controles, menos chivatos, te escondes más rápido.
// ─────────────────────────────────────────────────────────────────────────────
using UnityEngine;

[CreateAssetMenu(menuName = "Alsasua/Sintonía (calor)", fileName = "SintoniaAltsasu")]
public class SintoniaAltsasu : ScriptableObject
{
    [Header("─ Paranoia → Guardia Civil ─")]
    [Tooltip("Paranoia a partir de la cual empiezan a convertirse NPCs/coches.")]
    public float umbralConversion = 70f;
    [Tooltip("Paranoia a partir de la cual la conversión es máxima/agresiva.")]
    public float umbralCritico = 90f;
    [Tooltip("Máximo de unidades convertidas a la vez a paranoia 100.")]
    public int maxConvertidos = 12;
    [Tooltip("Cuánto frena el apoyo la conversión (1 = sin freno, 0.2 = -80% a apoyo 100).")]
    [Range(0.05f, 1f)] public float frenoApoyoMin = 0.25f;

    [Header("─ Controles de carretera ─")]
    public float controlesUmbralActivacion = 70f;
    public int   maxControles = 4;
    [Tooltip("Búsqueda (0-5) a partir de la cual el control arresta en vez de cachear.")]
    public int   controlUmbralArresto = 3;
    [Range(0f, 1f)] public float controlProbPasarApoyo0   = 0.05f;
    [Range(0f, 1f)] public float controlProbPasarApoyo100 = 0.6f;

    [Header("─ Testigos ─")]
    public float testigoRango = 18f;
    public float testigoRetardoReporte = 3f;
    [Range(0f, 1f)] public float testigoProbChivarApoyo0   = 0.9f;
    [Range(0f, 1f)] public float testigoProbChivarApoyo100 = 0.05f;

    [Header("─ Coartada (refugios) ─")]
    [Tooltip("Ritmo base de enfriamiento de búsqueda/paranoia dentro de un refugio.")]
    public float coartadaRitmoBase = 1f;
    [Tooltip("Cuánto acelera el apoyo el enfriamiento (ritmo × (1 + apoyo/100 × esto)).")]
    [Range(0f, 2f)] public float coartadaBonusApoyo = 1f;

    // ── Helpers compartidos (misma matemática que usan los managers) ──────────

    /// <summary>Factor 1→frenoApoyoMin según apoyo (frena conversión a tricornios).</summary>
    public float FactorApoyo(float apoyo) => Mathf.Lerp(1f, frenoApoyoMin, Mathf.Clamp01(apoyo / 100f));

    /// <summary>Nº de unidades GC objetivo para una paranoia dada.</summary>
    public int ConvertidosObjetivo(float paranoia, float apoyo)
    {
        if (paranoia < umbralConversion) return 0;
        float t = Mathf.InverseLerp(umbralConversion, 100f, paranoia);
        return Mathf.Clamp(Mathf.CeilToInt(t * maxConvertidos * FactorApoyo(apoyo)), 0, maxConvertidos);
    }

    /// <summary>Nº de controles activos objetivo para una paranoia dada.</summary>
    public int ControlesObjetivo(float paranoia)
    {
        if (paranoia < controlesUmbralActivacion) return 0;
        float t = Mathf.InverseLerp(controlesUmbralActivacion, 100f, paranoia);
        return Mathf.Clamp(Mathf.CeilToInt(t * maxControles), 0, maxControles);
    }

    /// <summary>Probabilidad de que un control te deje pasar según apoyo.</summary>
    public float ControlProbPasar(float apoyo)
        => Mathf.Lerp(controlProbPasarApoyo0, controlProbPasarApoyo100, Mathf.Clamp01(apoyo / 100f));

    /// <summary>Probabilidad de que un testigo se chive (apoyo × gravedad 0..1).</summary>
    public float TestigoProbChivar(float apoyo, float gravedad)
        => Mathf.Lerp(testigoProbChivarApoyo0, testigoProbChivarApoyo100, Mathf.Clamp01(apoyo / 100f))
         * Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(gravedad));

    /// <summary>Ritmo de enfriamiento de la coartada según apoyo.</summary>
    public float CoartadaRitmo(float apoyo)
        => coartadaRitmoBase * (1f + Mathf.Clamp01(apoyo / 100f) * coartadaBonusApoyo);
}
