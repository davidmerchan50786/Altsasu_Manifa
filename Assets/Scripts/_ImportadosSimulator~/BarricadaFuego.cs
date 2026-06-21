// Assets/Scripts/BarricadaFuego.cs
// Barricada con fuego y humo — obstáculo en la escena
// Se crea por código, no necesita assets externos

using UnityEngine;
using System.Collections.Generic;

public class BarricadaFuego : MonoBehaviour
{
    [Header("═══ BARRICADA ═══")]
    [SerializeField] private bool  fuegoPrendido  = true;
    [SerializeField] private float intensidadFuego = 1f;  // 0-1
    [SerializeField] private int   vida            = 200;
    [SerializeField] private float radioBloqueo    = 2.5f;

    // BUG 5 FIX: rastrear materiales creados con new Material() para destruirlos en OnDestroy().
    // MatURP() era static → los materiales no pertenecían a ninguna instancia y Unity no los
    // liberaba al salir del modo Play en el Editor, acumulando leaks cada vez que se entraba.
    private readonly List<Material> _matsCreados = new List<Material>();

    private ParticleSystem psFuego;
    private ParticleSystem psHumo;
    private ParticleSystem psChispas;
    private Light          luzFuego;
    private float          timerParpadeo = 0f;

    // ═══════════════════════════════════════════════════════════════════════
    //  UNITY
    // ═══════════════════════════════════════════════════════════════════════

    private void Start()
    {
        CrearEstructura();
        if (fuegoPrendido) PrenderFuego();
    }

