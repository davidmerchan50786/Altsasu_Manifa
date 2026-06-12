// Assets/Scripts/SistemaHumoFabricas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA HUMO DE FÁBRICAS
//
//  Coloca chimeneas industriales con penachos de humo en el Polígono Isasia
//  (y un par de puntos extra). El humo:
//    · Sube lento, se expande y se disipa (gris industrial).
//    · Se curva con el viento mediante el módulo External Forces del
//      ParticleSystem, que responde al WindZone creado por
//      SistemaVientoVegetacion.
//
//  Antes de esto no existía ningún sistema de humo de fábrica.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[DefaultExecutionOrder(-20)]
public class SistemaHumoFabricas : MonoBehaviour
{
    public static SistemaHumoFabricas Instance { get; private set; }

    [Header("Chimeneas")]
    [Tooltip("Altura de cada chimenea sobre el suelo (m).")]
    public float alturaChimenea = 16f;
    [Tooltip("Intensidad del humo (partículas/seg).")]
    public float emision = 14f;

    // Offsets respecto al Polígono Isasia (en metros) para repartir varias fábricas
    static readonly Vector2[] OFFSETS =
    {
        new(0f,   0f),
        new(40f, 25f),
        new(-35f, 15f),
        new(20f, -40f),
        new(-50f, -30f),
    };

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        int creadas = 0;
        Vector3 baseIndustrial = GeoDataAlsasua.PoligonoIsasia;
        foreach (var off in OFFSETS)
        {
            float x = baseIndustrial.x + off.x;
            float z = baseIndustrial.z + off.y;
            float y = GeoDataAlsasua.AlturaTerreno(x, z);
            CrearChimenea(new Vector3(x, y, z), creadas);
            creadas++;
        }
        AlsasuaLogger.Info("Humo", $"{creadas} chimeneas industriales humeando en Polígono Isasia.");
    }

    void CrearChimenea(Vector3 basePos, int idx)
    {
        var raiz = new GameObject($"Chimenea_{idx}");
        raiz.transform.SetParent(transform, false);
        raiz.transform.position = basePos;

        // ── Nave industrial bajo la chimenea (chapa ondulada real CC0) ───────
        var matChapa = TexturasVivo.ChapaIndustrial ?? MatColor(new Color(0.45f, 0.47f, 0.50f));
        var nave = GameObject.CreatePrimitive(PrimitiveType.Cube);
        nave.name = "Nave";
        nave.transform.SetParent(raiz.transform, false);
        nave.transform.localScale    = new Vector3(18f, 7f, 14f);
        nave.transform.localPosition = new Vector3(Random.Range(-4f, 4f), 3.5f, Random.Range(-4f, 4f));
        nave.GetComponent<Renderer>().sharedMaterial = matChapa;

        // ── Tubo de la chimenea (ladrillo real CC0 con respaldo a color) ─────
        var matTubo = TexturasVivo.Ladrillo ?? MatColor(new Color(0.42f, 0.36f, 0.32f));
        var tubo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tubo.name = "Tubo";
        tubo.transform.SetParent(raiz.transform, false);
        tubo.transform.localScale    = new Vector3(2.2f, alturaChimenea * 0.5f, 2.2f);
        tubo.transform.localPosition = new Vector3(0f, alturaChimenea * 0.5f, 0f);
        tubo.GetComponent<Renderer>().sharedMaterial = matTubo;

        // ── Penacho de humo ──────────────────────────────────────────────────
        var humoGO = new GameObject("Humo");
        humoGO.transform.SetParent(raiz.transform, false);
        humoGO.transform.localPosition = new Vector3(0f, alturaChimenea + 0.5f, 0f);

        var ps = humoGO.AddComponent<ParticleSystem>();
        ps.Stop();

        var main = ps.main;
        main.startLifetime  = new ParticleSystem.MinMaxCurve(7f, 11f);
        main.startSpeed     = new ParticleSystem.MinMaxCurve(2.2f, 3.4f);
        main.startSize      = new ParticleSystem.MinMaxCurve(3.5f, 6f);
        main.startColor     = new Color(0.55f, 0.55f, 0.57f, 0.45f);
        main.gravityModifier = -0.03f;                 // ligeramente ascendente
        main.maxParticles   = 220;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = ps.emission;
        em.rateOverTime = emision;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle     = 8f;
        shape.radius    = 0.8f;
        shape.rotation  = new Vector3(-90f, 0f, 0f);   // apuntando hacia arriba

        // Crece y se disipa con el tiempo
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.4f), new Keyframe(0.4f, 1f), new Keyframe(1f, 2.4f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(0.5f, 0.5f, 0.52f), 0f),
                    new GradientColorKey(new Color(0.7f, 0.7f, 0.72f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.5f, 0.15f),
                    new GradientAlphaKey(0.3f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        // El humo se curva con el viento (WindZone de SistemaVientoVegetacion)
        var ext = ps.externalForces;
        ext.enabled = true;
        ext.multiplier = 1.4f;

        // Material de partícula gris translúcido
        var rend = ps.GetComponent<ParticleSystemRenderer>();
        var shHumo = Shader.Find("HDRP/Lit") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
        var matHumo = new Material(shHumo) { name = "Mat_Humo" };
        if (matHumo.HasProperty("_BaseColor")) matHumo.SetColor("_BaseColor", new Color(0.6f, 0.6f, 0.62f, 0.4f));
        rend.sharedMaterial = matHumo;
        rend.sortingFudge = 2f;

        ps.Play();
    }

    static Material MatColor(Color c)
    {
        var m = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
        return m;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
