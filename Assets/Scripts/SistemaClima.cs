// SistemaClima.cs — Sistema de clima seleccionable en tiempo real
// Sol, nubes, lluvia, tormenta, niebla, viento.
// UI con botones de clima + transiciones suaves.

using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public class SistemaClima : MonoBehaviour
{
    // ── Estado de clima ────────────────────────────────────────────────────
    public enum EstadoClima { Sol, Nublado, LluviaLigera, Tormenta, Niebla, NieveLigera }
    public EstadoClima climaActual = EstadoClima.Sol;

    // ── Sol ───────────────────────────────────────────────────────────────
    [Header("Sol")]
    public Light solDireccional;
    HDAdditionalLightData _hdSol;

    // ── Partículas ────────────────────────────────────────────────────────
    [Header("Partículas de clima")]
    public ParticleSystem particulasLluvia;
    public ParticleSystem particulasNieve;
    public ParticleSystem particulasViento;   // hojas volando

    // ── Audio ─────────────────────────────────────────────────────────────
    [Header("Audio clima")]
    public AudioClip clipLluvia;
    public AudioClip clipTrueno;
    public AudioClip clipViento;
    AudioSource _srcLluvia, _srcViento;

    // ── Viento ────────────────────────────────────────────────────────────
    [Header("Viento")]
    [Range(0f, 10f)] public float fuerzaViento = 0f;
    public Vector3 direccionViento = new(1, 0, 0.3f);

    // ── UI ────────────────────────────────────────────────────────────────
    [Header("UI (opcional)")]
    public GameObject panelClima;      // panel con botones
    bool _panelVisible = false;

    // ── Configuraciones por clima ─────────────────────────────────────────
    struct ConfigClima
    {
        public float intensidadSol, niebla;
        public Color colorSol, colorFog;
        public float viento;
        public bool  llueve, nieva;
    }

    static readonly ConfigClima[] CONFIGS = {
        // Sol
        new ConfigClima { intensidadSol=1.15f, niebla=0.0008f, colorSol=new Color(1f,.93f,.78f), colorFog=new Color(.72f,.75f,.80f), viento=0.3f, llueve=false, nieva=false },
        // Nublado
        new ConfigClima { intensidadSol=0.45f, niebla=0.0018f, colorSol=new Color(.8f,.8f,.85f),  colorFog=new Color(.68f,.70f,.74f), viento=1.5f, llueve=false, nieva=false },
        // Lluvia ligera
        new ConfigClima { intensidadSol=0.20f, niebla=0.0035f, colorSol=new Color(.65f,.65f,.70f), colorFog=new Color(.60f,.63f,.68f), viento=2f,   llueve=true,  nieva=false },
        // Tormenta
        new ConfigClima { intensidadSol=0.05f, niebla=0.008f,  colorSol=new Color(.4f,.4f,.5f),   colorFog=new Color(.45f,.48f,.55f), viento=8f,   llueve=true,  nieva=false },
        // Niebla
        new ConfigClima { intensidadSol=0.30f, niebla=0.018f,  colorSol=new Color(.8f,.82f,.85f), colorFog=new Color(.80f,.82f,.84f), viento=0.1f, llueve=false, nieva=false },
        // Nieve ligera
        new ConfigClima { intensidadSol=0.55f, niebla=0.004f,  colorSol=new Color(.9f,.92f,1f),   colorFog=new Color(.88f,.90f,.94f), viento=1f,   llueve=false, nieva=true  },
    };

    bool _transicionando;

    // =========================================================================

    void Start()
    {
        if (solDireccional == null) solDireccional = FindFirstObjectByType<Light>();
        if (solDireccional != null)
        {
            _hdSol = solDireccional.GetComponent<HDAdditionalLightData>();
            if (_hdSol == null) _hdSol = solDireccional.gameObject.AddComponent<HDAdditionalLightData>();
        }
        InicializarAudio();
        InicializarParticulas();
        AplicarClimaInstantaneo(climaActual);
        CrearUIClima();
    }

    void Update()
    {
        // Shortcut teclado: Ctrl+W abre el panel de clima (new Input System)
        var _kb = UnityEngine.InputSystem.Keyboard.current;
        if (_kb != null && _kb.leftCtrlKey.isPressed && _kb.wKey.wasPressedThisFrame)
            TogglePanel();

        // Truenos aleatorios en tormenta
        if (climaActual == EstadoClima.Tormenta && Random.value < 0.003f)
            StartCoroutine(Trueno());

        // BUG FIX (auditoría): NO tocar Physics.gravity global — metía gravedad
        // lateral permanente a TODOS los rigidbodies (personajes, coches, ragdolls),
        // no sólo a los proyectiles. El viento se aplica vía WindZone/partículas.
        if (Physics.gravity.x != 0f || Physics.gravity.z != 0f)
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
    }

    // ── Cambio de clima ───────────────────────────────────────────────────

    public void CambiarClima(EstadoClima nuevo)
    {
        if (_transicionando) return;
        climaActual = nuevo;
        StartCoroutine(TransicionClima(CONFIGS[(int)nuevo]));
        // Notificar al sistema de post-proceso para ajustar color grading según el clima
        SistemaPostProcesoAAA.SetClimaGrading(nuevo);
    }

    public void SetSol()        => CambiarClima(EstadoClima.Sol);
    public void SetNublado()    => CambiarClima(EstadoClima.Nublado);
    public void SetLluvia()     => CambiarClima(EstadoClima.LluviaLigera);
    public void SetTormenta()   => CambiarClima(EstadoClima.Tormenta);
    public void SetNiebla()     => CambiarClima(EstadoClima.Niebla);
    public void SetNieve()      => CambiarClima(EstadoClima.NieveLigera);

    void AplicarClimaInstantaneo(EstadoClima e)
    {
        var c = CONFIGS[(int)e];
        if (solDireccional != null)
        {
            solDireccional.color = c.colorSol;
            if (_hdSol != null) _hdSol.SetIntensity(c.intensidadSol * 80000f, LightUnit.Lux); // sol directo ~80klux
            else solDireccional.intensity = c.intensidadSol;
        }
        RenderSettings.fogDensity = c.niebla;
        RenderSettings.fogColor   = c.colorFog;
        fuerzaViento = c.viento;
        GestionarParticulas(c);
        GestionarAudio(c);
    }

    IEnumerator TransicionClima(ConfigClima objetivo, float duracion = 6f)
    {
        _transicionando = true;
        float t = 0f;
        float solInicial   = _hdSol != null ? _hdSol.intensity : (solDireccional?.intensity ?? 1f);
        Color colorInicial = solDireccional?.color ?? Color.white;
        float nieblaInicial = RenderSettings.fogDensity;
        Color fogInicial    = RenderSettings.fogColor;
        float vientoInicial = fuerzaViento;

        GestionarParticulas(objetivo);
        GestionarAudio(objetivo);

        while (t < duracion)
        {
            t += Time.deltaTime;
            float f = t / duracion;

            if (solDireccional != null)
            {
                float targetLux = objetivo.intensidadSol * 80000f;
                if (_hdSol != null) _hdSol.SetIntensity(Mathf.Lerp(solInicial, targetLux, f), LightUnit.Lux);
                else solDireccional.intensity = Mathf.Lerp(solInicial, objetivo.intensidadSol, f);
                solDireccional.color = Color.Lerp(colorInicial, objetivo.colorSol, f);
            }
            RenderSettings.fogDensity = Mathf.Lerp(nieblaInicial, objetivo.niebla, f);
            RenderSettings.fogColor   = Color.Lerp(fogInicial, objetivo.colorFog, f);
            fuerzaViento              = Mathf.Lerp(vientoInicial, objetivo.viento, f);
            yield return null;
        }
        _transicionando = false;
    }

    // ── Partículas ────────────────────────────────────────────────────────

    void InicializarParticulas()
    {
        // Crear partículas de lluvia si no existen
        if (particulasLluvia == null)
        {
            var go = new GameObject("Lluvia_Partículas");
            go.transform.SetParent(transform);
            particulasLluvia = go.AddComponent<ParticleSystem>();
            var main = particulasLluvia.main;
            main.maxParticles = 5000; main.startSpeed = 8f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.startColor = new Color(0.6f, 0.7f, 0.9f, 0.5f);
            main.gravityModifier = 1.5f; main.startLifetime = 1.2f;
            var em = particulasLluvia.emission; em.rateOverTime = 0f;
            var shape = particulasLluvia.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(60, 1, 60);
            go.transform.localPosition = new Vector3(0, 20, 0);
            particulasLluvia.Stop();
        }

        if (particulasViento == null)
        {
            var go = new GameObject("Viento_Hojas");
            go.transform.SetParent(transform);
            particulasViento = go.AddComponent<ParticleSystem>();
            var main = particulasViento.main;
            main.maxParticles = 200; main.startSpeed = 3f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            main.startColor = new Color(0.55f, 0.42f, 0.18f, 0.8f);
            main.startLifetime = 8f; main.gravityModifier = 0.1f;
            particulasViento.Stop();
        }
    }

    void GestionarParticulas(ConfigClima c)
    {
        if (particulasLluvia != null)
        {
            if (c.llueve && !particulasLluvia.isPlaying) particulasLluvia.Play();
            else if (!c.llueve) particulasLluvia.Stop();
            var em = particulasLluvia.emission;
            em.rateOverTime = c.llueve ? (climaActual == EstadoClima.Tormenta ? 3000 : 800) : 0;
        }
        if (particulasNieve != null)
        {
            if (c.nieva && !particulasNieve.isPlaying) particulasNieve.Play();
            else if (!c.nieva) particulasNieve.Stop();
        }
        if (particulasViento != null)
        {
            if (c.viento > 2f && !particulasViento.isPlaying) particulasViento.Play();
            else if (c.viento <= 2f) particulasViento.Stop();
        }
    }

    // ── Audio ─────────────────────────────────────────────────────────────

    void InicializarAudio()
    {
        if (clipLluvia != null)
        {
            _srcLluvia = gameObject.AddComponent<AudioSource>();
            _srcLluvia.clip = clipLluvia; _srcLluvia.loop = true; _srcLluvia.spatialBlend = 0f; _srcLluvia.volume = 0f; _srcLluvia.Play();
        }
        if (clipViento != null)
        {
            _srcViento = gameObject.AddComponent<AudioSource>();
            _srcViento.clip = clipViento; _srcViento.loop = true; _srcViento.spatialBlend = 0f; _srcViento.volume = 0f; _srcViento.Play();
        }
    }

    void GestionarAudio(ConfigClima c)
    {
        StartCoroutine(FadeAudio(_srcLluvia, c.llueve ? 0.6f : 0f));
        StartCoroutine(FadeAudio(_srcViento, c.viento > 2f ? Mathf.Clamp01(c.viento / 10f) * 0.5f : 0f));
        // Sincronizar audio ambiente de SistemaAtmosfera con Nature Sounds Pack
        var atmos = FindFirstObjectByType<SistemaAtmosfera>();
        if (atmos != null)
            atmos.SetClimaAudio(c.llueve, climaActual == EstadoClima.Tormenta);
    }

    IEnumerator FadeAudio(AudioSource src, float targetVol, float dur = 3f)
    {
        if (src == null) yield break;
        float start = src.volume, t = 0f;
        while (t < dur) { t += Time.deltaTime; src.volume = Mathf.Lerp(start, targetVol, t/dur); yield return null; }
        src.volume = targetVol;
    }

    IEnumerator Trueno()
    {
        yield return new WaitForSeconds(Random.Range(1f, 4f));
        if (clipTrueno != null) AudioSource.PlayClipAtPoint(clipTrueno, Camera.main?.transform.position ?? Vector3.zero, 0.9f);
        // Flash de relámpago
        if (solDireccional != null && _hdSol != null)
        {
            float orig = _hdSol.intensity;
            _hdSol.SetIntensity(400000f, LightUnit.Lux); // relámpago: ~400klux
            yield return new WaitForSeconds(0.08f);
            _hdSol.SetIntensity(orig, LightUnit.Lux);
            yield return new WaitForSeconds(0.12f);
            _hdSol.SetIntensity(300000f, LightUnit.Lux);
            yield return new WaitForSeconds(0.06f);
            _hdSol.SetIntensity(orig, LightUnit.Lux);
        }
    }

    // ── UI ────────────────────────────────────────────────────────────────

    void CrearUIClima()
    {
        if (panelClima != null) { panelClima.SetActive(false); return; }

        var canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        var panel = new GameObject("Panel_Clima");
        panel.transform.SetParent(canvas.transform, false);
        var rt = panel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 0.5f); rt.anchorMax = new Vector2(1, 0.5f);
        rt.pivot = new Vector2(1, 0.5f);
        rt.anchoredPosition = new Vector2(-10, 0);
        rt.sizeDelta = new Vector2(120, 260);
        panel.AddComponent<Image>().color = new Color(0, 0, 0, 0.7f);
        panel.SetActive(false);
        panelClima = panel;

        string[] nombres  = { "☀ Sol", "☁ Nubes", "🌧 Lluvia", "⛈ Tormenta", "🌫 Niebla", "❄ Nieve" };
        System.Action[] acciones = { SetSol, SetNublado, SetLluvia, SetTormenta, SetNiebla, SetNieve };

        for (int i = 0; i < nombres.Length; i++)
        {
            var btnGO = new GameObject($"Btn_{nombres[i]}");
            btnGO.transform.SetParent(panel.transform, false);
            var brt = btnGO.AddComponent<RectTransform>();
            brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(1, 1);
            brt.pivot = new Vector2(0.5f, 1);
            brt.anchoredPosition = new Vector2(0, -(i * 42 + 5));
            brt.sizeDelta = new Vector2(-10, 38);
            var btn = btnGO.AddComponent<Button>();
            btnGO.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f);
            var txt = new GameObject("Label");
            txt.transform.SetParent(btnGO.transform, false);
            var trt = txt.AddComponent<RectTransform>(); trt.sizeDelta = brt.sizeDelta;
            var t = txt.AddComponent<Text>();
            t.text = nombres[i]; t.alignment = TextAnchor.MiddleCenter;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.color = Color.white; t.fontSize = 14;
            int idx = i;
            btn.onClick.AddListener(() => acciones[idx]());
        }

        // Etiqueta Ctrl+W
        Debug.Log("[Clima] Panel clima creado. Ctrl+W para ab