    private void Update()
    {
        if (!fuegoPrendido || luzFuego == null) return;

        // Parpadeo de llama
        timerParpadeo += Time.deltaTime * 8f;
        luzFuego.intensity = Mathf.Lerp(2.5f, 5.5f, (Mathf.Sin(timerParpadeo) + 1f) / 2f)
                           * intensidadFuego;
        luzFuego.range     = Mathf.Lerp(6f, 10f, (Mathf.Sin(timerParpadeo * 1.3f) + 1f) / 2f);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ESTRUCTURA VISUAL (por código)
    // ═══════════════════════════════════════════════════════════════════════

    private void CrearEstructura()
    {
        // Palés / contenedor de basura (cajas apiladas)
        CrearCaja(new Vector3(0f,    0.25f, 0f),    new Vector3(2.5f, 0.5f, 0.8f), new Color(0.45f, 0.32f, 0.18f));
        CrearCaja(new Vector3(0.2f,  0.75f, 0.1f),  new Vector3(1.8f, 0.5f, 0.7f), new Color(0.40f, 0.28f, 0.15f));
        CrearCaja(new Vector3(-0.2f, 1.15f, -0.05f),new Vector3(1.4f, 0.4f, 0.65f),new Color(0.35f, 0.24f, 0.12f));

        // Neumáticos
        CrearCilindro(new Vector3(-0.8f, 0.25f, 0.5f),  new Vector3(0.55f, 0.25f, 0.55f), new Color(0.08f, 0.08f, 0.08f));
        CrearCilindro(new Vector3(0.8f,  0.25f, 0.45f), new Vector3(0.55f, 0.25f, 0.55f), new Color(0.08f, 0.08f, 0.08f));
        CrearCilindro(new Vector3(0f,    0.55f, 0.5f),  new Vector3(0.55f, 0.25f, 0.55f), new Color(0.1f,  0.1f,  0.1f));

        // Varillas/palo metálico
        CrearCaja(new Vector3(1.1f, 1f, 0f), new Vector3(0.08f, 2f, 0.08f), new Color(0.4f, 0.4f, 0.4f));
    }

    // Crea un Material compatible con URP (evita el magenta por shader incorrecto)
    // BUG FIX: null guard — new Material(null) lanza NullReferenceException si ningún shader está disponible.
    // Fallback en cadena: Lit → Unlit → Standard → InternalErrorShader (shader de error interno de Unity).
    // BUG 5 FIX: convertido de static a instancia para poder registrar en _matsCreados.
    private Material MatURP(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Universal Render Pipeline/Unlit")
                  ?? Shader.Find("Standard");
        if (shader == null)
        {
            Debug.LogError("[BarricadaFuego] MatURP: ningún shader URP/Standard encontrado. " +
                           "Incluye 'Universal Render Pipeline/Lit' en Always Included Shaders.");
            shader = Shader.Find("Hidden/InternalErrorShader");
            if (shader == null) return null;
        }
        var mat = new Material(shader) { color = color };
        _matsCreados.Add(mat);
        return mat;
    }

    private void OnDestroy()
    {
        // BUG 5 FIX: destruir todas las instancias de material creadas para evitar leaks en el Editor.
        foreach (var m in _matsCreados)
            if (m != null) Object.Destroy(m);
        _matsCreados.Clear();
    }

    private void CrearCaja(Vector3 pos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.SetParent(transform);
        go.transform.localPosition = pos;
        go.transform.localScale    = scale;
        go.transform.localRotation = Quaternion.Euler(0f, Random.Range(-5f, 5f), 0f);
        var matCaja = MatURP(color);
        // BUG 5 FIX: sharedMaterial asigna sin crear una segunda instancia de material.
        if (matCaja != null) go.GetComponent<Renderer>().sharedMaterial = matCaja;

        // Rigidbody para que las explosiones la muevan
        // FreezeRotationX/Z evita que las cajas se vuelquen solas al iniciarse
        var rb = go.AddComponent<Rigidbody>();
        rb.mass        = 40f;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    private void CrearCilindro(Vector3 pos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.transform.SetParent(transform);
        go.transform.localPosition = pos;
        go.transform.localScale    = scale;
        var matCil = MatURP(color);
        // BUG 5 FIX: ídem.
        if (matCil != null) go.GetComponent<Renderer>().sharedMaterial = matCil;

        var rb = go.AddComponent<Rigidbody>();
        rb.mass        = 15f;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  FUEGO Y HUMO
    // ═══════════════════════════════════════════════════════════════════════

    public void PrenderFuego()
    {
        fuegoPrendido = true;
        CrearFuego();
        CrearHumo();
        CrearChispas();
        CrearLuzFuego();
    }

    private void CrearFuego()
    {
        var go = new GameObject("Fuego");
        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        psFuego = go.AddComponent<ParticleSystem>();

        var main          = psFuego.main;
        main.loop          = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSpeed    = new ParticleSystem.MinMaxCurve(1.5f, 4.0f);
        main.startSize     = new ParticleSystem.MinMaxCurve(0.4f, 1.2f) ;
        main.startColor    = new ParticleSystem.MinMaxGradient(
                                 new Color(1.0f, 0.55f, 0.1f, 0.9f),
                                 new Color(1.0f, 0.2f,  0.0f, 0.7f));
        main.gravityModifier = -0.15f;
        main.maxParticles    = 80;

        var em = psFuego.emission;
        em.rateOverTime = 35f * intensidadFuego;

        var shape      = psFuego.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius   = 0.6f;

        var sizeOL = psFuego.sizeOverLifetime;
        sizeOL.enabled = true;
        var curva = new AnimationCurve();
        curva.AddKey(0f, 0.3f); curva.AddKey(0.5f, 1f); curva.AddKey(1f, 0f);
        sizeOL.size = new ParticleSystem.MinMaxCurve(1f, curva);

        psFuego.Play();
    }

    private void CrearHumo()
    {
        var go = new GameObject("Humo");
        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(0f, 2.5f, 0f);
        psHumo = go.AddComponent<ParticleSystem>();

        var main          = psHumo.main;
        main.loop          = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
        main.startSpeed    = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        main.startSize     = new ParticleSystem.MinMaxCurve(1f, 3.5f);
        main.startColor    = new ParticleSystem.MinMaxGradient(
                                 new Color(0.12f, 0.10f, 0.09f, 0.7f),
                                 new Color(0.30f, 0.25f, 0.20f, 0.4f));
        main.gravityModifier = -0.3f;
        main.maxParticles    = 40;

        var em = psHumo.emission;
        em.rateOverTime = 10f;

        var shape      = psHumo.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius   = 0.4f;

        psHumo.Play();
    }

    private void CrearChispas()
    {
        var go = new GameObject("Chispas");
        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(0f, 1.4f, 0f);
        psChispas = go.AddComponent<ParticleSystem>();

        var main           = psChispas.main;
        main.loop           = true;
        main.startLifetime  = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
        main.startSpeed     = new ParticleSystem.MinMaxCurve(2f, 6f);
        main.startSize      = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.startColor     = new ParticleSystem.MinMaxGradient(
                                  new Color(1f, 0.9f, 0.4f), new Color(1f, 0.5f, 0.1f));
        main.gravityModifier = 0.3f;
        main.maxParticles    = 60;

        var em = psChispas.emission;
        em.rateOverTime = 15f;

        var shape      = psChispas.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle    = 40f;
        shape.radius   = 0.3f;

        psChispas.Play();
    }

    private void CrearLuzFuego()
    {
        var go = new GameObject("LuzFuego");
        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(0f, 1f, 0f);
        luzFuego           = go.AddComponent<Light>();
        luzFuego.type      = LightType.Point;
        luzFuego.color     = new Color(1f, 0.55f, 0.15f);
        luzFuego.intensity = 4f;
        luzFuego.range     = 8f;
        luzFuego.shadows   = LightShadows.Soft;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DAÑO (explosiones pueden destruirla)
    // ═══════════════════════════════════════════════════════════════════════

    public void RecibirDano(int cantidad)
    {
        // BUG 21 FIX: si la barricada ya fue destruida (vida <= 0), ignorar daño adicional.
        // Sin este guard, una segunda explosión llama a .Stop() en sistemas de partículas
        // ya detenidos y desactiva luces que ya están desactivadas → no rompe nada pero
        // genera trabajo inútil y puede provocar que 'vida' desborde hacia negativo.
        if (vida <= 0) return;

        vida -= cantidad;
        if (vida <= 0)
        {
            if (psFuego  != null) psFuego.Stop();
            if (psHumo   != null) psHumo.Stop();
            if (psChispas!= null) psChispas.Stop();
            if (luzFuego != null) luzFuego.enabled = false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CREACIÓN ESTÁTICA
    // ═══════════════════════════════════════════════════════════════════════

    public static BarricadaFuego Crear(Vector3 posicion, float rotacionY = 0f)
    {
        var go = new GameObject("Barricada");
        go.transform.position = posicion;
        go.transform.rotation = Quaternion.Euler(0f, rotacionY, 0f);
        return go.AddComponent<BarricadaFuego>();
    }
}
