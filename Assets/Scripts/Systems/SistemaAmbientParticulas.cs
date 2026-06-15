// Assets/Scripts/SistemaAmbientParticulas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE PARTÍCULAS AMBIENTE — efectos por zona
//
//  Añade micro-detalles atmosféricos que diferencian las zonas de Alsasua:
//    · Vapor de alcantarilla (HerrikoPlaza, calles principales)
//    · Polen flotante (zonas de prado, monte Aralar)
//    · Motas de polvo en haces de luz (interiores, soportales)
//    · Chispas de soldadura (zona industrial Polígono Isasia)
//    · Hojas caídas girando (zonas de bosque)
//
//  Cada efecto es un ParticleSystem creado en runtime, sin prefabs.
//  GPU instancing activado. Los efectos se habilitan solo cuando el
//  jugador está a < radioActivacion metros.
//
//  Performance: solo 1-2 sistemas activos simultáneamente.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-35)]
public class SistemaAmbientParticulas : MonoBehaviour
{
    public static SistemaAmbientParticulas Instance { get; private set; }

    enum TipoEfecto { Vapor, Polen, PolvoLuz, ChispaSoldadura, HojasGiro }

    struct ZonaEfecto
    {
        public string        nombre;
        public Vector3       posicion;
        public float         radio;
        public TipoEfecto    tipo;
        public ParticleSystem ps;
        public bool          activo;
    }

    readonly List<ZonaEfecto> _zonas = new();
    float _checkTimer;
    const float CHECK_INTERVAL = 2f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start() => StartCoroutine(InicializarTras(6f));

