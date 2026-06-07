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
    public float decayApoyo    = 0.5f;
    public float decayParanoia = 2.0f;

    public static event System.Action<float> OnApoyoCambia;
    public static event System.Action<float> OnParanoiaCambia;
    public static event System.Action        OnParanoiaCritica;

    bool _criticaActiva;   // FIX: edge-trigger para no disparar OnParanoiaCritica cada frame

    void Update()
    {
        float dt = Time.deltaTime / 60f;
        apoyo    = Mathf.Clamp(apoyo    - decayApoyo    * dt, 0f, 100f);
        paranoia = Mathf.Clamp(paranoia - decayParanoia * dt, 0f, 100f);
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
    }

    public void SumarParanoia(float cantidad)
    {
        paranoia = Mathf.Clamp(paranoia + cantidad, 0f, 100f);
        OnParanoiaCambia?.Invoke(paranoia);
    }

    public void RestarParanoia(float cantidad) =>
        paranoia = Mathf.Clamp(paranoia - cantidad, 0f, 100f);

    public void SumarHonor(float cantidad) =>
        honor = Mathf.Clamp(honor + cantidad, 0f, 100f);

    public void ActualizarSegunNivel(int nivelWanted)
    {
        if (nivelWanted >= 3) SumarParanoia(nivelWanted * 2f);
        if (nivelWanted == 0) RestarParanoia(5f);
    }

    public bool EsGCDisfrazado(UnityEngine.GameObject npc) =>
        paranoia > umbralParanoia && Random.value < (paranoia - umbralParanoia) / 30f;
}
