// Assets/Scripts/SistemaCharcos.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA CHARCOS / SUELO MOJADO
//
//  Cuando SistemaClima está en Lluvia o Tormenta, el suelo se moja
//  progresivamente y aparecen charcos reflectantes en las zonas planas
//  alrededor del jugador. Al dejar de llover se secan poco a poco.
//
//  · Variable global de shader  _GlobalWetness / _Wetness  (0-1)  para que
//    cualquier material PBR del proyecto pueda oscurecer/abrillantar el albedo.
//  · Pool de quads-charco reflectantes colocados por raycast en suelo plano,
//    reciclados alrededor de la cámara. Su transparencia sigue a la humedad.
//
//  Antes de esto no había ningún sistema de charcos: "charco" sólo aparecía
//  en el generador de ríos.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-35)]
public class SistemaCharcos : MonoBehaviour
{
    public static SistemaCharcos Instance { get; private set; }

    [Header("Humedad")]
    [Tooltip("Velocidad de mojado al empezar a llover (unidades/seg).")]
    public float velMojado = 0.12f;
    [Tooltip("Velocidad de secado al parar la lluvia (unidades/seg).")]
    public float velSecado = 0.05f;

    [Header("Charcos")]
    [Tooltip("Nº de charcos en el pool alrededor del jugador.")]
    public int   numCharcos = 36;
    [Tooltip("Radio alrededor de la cámara donde se reparten los charcos.")]
    public float radio = 55f;
    [Tooltip("Tamaño mínimo/máximo de cada charco (m).")]
    public Vector2 tamano = new(1.2f, 4.5f);
    [Tooltip("Sólo se colocan en suelo con inclinación menor a este coseno (0.985 ≈ 10°).")]
    public float planitudMin = 0.985f;

    SistemaClima _clima;
    Transform    _objetivo;          // cámara / jugador a seguir
    Material     _matCharco;
    readonly List<Transform> _charcos = new();
    readonly List<MeshRenderer> _rends = new();

    float   _humedad;                // 0 seco … 1 totalmente mojado
    Vector3 _ultimoReparto = new(99999, 0, 99999);

    static readonly int ID_GlobalWetness = Shader.PropertyToID("_GlobalWetness");
    static readonly int ID_Wetness       = Shader.PropertyToID("_Wetness");
    static readonly int ID_BaseColor     = Shader.PropertyToID("_BaseColor");
    static readonly int ID_Color         = Shader.PropertyToID("_Color");
    static readonly int ID_Smoothness    = Shader.PropertyToID("_Smoothness");
    static readonly int ID_Metallic      = Shader.PropertyToID("_Metallic");
    // Ripple y SSR wetness — nuevos globals
    static readonly int ID_RippleTime    = Shader.PropertyToID("_GlobalRippleTime");
    static readonly int ID_WetnessSSR    = Shader.PropertyToID("_GlobalWetnessSSR");
    static readonly int ID_BaseColorMap  = Shader.PropertyToID("_BaseColorMap");

