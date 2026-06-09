// SistemaApoyoPopular.cs — Apoyo popular, paranoia y honor del movimiento
using UnityEngine;

public class SistemaApoyoPopular : SingletonMono<SistemaApoyoPopular>
{
    protected override bool DestroyGameObjectOnDuplicate => true;

    [Range(0,100)] public float apoyo    = 50f;
    [Range(0,100)] public float honor    = 50f;
    [Range(0,100)] public float paranoia = 0f;

    public float umbralParanoia    = 70f;
    public float umbralMaxParanoia = 90f;
    public float decayApoyo    = 0.5f;   // unidades/segundo
    public float decayParanoia = 2.0f;   // unidades/segundo
    // El honor no decae: se gana con acciones correctas y solo
    // se pierde explícitamente (RestarHonor) por acciones que dañan al movimiento.

    public static event System.Action<float> OnApoyoCambia;
    public static event System.Action<float> OnParanoiaCambia;
    public static event System.Action<float> OnHonorCambia;   // nuevo
    public static event System.Action        OnParanoiaCritica;

    bool _criticaActiva;   // edge-trigger para no disparar OnParanoiaCritica cada frame

    void Update()
    {
        // BUG FIX: era `Time.deltaTime / 60f`, lo que hacía el decay 60× más lento
        // de lo esperado (~3.3 h para bajar de 100 a 0 con decayApoyo=0.5).
        float dt = Time.deltaTime;
        apoyo    = Mathf.Clamp(apoyo    - decayApoyo    * dt, 0f, 100f);

        // El honor ralentiza la recuperación de paranoia: a 100 de honor, la paranoia
        // decae el doble de rápido porque la comunidad está alerta y organizada.
        float factorHonor = 1f + honor / 100f;
        paranoia = Mathf.Clamp(paranoia - decayParanoia * factorHonor * dt, 0f, 100f);

        bool critica = paranoia >= umbralMaxParanoia;
        if (critica && !_criticaActiva) OnParanoiaCritica?.Invoke();   // solo en flanco de subida
        _criticaActiva = critica;
    }

    public void SumarApoyo(float cantidad, string razon = "")
    {
        apoyo = Mathf.Clamp(apoyo + cantidad, 0f, 100f);
        OnApoyoCambia?.Invoke(apoyo);
        if (!string.IsNullOrEmpty(razon)) AlsasuaLogger.Info("Apoyo", $"+{cantidad} ({razon}) → {apoyo:F0}%");
    }

    public void RestarApoyo(float cantidad, string razon = "")
    {
        apoyo = Mathf.Clamp(apoyo - cantidad, 0f, 100f);
        OnApoyoCambia?.Invoke(apoyo);
        if (!string.IsNullOrEmpty(razon)) AlsasuaLogger.Info("Apoyo",