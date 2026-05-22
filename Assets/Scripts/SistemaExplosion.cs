// Assets/Scripts/SistemaExplosion.cs
// Utilidad estática para detonar explosiones con física, daño en área y VFX.
// Usada por VehiculoNPC, SistemaDestruccion y SistemaBombas.

using UnityEngine;

public static class SistemaExplosion
{
    /// <summary>
    /// Detona una explosión en <paramref name="centro"/>.
    /// </summary>
    /// <param name="centro">Posición del centro de la explosión.</param>
    /// <param name="radio">Radio de efecto (metros).</param>
    /// <param name="fuerzaMax">Fuerza de impulso máxima (Newtons).</param>
    /// <param name="dañoMax">Daño máximo en el epicentro.</param>
    public static void Explotar(Vector3 centro, float radio, float fuerzaMax, float dañoMax = 100f)
    {
        // ── VFX procedural ───────────────────────────────────────────────
        CrearVFXExplosion(centro, radio);

        // ── Física en área ───────────────────────────────────────────────
        var colliders = Physics.OverlapSphere(centro, radio);
        foreach (var col in colliders)
        {
            float dist    = Vector3.Distance(col.transform.position, centro);
            float falloff = 1f - Mathf.Clamp01(dist / radio);

            // Impulso de Rigidbody
            var rb = col.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 dir    = (col.transform.position - centro).normalized + Vector3.up * 0.4f;
                float   fuerza = fuerzaMax * falloff;
                rb.AddForce(dir * fuerza, ForceMode.Impulse);
            }

            // Daño a entidades
            int danoInt = Mathf.RoundToInt(dañoMax * falloff);
            var jugador = col.GetComponent<ControladorJugador>();
            if (jugador != null) jugador.RecibirDano(danoInt);
            var coche = col.GetComponentInParent<ControladorVehiculoJugador>();
            if (coche != null) coche.RecibirDano(danoInt);

            // Daño a barricadas
            var barricada = col.GetComponent<BarricadaFuego>();
            if (barricada != null)
                barricada.RecibirDaño(dañoMax * falloff);
        }

        // ── Flash de post-proceso + polish ──────────────────────────────
        SistemaPostProcesoAAA.FlashExplosion();
        SistemaPolish.Shake(Mathf.Clamp01(radio / 12f));
        SistemaImpactos.Explosion(centro, radio);

        // ── Audio ────────────────────────────────────────────────────────
        if (SistemaMusica.Instance != null && SistemaMusica.Instance.stingEvento != null)
            SistemaMusica.TocarEvento(SistemaMusica.Instance.stingEvento);
    }

    // ── VFX — usa HQ Explosions Pack si disponible, procedural si no ─────────

    static void CrearVFXExplosion(Vector3 centro, float radio)
    {
        // Prioridad: HQ Explosions prefab → procedural
        var cfg = ConfiguradorAssetsAAA.Instance;
        if (cfg?.prefabsExplosion?.Length > 0)
        {
            // Seleccionar nivel según radio: pequeño<3m, mediano<6m, grande>6m
            int nivel = radio < 3f ? 0 : radio < 6f ? 2 : 4;
            nivel = Mathf.Clamp(nivel, 0, cfg.prefabsExplosion.Length - 1);
            var explo = Object.Instantiate(cfg.prefabsExplosion[nivel], centro, Quaternion.identity);
            explo.transform.localScale = Vector3.one * (radio / 4f);
            Object.Destroy(explo, 6f);

            // Añadir fuego persistente en explosiones grandes
            if (radio > 5f && cfg.prefabFuego != null)
            {
                var fire = Object.Instantiate(cfg.prefabFuego, centro, Quaternion.identity);
                fire.transform.localScale = Vector3.one * (radio * 0.3f);
                Object.Destroy(fire, 12f);
            }
            return;
        }

        // Fallback procedural
        var root = new GameObject("Explosion_VFX");
        root.transform.position = centro;

        // Bola de fuego
        var ps = root.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startColor    = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.6f, 0.1f), new Color(1f, 0.2f, 0f));
        main.startSize     = new ParticleSystem.MinMaxCurve(radio * 0.3f, radio * 0.8f);
        main.startSpeed    = new ParticleSystem.MinMaxCurve(radio * 0.5f, radio * 1.5f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.loop          = false;
        main.maxParticles  = 80;

        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 80) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.1f;

        ps.Play();

        // Humo
        var smokeGO = new GameObject("Humo");
        smokeGO.transform.SetParent(root.transform);
        smokeGO.transform.localPosition = Vector3.up * 0.5f;
        var smoke = smokeGO.AddComponent<ParticleSystem>();
        var sm = smoke.main;
        sm.startColor    = new Color(0.25f, 0.25f, 0.25f, 0.6f);
        sm.startSize     = new ParticleSystem.MinMaxCurve(radio * 0.4f, radio * 1.2f);
        sm.startSpeed    = new ParticleSystem.MinMaxCurve(1f, 3f);
        sm.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
        sm.loop          = false;
        sm.maxParticles  = 30;
        var se = smoke.emission;
        se.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });
        smoke.Play();

        // Luz de destello
        var luzGO = new GameObject("FlashLight");
        luzGO.transform.SetParent(root.transform);
        var luz = luzGO.AddComponent<Light>();
        luz.type      = LightType.Point;
        luz.color     = new Color(1f, 0.7f, 0.3f);
        luz.intensity = 8f;
        luz.range     = radio * 3f;

        // Auto-destruir todo
        Object.Destroy(root, 5f);

        // Fade-out de la luz
        if (luz != null)
        {
            // Simple: la luz se desvanece (no podemos usar coroutines en static)
            // El Destroy(root, 5f) la limpia; la intensidad inicial es suficiente para el destello
        }
    }
}
