#pragma warning disable CS0618 // API HDRP/Unity obsoleta (p.ej. Light intensity); migracion pendiente, sigue funcional
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
    Volume _volTransicion;   // hora dorada: amanecer (6-9h) y atardecer (17-21h)

    // ── Efectos volumen transición ─────────────────────────────────────────
    Bloom          _bloomTransicion;
    ColorAdjustments _caTransicion;
    float          _blendTransicion;

    // ── Efectos volumen día ────────────────────────────────────────────────
    Bloom             _bloomDia;
    Texture2D         _lensDirtTex;   // textura procedural de suciedad de lente
    ScreenSpaceAmbientOcclusion  _ssaoDia;
    ScreenSpaceReflection _ssrDia;
    DepthOfField      _dofDia;
    GlobalIllumination _ssgiDia;   // HDRP: el VolumeComponent público es GlobalIllumination
    Fog               _fogDia;   // HDRP: VolumetricFog era internal; el público es Fog
    HDRISky           _skyDia;
    PhysicallyBasedSky _pbskyDia;
    ProbeVolumesOptions _apvOpcionesDia; // APV: calidad/fugas GI (de facto global, ver CrearVolumenDia)

    // ── Efectos volumen noche ──────────────────────────────────────────────
    Bloom             _bloomNoche;
    ScreenSpaceAmbientOcclusion  _ssaoNoche;
    ScreenSpaceReflection _ssrNoche;
    Fog               _fogNoche;   // HDRP: Fog (VolumetricFog es internal)
    HDRISky           _skyNoche;

    // ── Luz direccional ────────────────────────────────────────────────────
    Light             _luzDireccional;
    HDAdditionalLightData _luzHDRP;

    // ── HDRIs indexados por hora y clima ─────────────────────────────────
    // 0=amanecer  1=mediodía  2=tarde  3=atardecer  4=noche  5=tormenta
    // 6=nieve     7=overcast/nublado   (nuevos)
    static readonly string[] HDRP_HDRI_PATHS = {
        "Assets/HDRIs/autumn_field_2k.hdr",           // 0 amanecer 5-9h
        "Assets/HDRIs/blaubeuren_outskirts_2k.hdr",   // 1 mediodía 9-14h
        "Assets/HDRIs/autumn_forest_04_2k.hdr",       // 2 tarde 14-18h
        "Assets/HDRIs/belfast_sunset_2k.hdr",         // 3 atardecer 18-21h
        "Assets/HDRIs/kloppenheim_06_puresky_2k.hdr", // 4 noche 21-5h
        "Assets/HDRIs/approaching_storm_2k.hdr",      // 5 tormenta (override)
        "Assets/HDRIs/snowy_hillside_2k.hdr",         // 6 nieve
        "Assets/HDRIs/overcast_soil.hdr",             // 7 nublado/lluvia
    };

    Cubemap[] _hdris;
    int       _hdriActual = -1;

    // ── Farolas ────────────────────────────────────────────────────────────
    readonly List<Light> _farolas = new();
    bool  _farolasEncendidas;
    float _flickerTimer;

    // ── Estado ────────────────────────────────────────────────────────────
    float _horaActual = 12f; // 0-24
    float _blendNoche;       // 0=día, 1=noche

    // ── Shader globals ─────────────────────────────────────────────────────
    static readonly int ID_NightLevel = Shader.PropertyToID("_GlobalNightLevel");
    static readonly int ID_FocusDist  = Shader.PropertyToID("_GlobalFocusDist");

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
        CrearVolumenTransicion();
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
            "HDRIs/autumn_field_2k",          "HDRIs/blaubeuren_outskirts_2k",
            "HDRIs/autumn_forest_04_2k",      "HDRIs/belfast_sunset_2k",
            "HDRIs/kloppenheim_06_puresky_2k","HDRIs/approaching_storm_2k",
            "HDRIs/snowy_hillside_2k",        "HDRIs/overcast_soil",
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
        _luzHDRP.useContactShadow.useOverride = true;   // HDRP: BoolScalableSettingValue
        _luzHDRP.useContactShadow.@override = true;
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

        // ── Exposición (FIX: sin esto el sol físico de 100k lux quemaba la imagen a blanco) ──
        // Exposición FIJA de día (evita que la automática mida del cielo y oscurezca todo).
        // ~12.5 EV ≈ exterior soleado. Ajustable si queda oscuro (bajar) o claro (subir).
        // FIX (jun 2026): EV 8.5 fijo con sol de ~100k lux quedaba ~5 pasos
        // sobreexpuesto → todo deslumbraba. Automática con límites 11-15
        // (igual que Volume_Bootstrap) mide la escena real día/atardecer.
        var exp = p.Add<Exposure>(true);
        exp.mode.Override(ExposureMode.Automatic);
        exp.limitMin.Override(11f);
        exp.limitMax.Override(15f);

        // ── Bloom ──────────────────────────────────────────────────────────
        // FIX (jun 2026): 0.85 convertía cada highlight especular en destello
        // cegador en movimiento. 0.25 = acento sutil solo sobre luces reales.
        _bloomDia = p.Add<Bloom>(true);
        _bloomDia.intensity.Override(0.25f);
        _bloomDia.scatter.Override(0.6f);
        _bloomDia.tint.Override(new Color(1.0f, 0.97f, 0.92f));
        _bloomDia.highQualityFiltering = true;   // HDRP: es bool (propiedad), no BoolParameter

        // ── SSAO ──────────────────────────────────────────────────────────
        _ssaoDia = p.Add<ScreenSpaceAmbientOcclusion>(true);
        _ssaoDia.intensity.Override(1.2f);
        _ssaoDia.radius.Override(0.6f);
        _ssaoDia.quality.Override(2); // FIX FPS: Medium — High costaba el doble por nada visible

        // ── SSR ───────────────────────────────────────────────────────────
        // FIX FPS (jun 2026): SSR desactivado por defecto — coste alto en un
        // mundo abierto y con reflectSky daba "brillos moviéndose". Reactivable
        // desde SistemaOptimizacion en GPUs holgadas.
        _ssrDia = p.Add<ScreenSpaceReflection>(true);
        _ssrDia.enabled.Override(false);
        _ssrDia.quality.Override(1); // Low si se reactiva
        _ssrDia.reflectSky.Override(false);

        // ── Depth of Field ────────────────────────────────────────────────
        // FIX mundo abierto: en Manual, far blur a 180m emborronaba edificios y
        // montes (efecto maqueta). Far blur empujado a varios km — solo suaviza
        // el anillo de fondo; el pueblo queda nítido como en GTA.
        // FIX FPS (jun 2026): DoF apagado por defecto (gather pass cara a 1080p+).
        _dofDia = p.Add<DepthOfField>(true);
        _dofDia.focusMode.Override(DepthOfFieldMode.Off);
        _dofDia.nearFocusStart.Override(0.3f);
        _dofDia.nearFocusEnd.Override(1.2f);
        _dofDia.farFocusStart.Override(3000f);
        _dofDia.farFocusEnd.Override(9000f);

        // ── SSGI ──────────────────────────────────────────────────────────
        // FIX FPS (jun 2026): SSGI es de lo más caro de HDRP (ray marching por
        // píxel) y su aporte visual de día es sutil. OFF por defecto.
        _ssgiDia = p.Add<GlobalIllumination>(true);
        _ssgiDia.enable.Override(false);
        _ssgiDia.quality.Override(0); // Low si se reactiva

        // ── Volumetric Fog ────────────────────────────────────────────────
        // NOTA (2026-06-03): VolumetricFog (internal) → Fog (público). El toggle
        // pasa de .enable a .enabled, y se añade .enableVolumetricFog para
        // conservar el scattering volumétrico que daba el componente anterior.
        _fogDia = p.Add<Fog>(true);
        _fogDia.enabled.Override(true);
        // FIX FPS (jun 2026): niebla volumétrica OFF — el froxel fog cuesta
        // varios ms/frame; la niebla exponencial analítica da el mismo efecto
        // de bruma en las sierras por casi gratis.
        _fogDia.enableVolumetricFog.Override(false);
        _fogDia.albedo.Override(new Color(0.88f, 0.90f, 0.95f));
        _fogDia.meanFreePath.Override(6500f);  // visibilidad 6.5km: las sierras de fondo asoman con bruma (SistemaMontesFondo)
        _fogDia.maxFogDistance.Override(16000f); // alcanza el anillo de montes antes de fundir a cielo
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

        // TAA: en HDRP el anti-aliasing es por camara (HDAdditionalCameraData),
        // no un VolumeComponent. Se activa en SceneBootstrapper.AnadirHDRPCameraData.

        // ── Nubes volumétricas ─────────────────────────────────────────────
        // FIX FPS (jun 2026): OFF — el ray-marching de nubes + sus sombras
        // cuesta 3-6 ms/frame. El cielo HDRI ya trae nubes pintadas.
        var nubes = p.Add<VolumetricClouds>(true);
        nubes.enable.Override(false);
        nubes.shadows.Override(false);

        // ── Contact Shadows — contacto suelo/objeto (quita el look "flotante") ──
        var cs = p.Add<ContactShadows>(true);
        cs.enable.Override(true);
        cs.length.Override(0.6f);
        cs.opacity.Override(0.8f);

        // ── Micro Shadows — sombra del detalle de normal map a sol directo ──
        var micro = p.Add<MicroShadowing>(true);
        micro.enable.Override(true);
        micro.opacity.Override(0.85f);

        // ── Lens Dirt — mancha de cámara en bloom de explosiones ──────────
        // Textura procedural 16×16: degradado radial con manchas aleatorias.
        _lensDirtTex = GenerarLensDirt();
        _bloomDia.dirtTexture.Override(_lensDirtTex);
        _bloomDia.dirtIntensity.Override(0f); // 0 en reposo; se sube desde SetLensDirt()

        // ── Adaptive Probe Volumes — ajuste GLOBAL de calidad/fugas de la GI ──
        // Va solo en el profile de día: el de noche no incluye este componente, así
        // que esta config sigue activa con cualquier weight de noche (es de facto
        // global). Con bricks gruesos en las sierras (ver ConstructorAPV), el ruido
        // de muestreo rompe el banding y los bias reducen el light-leaking sin subir
        // densidad de probes — que es lo que encarece VRAM/bake en un mundo de 14 km.
        _apvOpcionesDia = p.Add<ProbeVolumesOptions>(true);
        _apvOpcionesDia.leakReductionMode.Override(APVLeakReductionMode.Quality);
        _apvOpcionesDia.normalBias.Override(0.2f);
        _apvOpcionesDia.viewBias.Override(0.2f);
        _apvOpcionesDia.samplingNoise.Override(0.15f);
        _apvOpcionesDia.minValidDotProductValue.Override(0.1f);
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
        _ssaoNoche = p.Add<ScreenSpaceAmbientOcclusion>(true);
        _ssaoNoche.intensity.Override(2.0f); // más fuerte de noche
        _ssaoNoche.radius.Override(0.9f);
        _ssaoNoche.quality.Override(4);

        // ── SSR noche ──────────────────────────────────────────────────────
        _ssrNoche = p.Add<ScreenSpaceReflection>(true);
        _ssrNoche.enabled.Override(true);
        _ssrNoche.quality.Override(2);
        _ssrNoche.reflectSky.Override(true);

        // ── Niebla noche — más densa, más azul ────────────────────────────
        _fogNoche = p.Add<Fog>(true);
        _fogNoche.enabled.Override(true);
        _fogNoche.enableVolumetricFog.Override(true);
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

        // ── Lens Flare nocturno (screen-space) ────────────────────────────
        // NOTA (2026-06-03): LensFlareComponentSRP es un MonoBehaviour, NO un
        // VolumeComponent → no se puede añadir a un VolumeProfile. El efecto de
        // volumen correcto es ScreenSpaceLensFlare. Se añade con valores por
        // defecto; el dueño puede subir .intensity para que se note de noche.
        p.Add<ScreenSpaceLensFlare>(true);
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
            ActualizarTransicion();
            ActualizarHDRI();
            ActualizarFarolas();
            ActualizarShaderGlobals();
            ActualizarFlickerFarolas();
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
            if (_fogDia   != null) _fogDia.enabled.Override(fogDeseado);
            if (_fogNoche != null) _fogNoche.enabled.Override(fogDeseado);
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
                // BUG FIX (auditoría): SistemaAtmosfera expone la hora como "HoraDelDia";
                // antes sólo se buscaba HoraActual/hora/Hour → null → ciclo día/noche roto.
                _propHoraActual = tipo.GetProperty("HoraDelDia")
                               ?? tipo.GetProperty("HoraActual")
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
        _luzDireccional.colorTemperature = kelvin;   // HDRP: sin SetColorTemperature; se fija en el Light

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
        l.colorTemperature = 2700f;  // sodio: amarillo cálido (HDRP: se fija en el Light)
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


    /// <summary>
    /// Actualiza la distancia de foco del DoF físico.
    /// Llamado por SistemaPolish.ActualizarAutoFocus() cada 0.2 s con
    /// el resultado del raycast al centro de pantalla.
    /// </summary>
    // ── Bloom flash de explosiones ──────────────────────────────────────
    Coroutine _coBloomFlash;

    /// <summary>Subida puntual de bloom (explosiones). Decae sola en ~400ms.</summary>
    public static void BloomFlash(float pico)
    {
        if (Instance == null || Instance._bloomDia == null) return;
        if (Instance._coBloomFlash != null) Instance.StopCoroutine(Instance._coBloomFlash);
        Instance._coBloomFlash = Instance.StartCoroutine(Instance.CoBloomFlash(pico));
    }

    IEnumerator CoBloomFlash(float pico)
    {
        const float dur = 0.4f, baseInt = 0.85f;
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            _bloomDia.intensity.Override(Mathf.Lerp(pico, baseInt, t / dur));
            yield return null;
        }
        _bloomDia.intensity.Override(baseInt);
        _coBloomFlash = null;
    }

    public static void SetFocusDistance(float distancia)
    {
        if (Instance?._dofDia == null) return;
        // En UsePhysicalCamera mode el foco se controla con farFocusStart/End
        float d = Mathf.Clamp(distancia, 1f, 500f);
        Instance._dofDia.farFocusStart.Override(Mathf.Max(0.5f, d - d * 0.08f));
        Instance._dofDia.farFocusEnd.Override(d + d * 0.12f);
        Shader.SetGlobalFloat(ID_FocusDist, d);
    }

    /// <summary>
    /// Activa la textura de lens dirt en el bloom.
    /// intensidad 0 = off, 1 = máximo. Llamar desde SistemaPolish.ExplosionBloom.
    /// </summary>
    public static void SetLensDirt(float intensidad)
    {
        if (Instance?._bloomDia == null) return;
        Instance._bloomDia.dirtIntensity.Override(Mathf.Clamp(intensidad * 4f, 0f, 4f));
    }

    /// <summary>Fuerza transición a cielo de tormenta temporalmente.</summary>
    public static void SetTormenta(bool activo)
    {
        if (Instance == null) return;
        var hdri = activo ? Instance._hdris[5] : Instance._hdris[Instance._hdriActual];
        if (hdri != null) Instance._skyDia?.hdriSky.Override(hdri);
        if (Instance._fogDia != null)
        {
            Instance._fogDia.meanFreePath.Override(activo ? 150f : 6500f);
            Instance._fogDia.albedo.Override(activo
                ? new Color(0.65f, 0.65f, 0.70f)
                : new Color(0.88f, 0.90f, 0.95f));
        }
    }

    /// <summary>
    /// Aplica el HDRI correcto según el estado del clima.
    /// Llama desde SistemaClima.CambiarClima() para mantener el cielo coherente.
    /// </summary>
    public static void SetHdriClima(SistemaClima.EstadoClima clima)
    {
        if (Instance == null) return;

        // _hdris array: 0=amanecer 1=mediodía 2=tarde 3=atardecer 4=noche 5=tormenta 6=nieve 7=nublado
        int idx = clima switch
        {
            SistemaClima.EstadoClima.Tormenta     => 5,
            SistemaClima.EstadoClima.NieveLigera  => Instance._hdris.Length > 6 ? 6 : 1,
            SistemaClima.EstadoClima.Nublado      => Instance._hdris.Length > 7 ? 7 : 1,
            SistemaClima.EstadoClima.LluviaLigera => Instance._hdris.Length > 7 ? 7 : 1,
            _                                     => Instance._hdriActual >= 0 ? Instance._hdriActual : 1,
        };

        if (idx < Instance._hdris.Length && Instance._hdris[idx] != null)
            Instance._skyDia?.hdriSky.Override(Instance._hdris[idx]);
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

    // ── Generador de textura Lens Dirt ────────────────────────────────────

    static Texture2D GenerarLensDirt()
    {
        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.name = "LensDirt_Procedural";
        var pixels = new Color[S * S];
        var rng    = new System.Random(42); // semilla fija → determinista

        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            // Degradado radial oscuro en los bordes (vignette de suciedad)
            float nx = (x / (float)(S - 1)) * 2f - 1f;
            float ny = (y / (float)(S - 1)) * 2f - 1f;
            float r2 = nx * nx + ny * ny;
            float radial = Mathf.Clamp01(r2 * 0.7f);

            // Manchas de polvo: ruido random sparse
            float dust = rng.NextDouble() < 0.04 ? (float)rng.NextDouble() * 0.4f : 0f;

            float brightness = radial * 0.6f + dust;
            pixels[y * S + x] = new Color(brightness, brightness * 0.95f, brightness * 0.9f, 1f);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // ── Flicker de farolas durante tormenta ───────────────────────────────

    void ActualizarFlickerFarolas()
    {
        var clima = AltsasuCore.I?.climaSystem;
        bool tormenta = clima != null
            && clima.climaActual == SistemaClima.EstadoClima.Tormenta;

        if (!tormenta || !_farolasEncendidas) return;

        _flickerTimer -= 2f; // se llama cada 2 s desde CicloAtmosfera
        // Probabilidad de flicker: ~8% cada ciclo de 2 s
        if (UnityEngine.Random.value > 0.08f) return;

        // Apagar una farola aleatoria 0.12 s y volver a encender
        int idx = UnityEngine.Random.Range(0, _farolas.Count);
        var l = _farolas.Count > idx ? _farolas[idx] : null;
        if (l == null || !l.gameObject.activeSelf) return;

        StartCoroutine(FlickerLuz(l));
    }

    System.Collections.IEnumerator FlickerLuz(Light l)
    {
        var hd = l.GetComponent<HDAdditionalLightData>();
        float orig = hd != null ? 2200f : l.intensity;
        // Pulso: off → dim → off → on
        hd?.SetIntensity(0f,    LightUnit.Lumen); yield return new WaitForSeconds(0.04f);
        hd?.SetIntensity(500f,  LightUnit.Lumen); yield return new WaitForSeconds(0.02f);
        hd?.SetIntensity(0f,    LightUnit.Lumen); yield return new WaitForSeconds(0.06f);
        hd?.SetIntensity(orig,  LightUnit.Lumen);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  VOLUMEN TRANSICIÓN — hora dorada (amanecer / atardecer)
    // ════════════════════════════════════════════════════════════════════════

    void CrearVolumenTransicion()
    {
        var go = new GameObject("Volume_Transicion_HDRP");
        _volTransicion = go.AddComponent<Volume>();
        _volTransicion.isGlobal = true;
        _volTransicion.priority = 12f;
        _volTransicion.weight   = 0f;

        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        _volTransicion.profile = p;

        // Bloom cálido — naranja
        _bloomTransicion = p.Add<Bloom>(true);
        _bloomTransicion.intensity.Override(2.2f);
        _bloomTransicion.scatter.Override(0.75f);
        _bloomTransicion.tint.Override(new Color(1.0f, 0.72f, 0.35f));

        // Color grading hora dorada
        _caTransicion = p.Add<ColorAdjustments>(true);
        _caTransicion.postExposure.Override(0.8f);
        _caTransicion.contrast.Override(18f);
        _caTransicion.colorFilter.Override(new Color(1.0f, 0.82f, 0.60f));
        _caTransicion.saturation.Override(20f);

        // Niebla dorada con scatter forward — diagonales de luz visibles
        var fogT = p.Add<Fog>(true);
        fogT.enabled.Override(true);
        fogT.enableVolumetricFog.Override(true);
        fogT.albedo.Override(new Color(0.90f, 0.72f, 0.50f));
        fogT.meanFreePath.Override(800f);
        fogT.baseHeight.Override(150f);
        fogT.maximumHeight.Override(600f);
        fogT.anisotropy.Override(0.85f);
        fogT.globalLightProbeDimmer.Override(0.65f);

        // Vignette bordes oscuros naranjas
        var vig = p.Add<Vignette>(true);
        vig.intensity.Override(0.32f);
        vig.smoothness.Override(0.5f);
        vig.color.Override(new Color(0.15f, 0.06f, 0.02f));
    }

    void ActualizarTransicion()
    {
        if (_volTransicion == null) return;
        // Pico en 7.5h (amanecer) y en 19h (atardecer)
        float peso = 0f;
        if (_horaActual >= 6f && _horaActual < 9f)
            peso = 1f - Mathf.Abs((_horaActual - 7.5f) / 1.5f);
        else if (_horaActual >= 17f && _horaActual < 21f)
            peso = 1f - Mathf.Abs((_horaActual - 19f) / 2f);
        peso = Mathf.Clamp01(peso);
        _blendTransicion = Mathf.MoveTowards(_blendTransicion, peso, Time.deltaTime * 0.05f);
        _volTransicion.weight = _blendTransicion;
    }

    void ActualizarShaderGlobals()
    {
        // _GlobalNightLevel (0=día, 1=noche): edificios lo leen para iluminar ventanas
        Shader.SetGlobalFloat(ID_NightLevel, _blendNoche);
        // _GlobalFocusDist: escrito por SetFocusDistance() desde SistemaPolish
        // Aquí solo refrescamos el valor actual del DoF por si acaso.
        if (_dofDia != null)
            Shader.SetGlobalFloat(ID_FocusDist, _dofDia.farFocusStart.value);
    }


}
