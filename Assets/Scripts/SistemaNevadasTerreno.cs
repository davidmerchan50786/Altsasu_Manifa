// Assets/Scripts/SistemaNevadasTerreno.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE NEVADAS — acumulación de nieve en terreno
//
//  El Arquitecto: falta un sistema que conecte SistemaClima.NieveLigera
//  con el splatmap del terreno. Sin esto la nieve visual solo son partículas
//  pero el suelo queda igual de marrón/verde.
//
//  Funciona en tres fases:
//    1. Acumulación: cuando nieva, aumenta la capa capaNieve (TerrainLayer)
//       solo en zonas planas (pendiente < umbralPendienteNieve).
//    2. Mantenimiento: mientras sigue nevando, mantiene la cobertura.
//    3. Fusión: al dejar de nevar, la nieve desaparece gradualmente
//       (más rápido en pendientes y zonas de sol).
//
//  Shader global: _GlobalSnowLevel (0-1) para que materiales PBR
//  de edificios y vegetación muestren nieve sobre sus superficies.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(-62)]
public class SistemaNevadasTerreno : MonoBehaviour
{
    public static SistemaNevadasTerreno Instance { get; private set; }

    [Header("Configuración")]
    [Tooltip("TerrainLayer de nieve. Si es null, se usa un tint blanco sobre capaPrado.")]
    public TerrainLayer capaNieve;
    [Tooltip("Pendiente máxima (°) en la que se acumula nieve.")]
    [Range(0f, 45f)] public float umbralPendienteNieve = 28f;
    [Tooltip("Velocidad de acumulación (0-1 por segundo).")]
    public float velocidadAcumulacion = 0.02f;
    [Tooltip("Velocidad de fusión (0-1 por segundo).")]
    public float velocidadFusion = 0.008f;
    [Tooltip("Cobertura máxima de nieve (0-1). 1.0 = todo blanco.")]
    [Range(0f, 1f)] public float coberturaMaxima = 0.75f;

    static readonly int ID_SnowLevel = Shader.PropertyToID("_GlobalSnowLevel");

    SistemaClima _clima;
    TerrainData  _td;
    Terrain      _terrain;

    float _nivelNieve;         // 0 seco … coberturaMaxima
    int   _idxNieve  = -1;     // índice del canal nieve en el alphamap
    bool  _listo;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start() => StartCoroutine(InicializarTras(5f));

    IEnumerator InicializarTras(float d)
    {
        yield return new WaitForSeconds(d);
        _clima   = FindFirstObjectByType<SistemaClima>();
        _terrain = Terrain.activeTerrain;
        if (_terrain == null) yield break;
        _td = _terrain.terrainData;

        // Buscar o añadir capa nieve
        if (capaNieve != null)
        {
            var capas = _td.terrainLayers;
            for (int i = 0; i < capas.Length; i++)
                if (capas[i] == capaNieve) { _idxNieve = i; break; }

            if (_idxNieve < 0)
            {
                // Añadir la capa al final
                var nuevas = new TerrainLayer[capas.Length + 1];
                capas.CopyTo(nuevas, 0);
                nuevas[capas.Length] = capaNieve;
                _td.terrainLayers = nuevas;
                _idxNieve = capas.Length;
                AlsasuaLogger.Info("Nieve", "Capa nieve añadida al terreno.");
            }
        }

        _listo = true;
        StartCoroutine(CicloNieve());
        AlsasuaLogger.Info("Nieve", "Sistema de nevadas listo.");
    }

    IEnumerator CicloNieve()
    {
        while (true)
        {
            yield return new WaitForSeconds(15f);
            if (!_listo || _td == null) continue;

            bool nieva = _clima != null &&
                (_clima.climaActual == SistemaClima.EstadoClima.NieveLigera);

            // Objetivo: coberturaMaxima si nieva, 0 si no
            float objetivo = nieva ? coberturaMaxima : 0f;
            float vel = nieva ? velocidadAcumulacion : velocidadFusion;

            float nuevo = Mathf.MoveTowards(_nivelNieve, objetivo, vel * 15f); // 15s de ciclo
            if (Mathf.Abs(nuevo - _nivelNieve) < 0.005f) continue;

            _nivelNieve = nuevo;
            Shader.SetGlobalFloat(ID_SnowLevel, _nivelNieve);

            if (_idxNieve >= 0)
                yield return StartCoroutine(AplicarNieveSplatmap(_nivelNieve));
        }
    }

    IEnumerator AplicarNieveSplatmap(float nivel)
    {
        int res   = _td.alphamapResolution;
        int capas = _td.alphamapLayers;
        if (capas <= _idxNieve) yield break;

        var alpha = _td.GetAlphamaps(0, 0, res, res);

        for (int ay = 0; ay < res; ay++)
        {
            for (int ax = 0; ax < res; ax++)
            {
                float nx = ax / (float)(res - 1);
                float nz = ay / (float)(res - 1);

                // Nieve solo en zonas planas
                float pendiente = _td.GetSteepness(nx, nz);
                if (pendiente > umbralPendienteNieve) continue;

                // Cantidad según nivel global y pendiente
                float factorPlano = 1f - pendiente / umbralPendienteNieve;
                float cantNieve   = nivel * factorPlano;

                // No cubrir roca ni asfalto completamente
                float cantBloq = capas > 2 ? alpha[ay, ax, 2] : 0f; // roca
                cantBloq      += capas > 5 ? alpha[ay, ax, 5] : 0f; // asfalto
                cantNieve      = Mathf.Min(cantNieve, 1f - cantBloq * 0.8f);

                // Distribuir: reducir proporcional todos los otros canales
                float pesoActualNieve = alpha[ay, ax, _idxNieve];
                float delta           = cantNieve - pesoActualNieve;
                if (Mathf.Abs(delta) < 0.01f) continue;

                // Escalar el resto
                float totalOtros = 1f - pesoActualNieve;
                if (totalOtros > 0.001f)
                {
                    float escala = (1f - cantNieve) / totalOtros;
                    for (int c = 0; c < capas; c++)
                        if (c != _idxNieve) alpha[ay, ax, c] *= escala;
                }
                alpha[ay, ax, _idxNieve] = cantNieve;
            }
            if (ay % 32 == 0) yield return null;
        }

        _td.SetAlphamaps(0, 0, alpha);
        SistemaDetalleTerreno.Instance?.InvalidarCacheAlpha();
        AlsasuaLogger.Info("Nieve", $"Splatmap nieve aplicado (nivel={_nivelNieve:F2})");
    }

    void OnDestroy() { if (Instance == this) Instance = null; }
}