    IEnumerator InicializarTras(float d)
    {
        yield return new WaitForSeconds(d);
        RegistrarZonasAlsasua();
        AlsasuaLogger.Info("AmbientParticulas", $"{_zonas.Count} zonas de efecto registradas.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ZONAS DE EFECTO — coordenadas reales de Alsasua
    // ════════════════════════════════════════════════════════════════════════

    void RegistrarZonasAlsasua()
    {
        // Vapor de alcantarilla — plaza central
        AnadirZona("Vapor_HerrikoPlaza", GeoDataAlsasua.HerrikoPlaza,   18f, TipoEfecto.Vapor);
        AnadirZona("Vapor_Estacion",     GeoDataAlsasua.EstacionTren,    12f, TipoEfecto.Vapor);

        // Polen — prados y monte
        AnadirZona("Polen_Aralar",    GeoDataAlsasua.MonteAralar,   40f, TipoEfecto.Polen);
        AnadirZona("Polen_BarrioN",   GeoDataAlsasua.BarrioNorte,   25f, TipoEfecto.Polen);

        // Polvo en haces de luz — interiores
        AnadirZona("Polvo_Interior1",
            GeoDataAlsasua.HerrikoPlaza + new Vector3(5f, 0f, 5f), 8f, TipoEfecto.PolvoLuz);

        // Chispas — zona industrial
        AnadirZona("Chispas_Isasia", GeoDataAlsasua.PoligonoIsasia, 20f, TipoEfecto.ChispaSoldadura);

        // Hojas girando — bosques
        AnadirZona("Hojas_Aralar1",  GeoDataAlsasua.MonteAralar + new Vector3(50f, 0, 0f), 30f, TipoEfecto.HojasGiro);
        AnadirZona("Hojas_Aralar2",  GeoDataAlsasua.MonteAralar + new Vector3(-30f, 0, 20f), 30f, TipoEfecto.HojasGiro);
    }

    void AnadirZona(string nombre, Vector3 posicion, float radio, TipoEfecto tipo)
    {
        var go = new GameObject($"AmbientPS_{nombre}");
        go.transform.SetParent(transform, false);
        go.transform.position = posicion + Vector3.up * 0.3f;

        var ps = go.AddComponent<ParticleSystem>();
        ConfigurarPS(ps, tipo);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        _zonas.Add(new ZonaEfecto
        {
            nombre   = nombre,
            posicion = posicion,
            radio    = radio,
            tipo     = tipo,
            ps       = ps,
            activo   = false,
        });
    }

    // ════════════════════════════════════════════════════════════════════════
    //  UPDATE — activar/desactivar por proximidad
    // ════════════════════════════════════════════════════════════════════════

    void Update()
    {
        _checkTimer -= Time.deltaTime;
        if (_checkTimer > 0f) return;
        _checkTimer = CHECK_INTERVAL;

        var jugador = AltsasuCore.Jugador;
        if (jugador == null) return;

        // Consumidor del tier de calidad: en modo Performance (tier 3) las
        // partículas ambiente (puro eye-candy) se apagan para recuperar frame.
        // + Degrade DINÁMICO: respeta el FactorCarga del orquestador → si el frame se
        //   va de presupuesto (caída de 60 fps), se pausan antes de que se note el tirón.
        var orq = GlobalSimulationOrchestrator.Instancia;
        bool hayHolgura = orq == null || orq.FactorCarga >= orq.Config.productoresPausaFactor;
        bool permitidoPorCalidad = SistemaOptimizacion.TierCalidad < 3 && hayHolgura;

        for (int i = 0; i < _zonas.Count; i++)
        {
            var z = _zonas[i];
            float dist2 = (z.posicion - jugador.position).sqrMagnitude;
            bool debeActivo = permitidoPorCalidad && dist2 < z.radio * z.radio;

            if (debeActivo == z.activo) continue;

            var tmp = _zonas[i];
            tmp.activo = debeActivo;
            _zonas[i]  = tmp;

            if (debeActivo) z.ps.Play();
            else            z.ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CONFIGURACIÓN DE CADA TIPO DE EFECTO
    // ════════════════════════════════════════════════════════════════════════

    static void ConfigurarPS(ParticleSystem ps, TipoEfecto tipo)
    {
        var main     = ps.main;
        var emission = ps.emission;
        var shape    = ps.shape;
        var vel      = ps.velocityOverLifetime;
        var sol      = ps.sizeOverLifetime;
        var col      = ps.colorOverLifetime;

        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake     = false;
        main.loop            = true;

        switch (tipo)
        {
            case TipoEfecto.Vapor:
                main.startLifetime    = new ParticleSystem.MinMaxCurve(2f, 5f);
                main.startSpeed       = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
                main.startSize        = new ParticleSystem.MinMaxCurve(0.08f, 0.25f);
                main.gravityModifier  = -0.05f;
                main.startColor       = new ParticleSystem.MinMaxGradient(
                    new Color(1f, 1f, 1f, 0.35f), new Color(0.9f, 0.95f, 1f, 0.15f));
                emission.rateOverTime = 8f;
                shape.shapeType       = ParticleSystemShapeType.Circle;
                shape.radius          = 0.4f;
                vel.enabled           = true;
                vel.space             = ParticleSystemSimulationSpace.Local;
                vel.y                 = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
                // Fade-out suave
                var gradVapor = new Gradient();
                gradVapor.SetKeys(new[]{ new GradientColorKey(Color.white,0f), new GradientColorKey(Color.white,1f)},
                                  new[]{ new GradientAlphaKey(0f,0f), new GradientAlphaKey(0.4f,0.3f), new GradientAlphaKey(0f,1f)});
                col.enabled   = true;
                col.color     = new ParticleSystem.MinMaxGradient(gradVapor);
                break;

            case TipoEfecto.Polen:
                main.startLifetime    = new ParticleSystem.MinMaxCurve(6f, 12f);
                main.startSpeed       = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
                main.startSize        = new ParticleSystem.MinMaxCurve(0.015f, 0.045f);
                main.gravityModifier  = -0.02f;
                main.startColor       = new ParticleSystem.MinMaxGradient(
                    new Color(1f, 0.95f, 0.3f, 0.70f), new Color(0.9f, 1f, 0.4f, 0.40f));
                emission.rateOverTime = 15f;
                shape.shapeType       = ParticleSystemShapeType.Sphere;
                shape.radius          = 6f;
                vel.enabled           = true;
                vel.space             = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                vel.y = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
                vel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                break;

            case TipoEfecto.PolvoLuz:
                main.startLifetime    = new ParticleSystem.MinMaxCurve(4f, 8f);
                main.startSpeed       = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
                main.startSize        = new ParticleSystem.MinMaxCurve(0.008f, 0.022f);
                main.gravityModifier  = 0.005f;
                main.startColor       = new ParticleSystem.MinMaxGradient(
                    new Color(1f, 0.95f, 0.80f, 0.60f), new Color(1f, 1f, 1f, 0.30f));
                emission.rateOverTime = 30f;
                shape.shapeType       = ParticleSystemShapeType.Box;
                shape.scale           = new Vector3(3f, 4f, 0.2f);
                break;

            case TipoEfecto.ChispaSoldadura:
                main.startLifetime    = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
                main.startSpeed       = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
                main.startSize        = new ParticleSystem.MinMaxCurve(0.04f, 0.10f);
                main.gravityModifier  = 1.2f;
                main.startColor       = new ParticleSystem.MinMaxGradient(
                    new Color(1f, 0.75f, 0.10f, 1f), new Color(1f, 0.40f, 0.05f, 0.8f));
                emission.rateOverTime = 0f;
                emission.SetBursts(new[]{ new ParticleSystem.Burst(0f, 8, 18, 10, 2f) });
                shape.shapeType       = ParticleSystemShapeType.Cone;
                shape.angle           = 55f;
                shape.radius          = 0.1f;
                break;

            case TipoEfecto.HojasGiro:
                main.startLifetime    = new ParticleSystem.MinMaxCurve(5f, 10f);
                main.startSpeed       = new ParticleSystem.MinMaxCurve(0.5f, 2f);
                main.startSize        = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
                main.gravityModifier  = 0.08f;
                main.startColor       = new ParticleSystem.MinMaxGradient(
                    new Color(0.55f, 0.38f, 0.10f, 0.90f), new Color(0.28f, 0.45f, 0.10f, 0.75f));
                emission.rateOverTime = 5f;
                shape.shapeType       = ParticleSystemShapeType.Sphere;
                shape.radius          = 8f;
                vel.enabled = true;
                vel.x = new ParticleSystem.MinMaxCurve(-1f, 1f);
                vel.z = new ParticleSystem.MinMaxCurve(-1f, 1f);
                var rotHojas = ps.rotationOverLifetime;
                rotHojas.enabled = true;
                rotHojas.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
                break;
        }

        // Renderer: billboard sin sombras
        var ren = ps.gameObject.GetComponent<ParticleSystemRenderer>();
        ren.renderMode            = ParticleSystemRenderMode.Billboard;
        ren.shadowCastingMode     = UnityEngine.Rendering.ShadowCastingMode.Off;
        ren.receiveShadows        = false;

        // FIX: un ParticleSystemRenderer creado en runtime no trae material → Unity
        // dibuja las partículas con el material de error (magenta). Asignamos un
        // material unlit transparente. Las chispas usan blend aditivo para brillar.
        bool aditivo = tipo == TipoEfecto.ChispaSoldadura;
        ren.sharedMaterial = aditivo ? MatAditivo() : MatAlfa();
    }

    // Materiales compartidos (uno por modo de blend), creados una sola vez y
    // reutilizados entre todas las zonas. El color por partícula viene de
    // ParticleSystem.startColor (vertex color), que estos shaders multiplican.
    static Material _matAlfa, _matAditivo;

    static Material MatAlfa()
    {
        if (_matAlfa != null) return _matAlfa;
        var sh = Shader.Find("Sprites/Default")
              ?? Shader.Find("Unlit/Transparent")
              ?? Shader.Find("Standard");
        _matAlfa = new Material(sh) { name = "AmbientPS_Alfa" };
        return _matAlfa;
    }

    static Material MatAditivo()
    {
        if (_matAditivo != null) return _matAditivo;
        // Legacy additive existe en cualquier pipeline; si está stripeado, cae a alfa.
        var sh = Shader.Find("Legacy Shaders/Particles/Additive")
              ?? Shader.Find("Particles/Standard Unlit")
              ?? Shader.Find("Sprites/Default");
        _matAditivo = new Material(sh) { name = "AmbientPS_Aditivo" };
        return _matAditivo;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }
}
