// Assets/Scripts/Systems/ProxyInterior.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PROXY DE INTERIOR — gobernador VISTA + PRESUPUESTO (capa SYSTEMS)
//
//  Lo que tus GeneradorInterioresAAA/Simples (por RADIO) NO hacen: activar el
//  "falso interior" parallax SOLO en las ventanas que el jugador MIRA, y con un
//  TOPE DURO de ventanas activas a la vez (equilibrio densidad ↔ GPU).
//
//  Por frame (amortizado):
//   1. escanea un lote del registro; puntúa por (en frustum · de frente · cerca)
//      y compacta ventanas destruidas (chunk descargado → cristal == null)
//   2. ordena el lote por score
//   3. a las mejores (hasta 'presupuestoParallax') les pone el material
//      Altsasu/InteriorMapping; al resto lo revierte al cristal liso
//   4. _Seed (variación) y _EmissionNight (de _GlobalNightLevel) por MPB →
//      no se instancia material por ventana; todas comparten uno → batcheable
//
//  Tier 2 (muebles reales) lo siguen sirviendo tus generadores por radio: el
//  parallax es la capa media barata que cubre la franja "visible pero lejos".
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-50)]
public sealed class ProxyInterior : MonoBehaviour
{
    [Header("Presupuesto / vista")]
    [Tooltip("Máximo de ventanas con parallax activo a la vez (tope GPU).")]
    [SerializeField] int   presupuestoParallax = 120;
    [Tooltip("Distancia máxima (m) a la que se considera activar parallax.")]
    [SerializeField] float maxDist = 80f;
    [Tooltip("Coseno del semiángulo del cono de visión (0.5 ≈ 120°).")]
    [SerializeField, Range(0f, 1f)] float cosFov = 0.5f;
    [Tooltip("Ventanas evaluadas por frame (amortización; el resto conserva su tier).")]
    [SerializeField] int   ventanasPorFrame = 128;

    Material _matParallax;
    MaterialPropertyBlock _mpb;
    Camera _cam;
    int    _cursor;
    int    _activos;                       // ventanas actualmente en tier 1
    readonly List<VentanaProxy> _cand = new(256);