    // Ripple: animamos las escalas de los charcos individualmente
    // para simular anillos de lluvia sin un shader personalizado.
    float[] _ripplePhase;   // fase aleatoria por charco (0-2π)
    float   _rippleTimer;
    // BUG FIX #1: guardar escala base de cada charco para evitar drift acumulativo.
    // Sin esto, cada frame multiplica osc sobre la escala ya-oscillada del frame anterior.
    float[] _charcoEscalaBase;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        _clima = FindFirstObjectByType<SistemaClima>();
        _objetivo = Camera.main != null ? Camera.main.transform : null;
        CrearMaterialCharco();
        CrearPool();
        AlsasuaLogger.Info("Charcos", $"Sistema de charcos listo ({numCharcos} en pool).");
    }

    void CrearMaterialCharco()
    {
        var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        _matCharco = new Material(sh) { name = "Mat_Charco" };
        // Agua oscura muy lisa y algo metálica → refleja el cielo/entorno
        if (_matCharco.HasProperty(ID_BaseColor)) _matCharco.SetColor(ID_BaseColor, new Color(0.03f, 0.045f, 0.06f, 0.85f));
        if (_matCharco.HasProperty(ID_Color))     _matCharco.SetColor(ID_Color,     new Color(0.03f, 0.045f, 0.06f, 0.85f));
        if (_matCharco.HasProperty(ID_Smoothness))_matCharco.SetFloat(ID_Smoothness, 0.97f);
        if (_matCharco.HasProperty(ID_Metallic))  _matCharco.SetFloat(ID_Metallic,  0.1f);
        ConfigurarTransparencia(_matCharco);
    }

    static void ConfigurarTransparencia(Material m)
    {
        // Modo transparente compatible con HDRP/Lit y Standard
        m.SetFloat("_SurfaceType", 1f);           // HDRP: Transparent
        m.SetFloat("_Mode", 3f);                  // Standard: Transparent
        m.SetFloat("_BlendMode", 0f);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    void CrearPool()
    {
        var raiz = new GameObject("Charcos_Pool").transform;
        raiz.SetParent(transform, false);
        for (int i = 0; i < numCharcos; i++)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = $"Charco_{i}";
            q.transform.SetParent(raiz, false);
            q.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // tumbado en el suelo
            Destroy(q.GetComponent<Collider>());
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = _matCharco;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.enabled = false;
            _charcos.Add(q.transform);
            _rends.Add(r);
        }
        // Fase aleatoria por charco para que los anillos no sean síncronos
        _ripplePhase    = new float[numCharcos];
        _charcoEscalaBase = new float[numCharcos];
        for (int i = 0; i < numCharcos; i++)
            _ripplePhase[i] = Random.Range(0f, Mathf.PI * 2f);
        // _charcoEscalaBase se rellena en RepartirCharcos cuando se asigna la escala real.
    }

    void Update()
    {
        // Objetivo (la cámara puede crearse después del Start)
        if (_objetivo == null && Camera.main != null) _objetivo = Camera.main.transform;

        // ── Humedad objetivo según el clima ──────────────────────────────────
        bool lloviendo = _clima != null &&
            (_clima.climaActual == SistemaClima.EstadoClima.LluviaLigera ||
             _clima.climaActual == SistemaClima.EstadoClima.Tormenta);
        float objetivo = lloviendo ? (_clima.climaActual == SistemaClima.EstadoClima.Tormenta ? 1f : 0.7f) : 0f;

        float vel = objetivo > _humedad ? velMojado : velSecado;
        _humedad = Mathf.MoveTowards(_humedad, objetivo, vel * Time.deltaTime);

        // ── Global de shader (PBR del suelo puede leerlo) ────────────────────
        Shader.SetGlobalFloat(ID_GlobalWetness, _humedad);
        Shader.SetGlobalFloat(ID_Wetness, _humedad);
        // _GlobalWetnessSSR: smoothness adicional para que el suelo mojado
        // refleje más el entorno (SSR lo recoge si el shader expone la propiedad).
        Shader.SetGlobalFloat(ID_WetnessSSR, _humedad * 0.95f);
        // _GlobalRippleTime: tiempo escalado — shaders de charco pueden usarlo
        // para desplazar sus UVs de Normal Map y animar la superficie del agua.
        _rippleTimer += Time.deltaTime;
        Shader.SetGlobalFloat(ID_RippleTime, _rippleTimer);

        // ── Reparto de charcos alrededor del jugador ─────────────────────────
        if (_objetivo != null && _humedad > 0.01f)
        {
            if (Vector3.Distance(_objetivo.position, _ultimoReparto) > radio * 0.5f)
                RepartirCharcos(_objetivo.position);
        }

        // ── Ripple rings: pulsar escala de cada charco con onda sinusoidal ───
        // Cada charco tiene una fase aleatoria → los anillos no son síncronos.
        // La amplitud del pulso escala con la humedad: sin lluvia no hay ripple.
        float rippleAmp = _humedad * 0.18f; // max 18% de variación en escala
        float rippleFreq = 1.4f;            // Hz — 1.4 anillos/seg por charco
        for (int i = 0; i < _charcos.Count; i++)
        {
            if (_ripplePhase == null || i >= _ripplePhase.Length) break;
            var t = _charcos[i];
            if (t == null || !t.gameObject.activeSelf) continue;
            // BUG FIX #1: usar _charcoEscalaBase (inmutable) en lugar de t.localScale.x
            // que ya contenía la oscilación del frame anterior → drift acumulativo corregido.
            float sBase = (_charcoEscalaBase != null && i < _charcoEscalaBase.Length)
                        ? _charcoEscalaBase[i] : t.localScale.x;
            float sBaseY = sBase * (t.localScale.y > 0.001f ? t.localScale.y / Mathf.Max(0.001f, t.localScale.x) : 1f);
            float osc = 1f + Mathf.Sin(_rippleTimer * rippleFreq * Mathf.PI * 2f
                                        + _ripplePhase[i]) * rippleAmp;
            t.localScale = new Vector3(sBase * osc, sBaseY, 1f);
        }

        // ── Transparencia de los charcos sigue a la humedad ──────────────────
        float alpha = Mathf.SmoothStep(0f, 0.85f, _humedad);
        bool visibles = _humedad > 0.04f;
        for (int i = 0; i < _rends.Count; i++)
        {
            if (_rends[i].enabled != visibles) _rends[i].enabled = visibles;
        }
        if (visibles && _matCharco != null)
        {
            var c = new Color(0.03f, 0.045f, 0.06f, alpha);
            if (_matCharco.HasProperty(ID_BaseColor)) _matCharco.SetColor(ID_BaseColor, c);
            if (_matCharco.HasProperty(ID_Color))     _matCharco.SetColor(ID_Color, c);
            // Animar offset UV del Normal Map del charco si el shader lo soporta
            // → simula la superficie agitada sin geometría extra.
            if (_matCharco.HasProperty(ID_BaseColorMap))
            {
                float uvX = Mathf.Sin(_rippleTimer * 0.3f) * 0.04f;
                float uvY = Mathf.Cos(_rippleTimer * 0.25f) * 0.04f;
                _matCharco.SetTextureOffset("_BaseColorMap", new Vector2(uvX, uvY));
                _matCharco.SetTextureOffset("_NormalMap",    new Vector2(-uvX * 2f, uvY * 2f));
            }
        }
    }

    void RepartirCharcos(Vector3 centro)
    {
        _ultimoReparto = centro;
        for (int i = 0; i < _charcos.Count; i++)
        {
            // Punto aleatorio en el disco alrededor del centro
            Vector2 r2 = Random.insideUnitCircle * radio;
            Vector3 p  = new(centro.x + r2.x, centro.y + 60f, centro.z + r2.y);

            if (Physics.Raycast(p, Vector3.down, out var hit, 200f) && hit.normal.y >= planitudMin)
            {
                _charcos[i].position = hit.point + Vector3.up * 0.03f;
                float s = Random.Range(tamano.x, tamano.y);
                float sy = s * Random.Range(0.6f, 1f);
                _charcos[i].localScale = new Vector3(s, sy, 1f);
                if (_charcoEscalaBase != null && i < _charcoEscalaBase.Length) _charcoEscalaBase[i] = s;
                _charcos[i].gameObject.SetActive(true);
            }
            else
            {
                // Sin suelo plano: usar altura de terreno como fallback plano
                float y = GeoDataAlsasua.AlturaTerreno(p.x, p.z);
                _charcos[i].position = new Vector3(p.x, y + 0.03f, p.z);
                float s = Random.Range(tamano.x, tamano.y);
                _charcos[i].localScale = new Vector3(s, s, 1f);
            }
        }
    }

    /// <summary>Humedad actual del mundo (0 seco … 1 empapado).</summary>
    public float Humedad => _humedad;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
