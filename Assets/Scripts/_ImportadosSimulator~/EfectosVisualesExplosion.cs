// Assets/Scripts/EfectosVisualesExplosion.cs
using UnityEngine;

/// <summary>
/// Motor de Efectos Visuales (VFX) Desacoplado (V15 Clean Architecture).
/// Responsabilidad Única: Generar partículas paramétricas sin intervenir en Físicas o IA.
/// </summary>
public class EfectosVisualesExplosion : MonoBehaviour
{
    public void GenerarVFX(float radio, float duracionFuego)
    {
        EfectoBolaFuego(radio);
        EfectoHumo(radio);
        EfectoRescoldo();
        EfectoLlamas(radio, duracionFuego);
        EfectoOnda(radio);
    }

    private void EfectoBolaFuego(float radio)
    {
        var go = new GameObject("BolaDeFuego");
        go.transform.position = transform.position;
        var ps = go.AddComponent<ParticleSystem>();

        var main          = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSpeed    = new ParticleSystem.MinMaxCurve(4f, 14f);
        main.startSize     = new ParticleSystem.MinMaxCurve(radio * 0.5f, radio * 1.2f);
        main.startColor    = new ParticleSystem.MinMaxGradient(
                                 new Color(1f, 0.6f, 0.1f, 0.9f),
                                 new Color(1f, 0.2f, 0f,   0.7f));
        main.gravityModifier = -0.3f;
        main.maxParticles    = 40;

        var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 35) });
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = radio * 0.3f;

        ps.Play();
        Destroy(go, 2f);
    }

    private void EfectoHumo(float radio)
    {
        var go = new GameObject("HumoExplosion");
        go.transform.position = transform.position;
        var ps = go.AddComponent<ParticleSystem>();

        var main           = ps.main;
        main.startLifetime  = new ParticleSystem.MinMaxCurve(3f, 6f);
        main.startSpeed     = new ParticleSystem.MinMaxCurve(2f, 6f);
        main.startSize      = new ParticleSystem.MinMaxCurve(radio * 0.8f, radio * 2.5f);
        main.startColor     = new ParticleSystem.MinMaxGradient(new Color(0.15f, 0.12f, 0.10f, 0.8f), new Color(0.35f, 0.30f, 0.25f, 0.5f));
        main.gravityModifier = -0.5f; main.maxParticles = 25;

        var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 20) });
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = radio * 0.4f;

        ps.Play();
        Destroy(go, 8f);
    }

    private void EfectoRescoldo()
    {
        var go = new GameObject("Rescoldos");
        go.transform.position = transform.position;
        var ps = go.AddComponent<ParticleSystem>();

        var main           = ps.main;
        main.startLifetime  = new ParticleSystem.MinMaxCurve(1f, 2.5f);
        main.startSpeed     = new ParticleSystem.MinMaxCurve(5f, 20f);
        main.startSize      = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
        main.startColor     = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.2f), new Color(1f, 0.4f, 0f));
        main.gravityModifier = 0.6f; main.maxParticles = 80;

        var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.5f;

        ps.Play();
        Destroy(go, 3f);
    }

    private void EfectoLlamas(float radio, float duracionFuego)
    {
        var go = new GameObject("LlamasExplosion");
        go.transform.position = transform.position + Vector3.up * 0.5f;
        var ps = go.AddComponent<ParticleSystem>();

        var main           = ps.main;
        main.duration       = duracionFuego;
        main.loop           = true;
        main.startLifetime  = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
        main.startSpeed     = new ParticleSystem.MinMaxCurve(1f, 3f);
        main.startSize      = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        main.startColor     = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.1f, 0.85f), new Color(1f, 0.2f, 0.0f, 0.65f));
        main.gravityModifier = -0.2f; main.maxParticles = 60;

        var em = ps.emission; em.rateOverTime = 30f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = radio * 0.25f;

        ps.Play();
        Destroy(go, duracionFuego + 1f);
    }

    private void EfectoOnda(float radio)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.transform.position   = transform.position;
        go.transform.localScale = Vector3.zero;
        Destroy(go.GetComponent<Collider>());

        var rend     = go.GetComponent<Renderer>();
        var mat      = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
        mat.color    = new Color(1f, 0.8f, 0.5f, 0.3f);
        rend.material = mat;

        var lerper = go.AddComponent<OndaExplosionAnim>();
        lerper.Init(radio * 2.5f, 0.3f);
    }
}

public class OndaExplosionAnim : MonoBehaviour
{
    private float maxScale, duracion, timer;
    private Renderer rend;
    private Material matInstancia;

    public void Init(float max, float dur)
    {
        maxScale = max;
        duracion = Mathf.Max(dur, 0.01f);
        rend     = GetComponent<Renderer>();
        matInstancia = rend != null ? rend.material : null;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / duracion);
        transform.localScale = Vector3.one * Mathf.Lerp(0f, maxScale, t);
        if (matInstancia != null) matInstancia.color = new Color(1f, 0.8f, 0.5f, Mathf.Lerp(0.4f, 0f, t));
        if (timer >= duracion) Destroy(gameObject);
    }

    private void OnDestroy() { if (matInstancia != null) Destroy(matInstancia); }
}