    static readonly int ID_Seed     = Shader.PropertyToID("_Seed");
    static readonly int ID_EmiNight = Shader.PropertyToID("_EmissionNight");
    const string GLOBAL_NIGHT = "_GlobalNightLevel";   // lo publica SistemaVolumenHDRP

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<ProxyInterior>() != null) return;
        var go = new GameObject("ProxyInterior");
        DontDestroyOnLoad(go);
        go.AddComponent<ProxyInterior>();
    }

    void Awake()
    {
        var sh = Shader.Find("Altsasu/InteriorMapping");
        if (sh == null)
        {
            AlsasuaLogger.Warn("ProxyInterior", "Shader 'Altsasu/InteriorMapping' no encontrado — Proxy desactivado.");
            enabled = false;
            return;
        }
        _matParallax = new Material(sh) { name = "ProxyInterior_Parallax (runtime)" };

        // Reusa los cubemaps de interior ya existentes (Resources/Interiores/*.asset,
        // generados por IntegradorAssetsInteriores). 'residencial' como genérico; si falta,
        // el primero disponible; si no hay ninguno, el shader usa su habitación blanca.
        var cube = Resources.Load<Cubemap>("Interiores/residencial");
        if (cube == null) { var todos = Resources.LoadAll<Cubemap>("Interiores"); if (todos.Length > 0) cube = todos[0]; }
        if (cube != null) _matParallax.SetTexture("_InteriorCube", cube);
        else AlsasuaLogger.Info("ProxyInterior", "Sin cubemap en Resources/Interiores — habitación blanca por defecto.");

        _mpb = new MaterialPropertyBlock();

        // El Proxy es la ÚNICA autoridad del parallax: cede los sistemas por radio para
        // evitar doble interior (mismo shader, dos mecanismos). Como esto corre en Awake
        // ANTES del primer frame, sus Start() ni siquiera llegan a spawnear.
        var aaa = FindAnyObjectByType<GeneradorInterioresAAA>();
        if (aaa != null) { aaa.enabled = false; AlsasuaLogger.Info("ProxyInterior", "GeneradorInterioresAAA cedido: el Proxy gobierna el parallax (vista+presupuesto)."); }
        var simple = FindAnyObjectByType<GeneradorInterioresSimples>();
        if (simple != null) simple.enabled = false;
    }

    void OnDestroy()
    {
        if (_matParallax != null) Destroy(_matParallax);
    }

    void Update()
    {
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }

        var lista = ProxyInteriorRegistro.Ventanas;
        if (lista.Count == 0) return;

        float night = Shader.GetGlobalFloat(GLOBAL_NIGHT);
        Vector3 camPos = _cam.transform.position;
        Vector3 camFwd = _cam.transform.forward;

        // 1) Escaneo amortizado + compactación de ventanas muertas (chunk descargado)
        _cand.Clear();
        int pasos = Mathf.Min(ventanasPorFrame, lista.Count);
        for (int s = 0; s < pasos; s++)
        {
            if (_cursor >= lista.Count) _cursor = 0;
            var v = lista[_cursor];

            if (v.cristal == null)              // destruida → swap-remove O(1), no avanzar cursor
            {
                if (v.tier == 1) _activos = Mathf.Max(0, _activos - 1);
                ProxyInteriorRegistro.ParallaxActivas.Remove(v.cristal);
                int last = lista.Count - 1;
                lista[_cursor] = lista[last];
                lista.RemoveAt(last);
                continue;
            }

            v.score = Score(v, camPos, camFwd);
            _cand.Add(v);
            _cursor++;
        }

        // 2) Ordenar el lote por score (descendente)
        _cand.Sort((a, b) => b.score.CompareTo(a.score));

        // 3) Asignar tier respetando el presupuesto
        for (int i = 0; i < _cand.Count; i++)
        {
            var v = _cand[i];
            bool visible = v.score > 0f;

            if (visible && (v.tier == 1 || _activos < presupuestoParallax))
                ActivarParallax(v, night);
            else if (v.tier == 1 && !visible)   // fuera de vista/rango → liberar presupuesto
                DesactivarParallax(v);
        }
    }

    float Score(VentanaProxy v, Vector3 camPos, Vector3 camFwd)
    {
        Vector3 aCam = camPos - v.centro;
        float d2 = aCam.sqrMagnitude;
        if (d2 > maxDist * maxDist) return 0f;
        if (Vector3.Dot(v.normal, aCam) <= 0f) return 0f;     // se mira el dorso de la fachada
        float d = Mathf.Sqrt(d2);
        Vector3 dir = -aCam / d;                               // de la cámara hacia la ventana
        float centralidad = Vector3.Dot(camFwd, dir);
        if (centralidad < cosFov) return 0f;                   // fuera del cono de visión
        return centralidad * centralidad / Mathf.Max(d2, 1f);  // más centrado y cerca = más
    }

    void ActivarParallax(VentanaProxy v, float night)
    {
        if (v.tier != 1)
        {
            if (v.matOriginal == null) v.matOriginal = v.cristal.sharedMaterial;
            v.cristal.sharedMaterial = _matParallax;           // compartido → batcheable
            v.tier = 1;
            _activos++;
            ProxyInteriorRegistro.ParallaxActivas.Add(v.cristal);   // el bucle nocturno lo respetará
        }
        // variación + emisión nocturna por instancia, sin instanciar material:
        v.cristal.GetPropertyBlock(_mpb);
        _mpb.SetFloat(ID_Seed, SeedDe(v.centro));
        _mpb.SetFloat(ID_EmiNight, night);
        v.cristal.SetPropertyBlock(_mpb);
    }

    void DesactivarParallax(VentanaProxy v)
    {
        if (v.tier != 1) return;
        if (v.matOriginal != null) v.cristal.sharedMaterial = v.matOriginal;
        v.cristal.SetPropertyBlock(null);                      // limpia el MPB
        ProxyInteriorRegistro.ParallaxActivas.Remove(v.cristal);
        v.tier = 0;
        _activos = Mathf.Max(0, _activos - 1);
    }

    // Semilla determinista por posición → cada ventana varía pero es estable entre frames.
    static float SeedDe(Vector3 p)
    {
        int h = (Mathf.RoundToInt(p.x * 7.1f) * 73856093) ^ (Mathf.RoundToInt(p.z * 7.1f) * 19349663);
        return ((h & 0x7fffffff) % 1000) / 1000f;
    }
}
