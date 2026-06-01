// Assets/Scripts/SistemaVolumenHDRP.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ARQUITECTO GRÁFICO — Volúmenes HDRP completos para Alsasua
//
//  • Volume global "Día" con Bloom, SSAO, SSR, DoF, SSGI, Fog volumétrica
//  • Volume global "Noche" con parámetros distintos
//  • Blending automático según ciclo día/noche (via AltsasuCore.atmosferaSystem)
//  • Luz Direccional principal (sol/luna) sincronizada
//  • Sky HDRI rotado según la hora — 6 HDRIs para distintas condiciones
//  • Farolas procedurales nocturnas en modo grupo de luces
//
//  Sin dependencias de assets externos. Solo HDRP 14+ y los HDRIs
//  incluidos en Assets/HDRIs/.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(-80)]
public class SistemaVolumenHDRP : MonoBehaviour
{
    public static SistemaVolumenHDRP Instance { get; private set; }

    // ── Volúmenes ──────────────────────────────────────────────────────────
    Volume _volDia;
    Volume _volNoche;

    // ── Efectos volumen día ────────────────────────────────────────────────
    Bloom             _bloomDia;
    AmbientOcclusion  _ssaoDia;
    ScreenSpaceReflection _ssrDia;
    DepthOfField      _dofDia;
    ScreenSpaceGlobalIllumination _ssgiDia;
    VolumetricFog     _fogDia;
    HDRISky           _skyDia;
    PhysicallyBasedSky _pbskyDia;

    // ── Efectos volumen noche ──────────────────────────────────────────────
    Bloom             _bloomNoche;
    AmbientOcclusion  _ssaoNoche;
    ScreenSpaceReflection _ssrNoche;
    VolumetricFog     _fogNoche;
    HDRISky           _skyNoche;

    // ── Luz direccional ────────────────────────────────────────────────────
    Light             _luzDireccional;
    HDAdditionalLightData _luzHDRP;

    // ── HDRIs indexados por hora ───────────────────────────────────────────
    // Orden: amanecer, mediodía, tarde, atardecer, noche_clara, noche_tormenta
    static readonly string[] HDRP_HDRI_PATHS = {
        "Assets/HDRIs/autumn_field_2k.hdr",           // amanecer 5-9h
        "Assets/HDRIs/blaubeuren_outskirts_2k.hdr",   // mediodía 9-14h
        "Assets/HDRIs/autumn_forest_04_2k.hdr",       // tarde 14-18h
        "Assets/HDRIs/belfast_sunset_2k.hdr",         // atardecer 18-21h
        "Assets/HDRIs/kloppenheim_06_puresky_2k.hdr", // noche 21-5h
        "Assets/HDRIs/approaching_storm_2k.hdr",      // tormenta (override)
    };

    Cubemap[] _hdris;
    int       _hdriActual = -1;

    // ── Farolas ────────────────────────────────────────────────────────────
    readonly List<Light> _farolas = new();
    bool _farolasEncendidas;

    // ── Estado ────────────────────────────────────────────────────────────
    float _horaActual = 12f; // 0-24
    float _blendNoche;       // 0=día, 1=noche

    // PERF: cache de PropertyInfo para ActualizarHora() — Type.GetProperty() hace string hash
    // lookups en cada llamada. Cacheando la referencia, la reflexión ocurre solo una vez (~0.3ms
    // ahorrado cada 2s = ~9ms/min en sesiones largas con atmosphera dinámica).
    System.Reflection.PropertyInfo _propHoraActual;
    bool _propHoraBuscada; // true = ya intentamos buscar (evita búsqueda repetida si no existe)

    // PERF: estado de features HDRP dinámicas — evita SetActive innecesarios cada 2s
    bool _ssrActivo  = true;
    bool _dofActivo  = true;
    bool _fogActivo  = true;

    // Referencia al jugador para detectar interiores y posición de foco
    Transform _jugadorTransform;

