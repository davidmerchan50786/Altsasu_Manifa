// Assets/Scripts/Runtime/SistemaDañoFisicoVehiculo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DAÑO FÍSICO POR COLISIÓN — convierte impulso de choque en HP y estados visuales
//
//  Añadir al mismo GO que VehiculoBase (VehiculoNPC lo añade en OnAwakeVehiculo;
//  ControladorVehiculoJugador también). Nada más necesario.
//
//  Flujo:
//    OnCollisionEnter → calcular impulso → VehiculoBase.RecibirDano()
//    → HP% → EstadoVisual (Intacto / Golpeado / Grave / Llamas)
//    → actualizar MPB de todos los Renderer (sin crear instancias de Material)
//    → activar/desactivar partículas de humo y fuego
//    → apagar faros si HP < 20%
//
//  El flash de impacto (emissive spike) desaparece en duracionFlash segundos.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[DisallowMultipleComponent]
public sealed class SistemaDañoFisicoVehiculo : MonoBehaviour
{
    // ── Parámetros de colisión ────────────────────────────────────────────────
    [Header("Colisión → Daño")]
    [Tooltip("Velocidad relativa mínima del impacto (m/s) para causar daño. Evita raspados triviales.")]
    [SerializeField] float velocidadMin    = 2.5f;
    [Tooltip("Daño = impulso_N × escala. Ajustar por tipo de vehículo (coche ligero ≈ 0.10, furgoneta ≈ 0.06).")]
    [SerializeField] float escalaDaño      = 0.10f;
    [Tooltip("Tiempo mínimo entre golpes (s). Evita spam de daño en colisiones de rozamiento.")]
    [SerializeField] float cooldown        = 0.35f;

    [Header("Flash de impacto")]
    [Tooltip("Duración del destello naranja en los renderers al recibir un impacto fuerte (s).")]
    [SerializeField] float duracionFlash   = 0.08f;

    // ── Estado interno ────────────────────────────────────────────────────────
    VehiculoBase   _vehiculo;
    Renderer[]     _renderers;
    MaterialPropertyBlock _mpb;
    Light[]        _faros;
    ParticleSystem _humo;
    ParticleSystem _fuego;

    float _cooldown;
    float _flashTimer;

    enum Estado { Intacto, Golpeado, Grave, Llamas }
    Estado _estado = Estado.Intacto;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        _vehiculo  = GetComponent<VehiculoBase>();
        _renderers = GetComponentsInChildren<Renderer>(true);
        _mpb       = new MaterialPropertyBlock();
        _faros     = GetComponentsInChildren<Light>(true);
    }

    void Update()
    {
        if (_cooldown > 0f) _cooldown -= Time.deltaTime;

        if (_flashTimer > 0f)
        {
            _flashTimer -= Time.deltaTime;
            if (_flashTimer <= 0f) ApagarFlash();
        }
    }

    // ── Detección de colisión ─────────────────────────────────────────────────

    void OnCollisionEnter(Collision col)
    {
        if (_cooldown > 0f) return;
        if (_vehiculo == null || _vehiculo.EstaMuerto) return;

        float velRel = col.relativeVelocity.magnitude;
        if (velRel < velocidadMin) return;

        float impulso = col.impulse.magnitude;
        int daño = Mathf.Max(1, Mathf.RoundToInt(impulso * escalaDaño));

        Vector3 origen = col.contactCount > 0
            ? col.GetContact(0).point
            : transform.position;

        _vehiculo.RecibirDano(daño, origen, TipoDano.Impacto);
        _cooldown = cooldown;

        if (velRel > 6f) ActivarFlash();

        ActualizarEstado();
    }

    // ── Estado visual por HP% ─────────────────────────────────────────────────

    void ActualizarEstado()
    {
        if (_vehiculo == null) return;
        float hp = _vehiculo.VidaMax > 0
            ? (float)_vehiculo.Vida / _vehiculo.VidaMax
            : 1f;

        Estado nuevo;
        if      (hp > 0.70f) nuevo = Estado.Intacto;
        else if (hp > 0.45f) nuevo = Estado.Golpeado;
        else if (hp > 0.20f) nuevo = Estado.Grave;
        else                 nuevo = Estado.Llamas;

        if (nuevo == _estado) return;
        _estado = nuevo;

        AplicarEstadoVisual();
    }

    void AplicarEstadoVisual()
    {
        // ── Tinte de carrocería (MPB, sin crear instancias de Material) ────────
        // Cada estado oscurece más la carrocería hacia negro quemado.
        Color addTint = _estado switch
        {
            Estado.Golpeado => new Color(0.04f, 0.03f, 0.02f, 0f),
            Estado.Grave    => new Color(0.10f, 0.08f, 0.06f, 0f),
            Estado.Llamas   => new Color(0.16f, 0.10f, 0.06f, 0f),
            _               => Color.clear
        };

        foreach (var r in _renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            Color baseActual = _mpb.GetColor("_BaseColor");
            Color nuevoBase  = Color.Lerp(baseActual, Color.black + addTint,
                                          _estado == Estado.Intacto ? 0f : 0.35f);
            _mpb.SetColor("_BaseColor", nuevoBase);
            _mpb.SetColor("_Color",     nuevoBase);
            r.SetPropertyBlock(_mpb);
        }

        // ── Humo (>= Grave) ────────────────────────────────────────────────────
        if (_estado >= Estado.Grave && _humo == null)
            _humo = CrearParticulaEstado(new Color(0.25f, 0.25f, 0.25f, 0.7f),
                                         size: 1.5f, rate: 10f, offset: Vector3.up * 0.9f);
        if (_estado < Estado.Grave && _humo != null)
        { Destroy(_humo.gameObject); _humo = null; }

        // ── Fuego (== Llamas) ──────────────────────────────────────────────────
        if (_estado == Estado.Llamas && _fuego == null)
            _fuego = CrearParticulaEstado(new Color(1f, 0.35f, 0f, 0.9f),
                                          size: 2f, rate: 18f, offset: Vector3.up * 0.6f);
        if (_estado != Estado.Llamas && _fuego != null)
        { Destroy(_fuego.gameObject); _fuego = null; }

        // ── Faros (se apagan al 20% HP) ───────────────────────────────────────
        bool farosActivos = _estado < Estado.Llamas;
        foreach (var l in _faros)
            if (l != null) l.enabled = farosActivos;
    }

    // ── Flash de impacto (emissive spike en HDRP) ─────────────────────────────

    void ActivarFlash()
    {
        _flashTimer = duracionFlash;
        var flash = new Color(1f, 0.75f, 0.30f) * 4f; // HDR naranja
        foreach (var r in _renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_EmissiveColor", flash);
            r.SetPropertyBlock(_mpb);
        }
    }

    void ApagarFlash()
    {
        foreach (var r in _renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_EmissiveColor", Color.black);
            r.SetPropertyBlock(_mpb);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    ParticleSystem CrearParticulaEstado(Color color, float size, float rate, Vector3 offset)
    {
        var go   = new GameObject("_DañoVFX");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = offset;
        var ps   = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startColor    = color;
        main.startSize     = size;
        main.startLifetime = 1.8f;
        main.startSpeed    = 1.2f;
        main.loop          = true;
        var em = ps.emission; em.rateOverTime = rate;
        var sh = ps.shape;   sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.15f;
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.y = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
        ps.Play();
        return ps;
    }
}
