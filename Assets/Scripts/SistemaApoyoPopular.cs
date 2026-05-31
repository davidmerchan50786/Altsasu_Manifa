// SistemaApoyoPopular.cs
// Barra de apoyo político popular + honor + paranoia.
// Afecta el comportamiento de los NPCs hacia el jugador.

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class SistemaApoyoPopular : MonoBehaviour
{
    public static SistemaApoyoPopular Instance { get; private set; }

    [Header("Valores (0-100)")]
    [Range(0,100)] public float apoyo    = 50f;  // apoyo popular
    [Range(0,100)] public float honor    = 50f;  // honor/credibilidad
    [Range(0,100)] public float paranoia = 0f;   // paranoia — si sube mucho, civiles = posible GC

    [Header("Umbrales")]
    public float umbralParanoia    = 70f;  // a partir de aquí algunos civiles son GC disfrazado
    public float umbralMaxParanoia = 90f;  // todos los civiles sospechosos son GC

    [Header("UI — barras")]
    public Slider sliderApoyo;
    public Slider sliderHonor;
    public Slider sliderParanoia;
    public Text   textoApoyo;
    public Text   textoParanoia;
    public Image  fondoParanoia;  // se vuelve rojo al subir

    [Header("Decay (bajada automática)")]
    public float decayApoyo    = 0.5f;   // apoyo baja 0.5/min si no hay actividad
    public float decayParanoia = 2.0f;   // paranoia baja 2/min si el jugador no hace nada

    [Header("Efectos")]
    public Color colorApoyoAlto  = new(0.2f, 0.8f, 0.2f);
    public Color colorApoyoBajo  = new(0.8f, 0.2f, 0.2f);
    public Color colorParanoiaCritica = Color.red;

    // ── Eventos ───────────────────────────────────────────────────────────
    public static event System.Action<float> OnApoyoCambia;
    public static event System.Action<float> OnParanoiaCambia;
    public static event System.Action        OnParanoiaCritica;

    bool _paranoiaCriticaLanzada;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        CrearUIBarras();
        StartCoroutine(BucleDecay());
    }

    void Update()
    {
        ActualizarUI();

        // Paranoia crítica → alerta
        if (paranoia >= umbralMaxParanoia && !_paranoiaCriticaLanzada)
        {
            _paranoiaCriticaLanzada = true;
            OnParanoiaCritica?.Invoke();
            Debug.Log("[Paranoia] ⚠ NIVEL CRÍTICO — Los civiles podrían ser Guardia Civil disfrazada.");
        }
        if (paranoia < umbralMaxParanoia - 5) _paranoiaCriticaLanzada = false;
    }

    // ── API pública ───────────────────────────────────────────────────────

    public void SumarApoyo(float cantidad, string razon = "")
    {
        apoyo = Mathf.Clamp(apoyo + cantidad, 0, 100);
        OnApoyoCambia?.Invoke(apoyo);
        if (!string.IsNullOrEmpty(razon)) Debug.Log($"[Apoyo] +{cantidad} por {razon}. Total: {apoyo:F0}");
    }

    public void RestarApoyo(float cantidad, string razon = "")
    {
        apoyo = Mathf.Clamp(apoyo - cantidad, 0, 100);
        honor = Mathf.Clamp(honor - cantidad * 0.5f, 0, 100);
        OnApoyoCambia?.Invoke(apoyo);
    }

    public void SumarParanoia(float cantidad)
    {
        paranoia = Mathf.Clamp(paranoia + cantidad, 0, 100);
        OnParanoiaCambia?.Invoke(paranoia);
    }

    public void RestarParanoia(float cantidad) =>
        paranoia = Mathf.Clamp(paranoia - cantidad, 0, 100);

    public void SumarHonor(float cantidad) =>
        honor = Mathf.Clamp(honor + cantidad, 0, 100);

    /// ¿Un NPC concreto está disfrazado de civil pero es GC?
    public bool EsGCDisfrazado(GameObject npc)
    {
        if (paranoia < umbralParanoia) return false;
        // Probabilidad proporcional a la paranoia
        float prob = (paranoia - umbralParanoia) / (umbralMaxParanoia - umbralParanoia);
        // Usar el hash del npc para consistencia (siempre el mismo npc es o no es GC)
        float hash = (npc.GetInstanceID() % 1000) / 1000f;
        return hash < prob;
    }

    // ── Decay automático ──────────────────────────────────────────────────

    IEnumerator BucleDecay()
    {
        while (true)
        {
            yield return new WaitForSeconds(60f); // cada minuto real
            apoyo    = Mathf.Clamp(apoyo    - decayApoyo * Time.deltaTime * 60f,  0, 100);
            paranoia = Mathf.Clamp(paranoia - decayParanoia * Time.deltaTime * 60f, 0, 100);
        }
    }

    // ── UI ────────────────────────────────────────────────────────────────

    void ActualizarUI()
    {
        if (sliderApoyo    != null) sliderApoyo.value    = apoyo / 100f;
        if (sliderHonor    != null) sliderHonor.value    = honor / 100f;
        if (sliderParanoia != null) sliderParanoia.value = paranoia / 100f;

        if (textoApoyo    != null) textoApoyo.text    = $"Apoyo: {apoyo:F0}%";
        if (textoParanoia != null) textoParanoia.text =
            paranoia < umbralParanoia ? $"Paranoia: {paranoia:F0}%"
            : $"⚠ PARANOIA: {paranoia:F0}% — Desconfía de civiles";

        if (fondoParanoia != null)
            fondoParanoia.color = Color.Lerp(Color.clear, colorParanoiaCritica, paranoia / 100f * 0.4f);

        // Viñeta de pantalla roja si paranoia alta
        // (se puede activar vía postprocess si se tiene volumen)
    }

    void CrearUIBarras()
    {
        // Buscar canvas existente
        var canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null || sliderApoyo != null) return;

        // Panel izquierdo inferior
        var panel = new GameObject("Panel_ApoyoParanoia");
        panel.transform.SetParent(canvas.transform, false);
        var rt = panel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 0);
        rt.pivot = new Vector2(0, 0); rt.anchoredPosition = new Vector2(10, 80);
        rt.sizeDelta = new Vector2(180, 90);
        var bg = panel.AddComponent<Image>(); bg.color = new Color(0, 0, 0, 0.6f);

        sliderApoyo    = CrearSlider(panel.transform, "Apoyo",    new Vector2(10,-10), colorApoyoAlto);
        sliderParanoia = CrearSlider(panel.transform, "Paranoia", new Vector2(10,-52), Color.red);
        sliderHonor    = CrearSlider(panel.transform, "Honor",    new Vector2(10,-30), new Color(1f,0.85f,0f));
    }

    static Slider CrearSlider(Transform padre, string etiqueta, Vector2 pos, Color color)
    {
        var go = new GameObject($"Slider_{etiqueta}");
        go.transform.SetParent(padre, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0,1); rt.anchorMax = new Vector2(1,1);
        rt.pivot = new Vector2(0,1); rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(-20, 16);

        var slider = go.AddComponent<Slider>();
        slider.minValue = 0; slider.maxValue = 1; slider.value = 0.5f;
        slider.interactable = false;

        // Background
        var bg = new GameObject("BG"); bg.transform.SetParent(go.transform, false);
        var bgRT = bg.AddComponent<RectTransform>(); bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one; bgRT.sizeDelta = Vector2.zero;
        var bgImg = bg.AddComponent<Image>(); bgImg.color = new Color(0.2f, 0.2f, 0.2f);

        // Fill
        var fillArea = new GameObject("FillArea"); fillArea.transform.SetParent(go.transform, false);
        var faRT = fillArea.AddComponent<RectTransform>(); faRT.anchorMin = Vector2.zero; faRT.anchorMax = Vector2.one; faRT.sizeDelta = Vector2.zero;
        var fill = new GameObject("Fill"); fill.transform.SetParent(fillArea.transform, false);
        var fillRT = fill.AddComponent<RectTransform>(); fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one; fillRT.sizeDelta = Vector2.zero;
        var fillImg = fill.AddComponent<Image>(); fillImg.color = color;
        slider.fillRect = fillRT;

        // Label
        var lbl = new GameObject("Label"); lbl.transform.SetParent(go.transform, false);
        var lblRT = lbl.AddComponent<RectTransform>(); lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one; lblRT.sizeDelta = Vector2.zero;
        var txt = lbl.AddComponent<Text>();
        txt.text = etiqueta; txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 10; txt.color = Color.white; txt.alignment = TextAnchor.MiddleLeft;

        return slider;
    }
}