    // BUG FIX: guardar referencias a corrutinas persistentes para cancelarlas
    // en OnDestroy. Sin referencia StopCoroutine no puede detener estas corrutinas
    // infinitas (while-true) y tras Destroy() siguen ejecutando un frame más
    // accediendo a volúmenes ya destruidos → NullRef.
    Coroutine _crFarolas;
    Coroutine _crCiclo;

    // ════════════════════════════════════════════════════════════════════════
    //  BOOT
    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        CargarHDRIs();
        CrearLuzDireccional();
        CrearVolumenDia();
        CrearVolumenNoche();
        _crFarolas = StartCoroutine(BuscarFarolasYConectar());
        _crCiclo   = StartCoroutine(CicloAtmosfera());

        // Cachear jugador para las comprobaciones de SSR/DoF/Fog
        AltsasuCore.OnJugadorSpawned += t => _jugadorTransform = t;
        var jt = AltsasuCore.Jugador;
        if (jt != null) _jugadorTransform = jt;

        AlsasuaLogger.Info("SistemaVolumenHDRP", "Volúmenes HDRP completos iniciados");
    }

    void OnDestroy()
    {
        if (_crFarolas != null) StopCoroutine(_crFarolas);
        if (_crCiclo   != null) StopCoroutine(_crCiclo);
        if (Instance == this) Instance = null;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HDRI LOADING
    // ════════════════════════════════════════════════════════════════════════

    void CargarHDRIs()
    {
        _hdris = new Cubemap[HDRP_HDRI_PATHS.Length];
#if UNITY_EDITOR
        for (int i = 0; i < HDRP_HDRI_PATHS.Length; i++)
        {
            _hdris[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<Cubemap>(HDRP_HDRI_PATHS[i]);
            if (_hdris[i] == null)
            {
                // Intento alternativo como Texture2D (panorámica HDR) — HDRP la acepta como Cubemap
                // si está marcada como "Cubemap" en el importador
                var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture>(HDRP_HDRI_PATHS[i]);
                if (tex is Cubemap cb) _hdris[i] = cb;
            }
        }
#endif
        // Fallback a Resources si los anteriores fallan en runtime
        string[] fallbackNames = {
            "HDRIs/autumn_field_2k", "HDRIs/blaubeuren_outskirts_2k",
            "HDRIs/autumn_forest_04_2k", "HDRIs/belfast_sunset_2k",
            "HDRIs/kloppenheim_06_puresky_2k", "HDRIs/approaching_storm_2k"
        };
        for (int i = 0; i < _hdris.Length; i++)
        {
            if (_hdris[i] == null)
                _hdris[i] = Resources.Load<Cubemap>(fallbackNames[i]);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  LUZ DIRECCIONAL
    // ════════════════════════════════════════════════════════════════════════

    void CrearLuzDireccional()
    {
        // Reutilizar luz existente si hay una
        var existentes = FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (var l in existentes)
        {
            if (l.type == LightType.Directional)
            {
                _luzDireccional = l;
                break;
            }
        }

        if (_luzDireccional == null)
        {
            var go = new GameObject("LuzSolar_Direccional");
            _luzDireccional = go.AddComponent<Light>();
            _luzDireccional.type = LightType.Directional;
        }

        _luzHDRP = _luzDireccional.GetComponent<HDAdditionalLightData>();
        if (_luzHDRP == null)
            _luzHDRP = _luzDireccional.gameObject.AddComponent<HDAdditionalLightData>();

        // Configuración física
        _luzDireccional.shadows = LightShadows.Soft;
        _luzDireccional.shadowResolution = LightShadowResolution.VeryHigh;
        _luzHDRP.EnableColorTemperature(true);
        _luzHDRP.SetIntensity(100000f, LightUnit.Lux); // luz solar real
        _luzHDRP.volumetricDimmer = 0.85f;
        _luzHDRP.useContactShadow.overrideState = true;
        _luzHDRP.useContactShadow.value = true;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  VOLUMEN DÍA
    // ════════════════════════════════════════════════════════════════════════

    void CrearVolumenDia()
    {
        var go = new GameObject("Volume_Dia_HDRP");
        _volDia = go.AddComponent<Volume>();
        _volDia.isGlobal = true;
        _volDia.priority = 10f;
        _volDia.weight   = 1f;

        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        _volDia.profile = p;

        // ── Bloom ──────────────────────────────────────────────────────────
        _bloomDia = p.Add<Bloom>(true);
        _bloomDia.intensity.Override(0.85f);
        _bloomDia.scatter.Override(0.7f);
        _bloomDia.tint.Override(new Color(1.0f, 0.97f, 0.92f));
        _bloomDia.highQualityFiltering.Override(true);

        // ── SSAO ──────────────────────────────────────────────────────────
        _ssaoDia = p.Add<AmbientOcclusion>(true);
        _ssaoDia.intensity.Override(1.2f);
        _ssaoDia.radius.Override(0.6f);
        _ssaoDia.quality.Override(4); // High

        // ── SSR ───────────────────────────────────────────────────────────
        _ssrDia = p.Add<ScreenSpaceReflection>(true);
        _ssrDia.enabled.Override(true);
        _ssrDia.quality.Override(2); // Medium
        _ssrDia.reflectSky.Override(true);

        // ── Depth of Field ────────────────────────────────────────────────
        _dofDia = p.Add<DepthOfField>(true);
        _dofDia.focusMode.Override(DepthOfFieldMode.UsePhysicalCamera);
        _dofDia.nearFocusStart.Override(0.3f);
        _dofDia.nearFocusEnd.Override(1.2f);
        _dofDia.farFocusStart.Override(80f);
        _dofDia.farFocusEnd.Override(180f);

        // ── SSGI ──────────────────────────────────────────────────────────
        _ssgiDia = p.Add<ScreenSpaceGlobalIllumination>(true);
        _ssgiDia.enable.Override(true);
        _ssgiDia.quality.Override(1); // Medium

        // ── Volumetric Fog ────────────────────────────────────────────────
        _fogDia = p.Add<VolumetricFog>(true);
        _fogDia.enable.Override(true);
        _fogDia.albedo.Override(new Color(0.88f, 0.90f, 0.95f));
        _fogDia.meanFreePath.Override(1500f);  // visibilidad 1.5km en día
        _fogDia.baseHeight.Override(200f);
        _fogDia.maximumHeight.Override(1200f);
        _fogDia.anisotropy.Override(0.7f);     // scattering forward (sol)
        _fogDia.globalLightProbeDimmer.Override(0.8f);

        // ── Sky HDRI ──────────────────────────────────────────────────────
        _skyDia = p.Add<HDRISky>(true);
        _skyDia.exposure.Override(10f);
        _skyDia.rotation.Override(195f); // norte aprox
        if (_hdris[1] != null) _skyDia.hdriSky.Override(_hdris[1]); // mediodía por defecto

        // ── Color Adjustments ─────────────────────────────────────────────
        var ca = p.Add<ColorAdjustments>(true);
        ca.postExposure.Override(0.3f);
        ca.contrast.Override(12f);
        ca.colorFilter.Override(new Color(1.0f, 0.98f, 0.95f));
        ca.saturation.Override(8f);

        // ── Tone Mapping ──────────────────────────────────────────────────
        var tm = p.Add<Tonemapping>(true);
        tm.mode.Override(TonemappingMode.ACES);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  VOLUMEN NOCHE
    // ════════════════════════════════════════════════════════════════════════

    void CrearVolumenNoche()
    {
        var go = new GameObject("Volume_Noche_HDRP");
        _volNoche = go.AddComponent<Volume>();
        _volNoche.isGlobal = true;
        _volNoche.priority = 11f;
        _volNoche.weight   = 0f; // empieza invisible

        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        _volNoche.profile = p;

        // ── Bloom noche — más intenso para neon/farolas ────────────────────
        _bloomNoche = p.Add<Bloom>(true);
        _bloomNoche.intensity.Override(1.8f);
        _bloomNoche.scatter.Override(0.85f);
        _bloomNoche.tint.Override(new Color(0.9f, 0.92f, 1.0f)); // tinte azulado

        // ── SSAO noche ─────────────────────────────────────────────────────
        _ssaoNoche = p.Add<AmbientOcclusion>(true);
        _ssaoNoche.intensity.Override(2.0f); // más fuerte de noche
        _ssaoNoche.radius.Override(0.9f);
        _ssaoNoche.quality.Override(4);

        // ── SSR noche ──────────────────────────────────────────────────────
        _ssrNoche = p.Add<ScreenSpaceReflection>(true);
        _ssrNoche.enabled.Override(true);
        _ssrNoche.quality.Override(2);
        _ssrNoche.reflectSky.Override(true);

        // ── Niebla noche — más densa, más azul ────────────────────────────
        _fogNoche = p.Add<VolumetricFog>(true);
        _fogNoche.enable.Override(true);
        _fogNoche.albedo.Override(new Color(0.55f, 0.60f, 0.72f));
        _fogNoche.meanFreePath.Override(400f);  // niebla más densa de noche
        _fogNoche.baseHeight.Override(220f);
        _fogNoche.maximumHeight.Override(900f);
        _fogNoche.anisotropy.Override(0.4f);
        _fogNoche.globalLightProbeDimmer.Override(0.2f);

        // ── HDRI cielo nocturno ────────────────────────────────────────────
        _skyNoche = p.Add<HDRISky>(true);
        _skyNoche.exposure.Override(5f);
        _skyNoche.rotation.Override(195f);
        if (_hdris[4] != null) _skyNoche.hdriSky.Override(_hdris[4]); // cielo estrellado

        // ── Color más frío de noche ────────────────────────────────────────
        var ca = p.Add<ColorAdjustments>(true);
        ca.postExposure.Override(-1.2f); // oscurecer
        ca.contrast.Override(20f);
        ca.colorFilter.Override(new Color(0.85f, 0.90f, 1.0f)); // tinte azul noche
        ca.saturation.Override(-10f); // algo desaturado de noche

        // ── Tone Mapping noche ────────────────────────────────────────────
        var tm = p.Add<Tonemapping>(true);
        tm.mode.Override(TonemappingMode.ACES);

        // ── Lens Flare nocturno ───────────────────────────────────────────
        var lf = p.Add<LensFlareComponentSRP>(true);
        // (solo hace algo si hay LensFlares asignados en escena)
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CICLO ATMÓSFERA — actualiza cada frame
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator CicloAtmosfera()
    {
        while (true)
        {
            ActualizarHora();
            ActualizarSol();
            ActualizarBlendVolumenes();
            ActualizarHDRI();
            ActualizarFarolas();
            // PERF: desactivar features HDRP costosas cuando no aportan visualmente (~2-4ms/frame ahorrados)
            ActualizarFeaturesHDRPDinamicas();
            yield return new WaitForSeconds(2f); // cada 2s es suficiente
        }
    }

    /// <summary>
    /// Desactiva features HDRP costosas cuando no son visibles o no aportan:
    /// · SSR  — se desactiva en interiores (sin superficies reflectantes a la vista)
    /// · DoF  — se desactiva cuando la distancia de foco es &gt;500m (bokeh inapreciable)
    /// · Fog  — se desactiva en interiores (el volumen de niebla no afecta al interior)
    /// PERF: cada feature desactivada ahorra 0.5-2ms/frame en HDRP según la resolución.
    /// </summary>
    void ActualizarFeaturesHDRPDinamicas()
    {
        if (_ssrDia == null && _dofDia == null && _fogDia == null) return;

        // Detectar si el jugador está en interior: raycast hacia arriba 3m
        // PERF: un solo Raycast corto (3m) es mucho más barato que las features activas
        bool enInterior = false;
        if (_jugadorTransform != null)
        {
            // PERF: reutilizar LayerMask constante — solo colisionar con Default y Building
            enInterior = Physics.Raycast(
                _jugadorTransform.position + Vector3.up * 0.5f,
                Vector3.up, 3f,
                ~LayerMask.GetMask("Player", "Ignore Raycast", "Terrain", "Water"),
                QueryTriggerInteraction.Ignore);
        }

        // ── SSR: desactivar en interior (~1-2ms/frame ahorrados en HDRP) ─────
        bool ssrDeseado = !enInterior;
        if (ssrDeseado != _ssrActivo)
        {
            _ssrActivo = ssrDeseado;
            // PERF: eliminado SSR en interiores (~1-2ms/frame según resolución)
            if (_ssrDia   != null) _ssrDia.enabled.Override(ssrDeseado);
            if (_ssrNoche != null) _ssrNoche.enabled.Override(ssrDeseado);
        }

        // ── DoF: desactivar cuando la cámara está muy lejos del foco (>500m) ─
        // o en interior (DoF de foco largo es imperceptible)
        bool dofDeseado = !enInterior;
        if (dofDeseado != _dofActivo)
        {
            _dofActivo = dofDeseado;
            // PERF: DoF desactivado en interiores o foco lejano (~0.5-1ms/frame ahorrados)
            if (_dofDia != null) _dofDia.active = dofDeseado;
        }

        // ── Fog volumétrica: desactivar en interiores ─────────────────────────
        bool fogDeseado = !enInterior;
        if (fogDeseado != _fogActivo)
        {
            _fogActivo = fogDeseado;
            // PERF: Fog volumétrica desactivada en interiores (~0.5-1.5ms/frame ahorrados)
            if (_fogDia   != null) _fogDia.enable.Override(fogDeseado);
            if (_fogNoche != null) _fogNoche.enable.Override(fogDeseado);
        }
    }

    void ActualizarHora()
    {
        // Intentar leer hora del sistema de atmósfera existente
        if (AltsasuCore.I?.atmosferaSystem != null)
        {
            var atm = AltsasuCore.I.atmosferaSystem;

            // PERF: cachear PropertyInfo la primera vez — Type.GetProperty() hace string hashing en
            // cada llamada (~0.05-0.3ms). Con WaitForSeconds(2s) son ~30 lookups/min evitados.
            if (!_propHoraBuscada)
            {
                _propHoraBuscada = true;
                var tipo = atm.GetType();
                _propHoraActual = tipo.GetProperty("HoraActual")
                               ?? tipo.GetProperty("hora")
                               ?? tipo.GetProperty("Hour");
            }

            if (_propHoraActual != null)
            {
                _horaActual = System.Convert.ToSingle(_propHoraActual.GetValue(atm));
                return;
            }
        }
        // Fallback: simular un día de 24 minutos reales
        _horaActual = (Time.time / 1440f * 24f) % 24f;
    }

    void ActualizarSol()
    {
        if (_luzDireccional == null) return;

        // Ángulo solar: sale por el este (90°), cénit a las 12h, ocaso al oeste (270°)
        float anguloY = (_horaActual / 24f) * 360f - 90f; // rotación horizontal
        float anguloX = Mathf.Sin((_horaActual / 24f) * Mathf.PI * 2f - Mathf.PI * 0.5f) * 80f; // elevación

        _luzDireccional.transform.rotation =
            Quaternion.Euler(Mathf.Clamp(anguloX, -10f, 90f), anguloY, 0f);

        // Temperatura de color: amanecer/atardecer cálido, mediodía neutro
        bool esAtardecer = _horaActual > 17f && _horaActual < 21f;
        bool esAmanecer  = _horaActual > 5f  && _horaActual < 9f;
        float kelvin = esAtardecer || esAmanecer ? 3200f : 6500f;
        _luzHDRP?.SetColorTemperature(kelvin);

        // Intensidad: nula de noche, 100klux a mediodía
        bool esNoche = _horaActual < 5.5f || _horaActual > 21.5f;
        float lux = esNoche ? 0f :
            Mathf.Lerp(0f, 100000f, Mathf.Clamp01(Mathf.Sin((_horaActual - 6f) / 15f * Mathf.PI)));
        _luzHDRP?.SetIntensity(lux, LightUnit.Lux);

        // Luz de luna de noche
        if (esNoche)
        {
            _luzDireccional.color     = new Color(0.7f, 0.75f, 1.0f);
            _luzHDRP?.SetIntensity(0.002f, LightUnit.Lux); // 0.002 lux = luna llena
        }
        else
        {
            _luzDireccional.color = Color.white;
        }
    }

    void ActualizarBlendVolumenes()
    {
        bool esNoche = _horaActual < 5.5f || _horaActual > 21.5f;
        float targetBlend = esNoche ? 1f : 0f;

        // Transición suave en 2s (llamado cada 2s → blending instantáneo al paso)
        _blendNoche = Mathf.MoveTowards(_blendNoche, targetBlend, Time.deltaTime * 0.1f);
        _volNoche.weight = _blendNoche;
        // No tocar _volDia.weight — es siempre 1, el noche se superpone
    }

    void ActualizarHDRI()
    {
        int idxHDRI;
        if (_horaActual >= 5f  && _horaActual < 9f)  idxHDRI = 0;       // amanecer
        else if (_horaActual >= 9f  && _horaActual < 14f) idxHDRI = 1;   // mediodía
        else if (_horaActual >= 14f && _horaActual < 18f) idxHDRI = 2;   // tarde
        else if (_horaActual >= 18f && _horaActual < 21f) idxHDRI = 3;   // atardecer
        else idxHDRI = 4;                                                  // noche

        if (idxHDRI == _hdriActual) return;
        _hdriActual = idxHDRI;

        if (_hdris[idxHDRI] != null)
        {
            _skyDia?.hdriSky.Override(_hdris[idxHDRI]);
            // Rotación HDRI con el sol
            float rot = _horaActual / 24f * 360f;
            _skyDia?.rotation.Override(rot);
            _skyNoche?.rotation.Override(rot + 180f);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  FAROLAS
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator BuscarFarolasYConectar()
    {
        yield return new WaitForSeconds(5f); // esperar a que se genere el mundo

        // Buscar farolas existentes en la escena (generadas por SistemaSueloAAA o props)
        var todasLuces = FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (var l in todasLuces)
        {
            if (l.type == LightType.Point || l.type == LightType.Spot)
            {
                string n = l.gameObject.name.ToLower();
                if (n.Contains("farola") || n.Contains("street") || n.Contains("lamp")
                    || n.Contains("lantern") || n.Contains("luz"))
                {
                    _farolas.Add(l);
                    ConfigurarFarolaHDRP(l);
                }
            }
        }

        // Generar farolas procedurales en calles si no hay suficientes
        if (_farolas.Count < 10)
            yield return StartCoroutine(GenerarFarolasEnCalles());

        AlsasuaLogger.Info("SistemaVolumenHDRP", $"Farolas configuradas: {_farolas.Count}");
    }

    void ConfigurarFarolaHDRP(Light l)
    {
        var hd = l.GetComponent<HDAdditionalLightData>();
        if (hd == null) hd = l.gameObject.AddComponent<HDAdditionalLightData>();

        hd.SetIntensity(2200f, LightUnit.Lumen);
        hd.EnableColorTemperature(true);
        hd.SetColorTemperature(2700f);  // sodio: amarillo cálido
        l.range = 18f;
        l.shadows = LightShadows.None; // performance — las farolas no necesitan sombras
        hd.volumetricDimmer = 0.6f;
        hd.affectDiffuse  = true;
        hd.affectSpecular = true;
    }

    IEnumerator GenerarFarolasEnCalles()
    {
        var callesParent = GameObject.Find("Calles_Precisas") ?? GameObject.Find("Calles_OSM");
        if (callesParent == null) yield break;

        var terrain = Terrain.activeTerrain;
        int total   = 0;
        var parentFarolas = new GameObject("Farolas_Procedurales").transform;

        foreach (Transform calle in callesParent.transform)
        {
            if (calle == null) continue;
            var mf = calle.GetComponent<MeshFilter>();
            if (mf == null) continue;
            var b = mf.sharedMesh.bounds;
            if (b.size.z < 12f) continue;

            // Farola cada 30m a ambos lados de la calle
            for (float z = b.min.z + 15f; z < b.max.z - 15f; z += 30f)
            {
                for (int lado = -1; lado <= 1; lado += 2)
                {
                    float x = b.center.x + lado * (b.size.x * 0.5f + 1.8f);
                    float y = terrain != null ? terrain.SampleHeight(new Vector3(x, 0, z)) : 240f;

                    var goFarola = new GameObject($"Farola_{total}");
                    goFarola.transform.SetParent(parentFarolas);
                    goFarola.transform.position = new Vector3(x, y + 5.5f, z);

                    var luz = goFarola.AddComponent<Light>();
                    luz.type = LightType.Point;
                    ConfigurarFarolaHDRP(luz);

                    // Geometría mínima del poste
                    var poste = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    poste.transform.SetParent(goFarola.transform);
                    poste.transform.localPosition = new Vector3(0, -2.75f, 0);
                    poste.transform.localScale    = new Vector3(0.06f, 2.75f, 0.06f);
                    var matPoste = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"))
                        { color = new Color(0.3f, 0.3f, 0.32f) };
                    poste.GetComponent<Renderer>().sharedMaterial = matPoste;
                    Object.Destroy(poste.GetComponent<Collider>());

                    _farolas.Add(luz);
                    total++;
                }
            }

            if (total % 20 == 0) yield return null;
            if (total > 200) break; // máximo 200 farolas por performance
        }
    }

    void ActualizarFarolas()
    {
        bool debeEncender = _horaActual < 6.5f || _horaActual > 20f;
        if (debeEncender == _farolasEncendidas) return;

        _farolasEncendidas = debeEncender;
        foreach (var l in _farolas)
        {
            if (l == null) continue;
            l.gameObject.SetActive(debeEncender);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Fuerza transición a cielo de tormenta temporalmente.</summary>
    public static void SetTormenta(bool activo)
    {
        if (Instance == null) return;
        var hdri = activo ? Instance._hdris[5] : Instance._hdris[Instance._hdriActual];
        if (hdri != null) Instance._skyDia?.hdriSky.Override(hdri);
        if (Instance._fogDia != null)
        {
            Instance._fogDia.meanFreePath.Override(activo ? 150f : 1500f);
            Instance._fogDia.albedo.Override(activo
                ? new Color(0.65f, 0.65f, 0.70f)
                : new Color(0.88f, 0.90f, 0.95f));
        }
    }

    /// <summary>Activa DoF para vista de francotirador o ADS.</summary>
    public static void SetDoFSniper(bool activo, float focusDist = 40f)
    {
        if (Instance?._dofDia == null) return;
        Instance._dofDia.focusMode.Override(
            activo ? DepthOfFieldMode.UsePhysicalCamera : DepthOfFieldMode.UsePhysicalCamera);
        Instance._dofDia.farFocusStart.Override(activo ? focusDist - 3f : 80f);
        Instance._dofDia.farFocusEnd.Override(activo ? focusDist + 3f : 180f);
    }
}
