// SistemasSimulacion.cs — Simulación AAA+
// NOTA: las clases *Legacy (Trafico/Fauna/Multitud) fueron eliminadas — sustituidas
// por SistemaTrafico.cs, SistemaFauna.cs y SistemaSpawnCiviles.cs.
// SistemaVegetacion · SistemaAtmosfera · SistemaParanoia siguen activos.

using UnityEngine;
using System.Collections;
using System.Collections.Generic;


// ─────────────────────────────────────────────────────────────────────────────
//  SISTEMA VEGETACIÓN — wrapper para PosicionadorPrecisionUrbana + GreenForest
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaVegetacion : MonoBehaviour
{
    [Header("Configuración fallback (si no hay LIDAR)")]
    public int   densidadArboles = 2000;
    public float radioGeneracion = 600f;

    void Start() => AlsasuaLogger.Info("Vegetacion",
        PosicionadorPrecisionUrbana.Instance != null
            ? "PosicionadorPrecisionUrbana gestiona la vegetación LIDAR"
            : $"Stub: {densidadArboles} árboles en radio {radioGeneracion}m");
}

// ─────────────────────────────────────────────────────────────────────────────
//  SISTEMA ATMÓSFERA — sol astronómico + audio ambiente dinámico
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaAtmosfera : MonoBehaviour
{
    [Header("Tiempo")]
    [Range(0f, 24f)] public float horaDelDia    = 10f;
    public float velocidadDia = 1f;

    [Header("Referencia solar")]
    public Light solDireccional;

    [Header("Audio ambiente (auto desde ConfiguradorAssetsAAA)")]
    public AudioClip audioAmbiente;
    public AudioClip audioAmbienteLluvia;
    public AudioClip audioAmbienteTormenta;

    AudioSource _src;
    float _elevacionSolar;
    bool  _eraDeDia = true;

    public float HoraDelDia     => horaDelDia;
    public float ElevacionSolar => _elevacionSolar;
    public bool  EsDeDia        => _elevacionSolar > 0f;
    public event System.Action<bool> OnCambioDia;

    void Start()
    {
        if (solDireccional == null)
            solDireccional = FindFirstObjectByType<Light>();

        // Auto-asignar audio desde ConfiguradorAssetsAAA
        var cfg = ConfiguradorAssetsAAA.Instance;
        if (cfg != null)
        {
            if (audioAmbiente         == null) audioAmbiente         = cfg.ambientePajaros ?? cfg.ambienteExterior;
            if (audioAmbienteLluvia   == null) audioAmbienteLluvia   = cfg.ambienteNocheRain;
            if (audioAmbienteTormenta == null) audioAmbienteTormenta = cfg.ambienteTormenta;
        }

        // Iniciar audio ambiente
        if (audioAmbiente != null)
        {
            _src = gameObject.AddComponent<AudioSource>();
            _src.clip   = audioAmbiente;
            _src.loop   = true;
            _src.volume = 0.3f;
            _src.spatialBlend = 0f;  // 2D
            _src.Play();
        }

        AlsasuaLogger.Info("Atmosfera",
            $"Iniciado: hora={horaDelDia:F1}h, audio={audioAmbiente?.name ?? "ninguno"}");
    }

    void Update()
    {
        horaDelDia = (horaDelDia + Time.deltaTime * velocidadDia / 3600f) % 24f;
        ActualizarSol();
    }

    void ActualizarSol()
    {
        float horaRad   = (horaDelDia - 6f) / 12f * Mathf.PI;
        _elevacionSolar = Mathf.Sin(horaRad) * 70f;

        if (solDireccional != null)
        {
            solDireccional.transform.eulerAngles =
                new Vector3(_elevacionSolar, 30f, 0f);
            solDireccional.intensity =
                Mathf.Max(0f, _elevacionSolar / 70f) * 80000f;

            float t = Mathf.Clamp01(_elevacionSolar / 20f);
            solDireccional.color = Color.Lerp(
                new Color(1f, 0.4f, 0.1f),
                new Color(1f, 0.95f, 0.82f), t);
        }

        bool esDeDia = _elevacionSolar > 0f;
        if (esDeDia != _eraDeDia)
        {
            _eraDeDia = esDeDia;
            OnCambioDia?.Invoke(esDeDia);
        }
    }

    /// Cambiar el clip de ambiente según el clima.
    public void SetClimaAudio(bool lluvia, bool tormenta)
    {
        if (_src == null) return;
        var cfg   = ConfiguradorAssetsAAA.Instance;
        var clip  = cfg != null
            ? cfg.GetAmbienteClima(lluvia, tormenta)
            : (tormenta ? audioAmbienteTormenta : lluvia ? audioAmbienteLluvia : audioAmbiente);
        if (clip == null || _src.clip == clip) return;
        _src.clip   = clip;
        _src.volume = tormenta ? 0.6f : lluvia ? 0.45f : 0.3f;
        _src.Play();
    }
}


// ─────────────────────────────────────────────────────────────────────────────
//  SISTEMA PARANOIA — nivel de paranoia global independiente
// ─────────────────────────────────────────────────────────────────────────────
public class SistemaParanoia : MonoBehaviour
{
    public static SistemaParanoia Instance { get; private set; }
    [Range(0f, 100f)] public float paranoia = 0f;

    void Awake() { if (Instance && Instance != this) { Destroy(this); return; } Instance = this; }

    public void SumarParanoia(float v) => paranoia = Mathf.Clamp(paranoia + v, 0f, 100f);
    public void RestarParanoia(float v) => paranoia = Mathf.Clamp(paranoia - v, 0f, 100f);
}
