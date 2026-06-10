// Assets/Scripts/SistemaLluviaVisual.cs
// ═══════════════════════════════════════════════════════════════════════════
//  LLUVIA VISUAL — runtime
//   · Rain ParticleSystem que sigue al jugador (vista de primera persona)
//   · Salpicaduras al suelo
//   · Rayos esporádicos: flash de luz + sonido + delay físico
//   · Niebla volumétrica densifica con la lluvia
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public class SistemaLluviaVisual : MonoBehaviour
{
    [Header("═══ INTENSIDAD ═══")]
    [Range(0f, 1f)] public float intensidad = 0f; // 0 = sin lluvia, 1 = tormenta
    public float velocidadCambio = 0.1f;

    [Header("═══ RAYOS ═══")]
    public bool generarRayos = true;
    public float intervaloRayosMin = 8f;
    public float intervaloRayosMax = 35f;

    Transform _player;
    ParticleSystem _ps;
    ParticleSystem _psSalpicaduras;
    Light          _luzRayo;
    Volume         _volumen;
    float _timerProximoRayo;
    float _intensidadActual;

    void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        _player = p != null ? p.transform : null;

        _ps = CrearSistemaLluvia();
        _psSalpicaduras = CrearSistemaSalpicaduras();
        _luzRayo = CrearLuzRayo();
        _volumen = FindFirstObjectByType<Volume>();
        ResetTimerRayo();
    }

    void Update()
    {
        _intensidadActual = Mathf.MoveTowards(_intensidadActual, intensidad,
                                              velocidadCambio * Time.deltaTime);

        if (_player != null)
        {
            transform.position = _player.position + Vector3.up * 30f;
        }

        // Modulación del rate
        if (_ps != null)
        {
            var em = _ps.emission;
            em.rateOverTime = 4000f * _intensidadActual;
            if (_intensidadActual > 0.05f && !_ps.isPlaying) _ps.Play();
            else if (_intensidadActual < 0.05f && _ps.isPlaying) _ps.Stop();
        }
        if (_psSalpicaduras != null)
        {
            var em = _psSalpicaduras.emission;
            em.rateOverTime = 600f * _intensidadActual;
            if (_intensidadActual > 0.05f && !_psSalpicaduras.isPlaying) _psSalpicaduras.Play();
            else if (_intensidadActual < 0.05f && _psSalpicaduras.isPlaying) _psSalpicaduras.Stop();
        }

        // Rayos
        if (generarRayos && _intensidadActual > 0.5f)
        {
            _timerProximoRayo -= Time.deltaTime;
            if (_timerProximoRayo <= 0f)
            {
                StartCoroutine(DispararRayo());
                ResetTimerRayo();
            }
        }

        // Fog volumétrica
        if (_volumen != null && _volumen.profile != null
            && _volumen.profile.TryGet<Fog>(out var fog))
        {
            // Lluvia → niebla más espesa
            float meanPath = Mathf.Lerp(6000f, 800f, _intensidadActual);
            fog.meanFreePath.value = meanPath;
        }
    }

    System.Collections.IEnumerator DispararRayo()
    {
        if (_luzRayo == null) yield break;
        _luzRayo.enabled  = true;
        _luzRayo.intensity = 80000f;

        yield return new WaitForSeconds(0.05f);
        _luzRayo.intensity = 30000f;
        yield return new WaitForSeconds(0.04f);
        _luzRayo.intensity = 60000f;
        yield return new WaitForSeconds(0.06f);
        _luzRayo.enabled = false;
    }

    void ResetTimerRayo() => _timerProximoRayo = Random.Range(intervaloRayosMin, intervaloRayosMax);

    // ─────────────────────────────────────────────────────────────────────

    ParticleSystem CrearSistemaLluvia()
    {
        var go = new GameObject("Lluvia_PS");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop                = true;
        main.duration            = 5f;
        main.startLifetime       = 1.2f;
        main.startSpeed          = 30f;
        main.startSize           = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
        main.startColor          = new Color(0.7f, 0.8f, 0.9f, 0.6f);
        main.maxParticles        = 5000;
        main.simulationSpace     = ParticleSystemSimulationSpace.World;
        main.gravityModifier     = 0.2f;

        var em = ps.emission;
        em.rateOverTime = 0f;

        var sh = ps.shape;
        sh.shapeType    = ParticleSystemShapeType.Box;
        sh.scale    = new Vector3(80f, 1f, 80f);
        sh.rotation = new Vector3(0, 0, 0);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.y = new ParticleSystem.MinMaxCurve(-25f);
        vel.x = new ParticleSystem.MinMaxCurve(-2f, 2f);

        var psr = go.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Stretch;
        psr.lengthScale = 4f;
        psr.velocityScale = 0.3f;
        var matLluvia = new Material(Shader.Find("HDRP/Unlit"));
        matLluvia.SetColor("_UnlitColor", new Color(0.85f, 0.92f, 0.98f, 0.6f));
        matLluvia.SetFloat("_SurfaceType", 1);
        psr.material = matLluvia;

        ps.Stop();
        return ps;
    }

    ParticleSystem CrearSistemaSalpicaduras()
    {
        var go = new GameObject("Salpicaduras_PS");
        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(0, -28f, 0);

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop              = true;
        main.startLifetime     = 0.4f;
        main.startSpeed        = 0.5f;
        main.startSize         = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
        main.startColor        = new Color(0.85f, 0.9f, 0.95f, 0.5f);
        main.maxParticles      = 1000;
        main.simulationSpace   = ParticleSystemSimulationSpace.World;

        var sh = ps.shape;
        sh.shapeType  = ParticleSystemShapeType.Box;
        sh.scale  = new Vector3(40f, 0.1f, 40f);

        var sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size    = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0,1,1,0));

        var psr = go.GetComponent<ParticleSystemRenderer>();
        var mat = new Material(Shader.Find("HDRP/Unlit"));
        mat.SetColor("_UnlitColor", new Color(0.85f, 0.92f, 0.98f, 0.4f));
        mat.SetFloat("_SurfaceType", 1);
        psr.material = mat;

        ps.Stop();
        return ps;
    }

    Light CrearLuzRayo()
    {
        var go = new GameObject("LuzRayo");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;
        go.transform.rotation = Quaternion.Euler(45f, 0f, 0f);

        var luz = go.AddComponent<Light>();
        luz.type      = LightType.Directional;
        luz.color     = new Color(0.85f, 0.92f, 1f);
        luz.intensity = 0f;
        luz.shadows   = LightShadows.Soft;
        luz.enabled   = false;

        var hd = go.AddComponent<HDAdditionalLightData>();
        return luz;
    }
}
