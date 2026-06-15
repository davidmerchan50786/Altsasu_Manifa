// Assets/Scripts/SistemaOptimizacion.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE OPTIMIZACIÓN — mantiene 60fps con 1030 edificios en pantalla
//
//  Estrategias:
//    1. Dynamic Quality: baja calidad automáticamente si FPS < umbral
//    2. Spawn throttling: limita generación por frame (ya en los sistemas)
//    3. Occlusion manual: desactiva edificios que el jugador no puede ver
//    4. Pool de instancias: reutiliza GameObjects en vez de Destroy/Instantiate
//    5. Batch combine: combina meshes de calles y aceras
//    6. Shadow distance: reduce distancia de sombras dinámicamente
//    7. LOD bias: ajusta bias global según FPS
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[DefaultExecutionOrder(-95)]
public class SistemaOptimizacion : SingletonMono<SistemaOptimizacion>
{

    [Header("Objetivos")]
    [Range(30, 144)] public int fpsMeta      = 60;
    [Range(10, 60)]  public int fpsMinimo    = 45;

    [Header("Calidad dinámica")]
    public bool  ajusteAutomatico   = true;
    public float intervaloMedicion  = 2f;

    // ── FPS measurement ───────────────────────────────────────────────────
    float   _fpsAcum;
    int     _frameCount;
    float   _timerMedicion;
    float   _fpsActual = 60f;
    int     _nivelCalidad; // 0=ultra, 1=alto, 2=medio, 3=bajo

    // ── Tier de calidad dinámico (AAA+++) ──────────────────────────────────
    // Señal global 0..3 derivada de la carga real, publicada como _GlobalQualityTier
    // para que el resto del pipeline (partículas, decals, features pesadas de render)
    // module su coste sin acoplarse a este sistema. 0 = Ultra … 3 = Performance.
    int _tierCalidad;
    static readonly int ID_QualityTier = Shader.PropertyToID("_GlobalQualityTier");

    /// <summary>Tier de calidad actual: 0 = Ultra, 1 = Alto, 2 = Medio, 3 = Performance.</summary>
    public static int TierCalidad => Instance?._tierCalidad ?? 0;

    // ── Estado ────────────────────────────────────────────────────────────
    float _shadowDistOrig;
    float _lodBiasOrig;
    float _fogDensityOrig;
    bool  _baselineRestaurado;   // ¿ya restauramos la calidad tras la carga?

    // OPT: cachear el Transform padre de edificios — GameObject.Find es O(n) escena completa.
    // BucleMedicion corre cada 0.5s pero hay pilas de SetActive de celdas intermedias.
    // Ahorro estimado: evita 2 Find() cada 0.5s = 4 traversals/s del hierarchy.
    Transform _edifParentCached;
    // OPT: buffer de hijos reutilizable — evita new List<Transform>() cada 0.5s
    // Ahorro: 1 GC alloc cada 2s (pequeño pero acumulado con 1030 edificios = heap fragmentation).
    readonly List<Transform> _childrenBuffer = new(1100);

    // ── Pool de efectos ───────────────────────────────────────────────────
    readonly Queue<ParticleSystem> _poolParticulas = new();
    const int POOL_SIZE = 20;

    protected override void OnAwake() { GuardarConfigOriginal(); PrecargarPool(); }

    void Start()
    {
        Application.targetFrameRate = fpsMeta;
        QualitySettings.vSyncCount  = 0; // vsync off — controlamos nosotros
        // Reacción más ágil: medir al menos 1×/s (un 0 FPS no puede esperar 2 s/escalón).
        intervaloMedicion = Mathf.Min(intervaloMedicion, 1f);
        _tierCalidad = 0;
        Shader.SetGlobalFloat(ID_QualityTier, 0f); // arrancar en Ultra; sube si la carga lo exige
        StartCoroutine(BucleMedicion());
        AlsasuaLogger.Info("Optimizacion", $"Sistema activo — objetivo {fpsMeta}fps");
    }

    void GuardarConfigOriginal()
    {
        _shadowDistOrig = QualitySettings.shadowDistance;
        _lodBiasOrig    = QualitySettings.lodBias;
        _fogDensityOrig = RenderSettings.fogDensity;
        _nivelCalidad   = QualitySettings.GetQualityLevel();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MEDICIÓN Y AJUSTE DINÁMICO
    // ════════════════════════════════════════════════════════════════════════

    void Update()
    {
        // CLAVE (2026-06-15): NO medir ni degradar mientras el mundo carga. Durante la
        // generación el FPS es ínfimo por naturaleza y el degrade de emergencia
        // (DegradarAlMaximo: tier 3 + LOD 0.4 + niebla + sombras 40 m) se quedaba LATCHED
        // para siempre → el mundo se veía plano/lavado/"de dibujos animados" tras cargar.
        // Mientras carga: congelar medición. Al estar listo: RESTAURAR calidad original y,
        // solo a partir de ahí, dejar que el ajuste dinámico actúe sobre FPS reales.
        if (!ArranqueMundo.BaselineListo)
        {
            _fpsAcum = 0f; _frameCount = 0; _timerMedicion = 0f;
            return;
        }
        if (!_baselineRestaurado)
        {
            _baselineRestaurado = true;
            RestaurarCalidadOriginal();
            _fpsAcum = 0f; _frameCount = 0; _timerMedicion = 0f;
            return;   // empezar a medir desde el siguiente frame, ya con calidad buena
        }

        _fpsAcum    += 1f / Time.unscaledDeltaTime;
        _frameCount++;
        _timerMedicion += Time.unscaledDeltaTime;

        if (_timerMedicion >= intervaloMedicion)
        {
            _fpsActual     = _fpsAcum / _frameCount;
            _fpsAcum       = 0f;
            _frameCount    = 0;
            _timerMedicion = 0f;

            if (ajusteAutomatico) AjustarCalidad(_fpsActual);
        }
    }

    void AjustarCalidad(float fps)
    {
        // PRIORIDAD VISUAL: el usuario quiere el mejor aspecto, así que la red de
        // seguridad solo actúa si el FPS es CATASTRÓFICO (<12), y de forma suave.
        // Por encima de eso mantenemos la calidad alta aunque el FPS no sea ideal.
        if (fps < 12f)       DegradarAlMaximo();
        else if (fps < 20f)  SubirNivelCalidad();   // bajo → recortar un poco
        else if (fps > 45f)  BajarNivelCalidad();    // holgado → volver a subir calidad
    }

    // Restaura la calidad capturada al arranque (la "buena"): deshace cualquier degrade
    // de emergencia disparado durante la carga. Se llama UNA vez al estar listo el baseline.
    void RestaurarCalidadOriginal()
    {
        // PRIORIDAD VISUAL: forzar el nivel de calidad MÁXIMO (High Fidelity = índice 0)
        // y un LOD bias generoso. El usuario quiere el mejor aspecto posible; el degrade
        // dinámico solo actúa ya como red de seguridad suave (ver AjustarCalidad).
        if (QualitySettings.GetQualityLevel() != 0)
            QualitySettings.SetQualityLevel(0, true);   // 0 = High Fidelity (mejor)
        QualitySettings.shadowDistance = Mathf.Max(_shadowDistOrig, 300f);
        QualitySettings.lodBias        = Mathf.Max(_lodBiasOrig, 2f);   // detalle lejano (edificios no se "acajonan")
        RenderSettings.fogDensity      = _fogDensityOrig;                // quita la niebla de emergencia
        FijarTier(0);
        AlsasuaLogger.Info("Optimizacion",
            $"Baseline listo → calidad MÁXIMA (High Fidelity, sombras {QualitySettings.shadowDistance:F0} m, LOD {QualitySettings.lodBias:F2}, tier 0).");
    }

    // Degrade inmediato al suelo (no escalonado): para FPS crítico, recuperar control YA.
    void DegradarAlMaximo()
    {
        // PRIORIDAD VISUAL: incluso en degrade catastrófico evitamos lo que se ve "como
        // dibujos animados": NO inyectamos niebla (lavaba la imagen) y mantenemos un LOD
        // bias decente (los edificios no se vuelven cajas). Solo recortamos sombras.
        QualitySettings.shadowDistance = 120f;
        QualitySettings.lodBias        = Mathf.Max(1f, QualitySettings.lodBias - 0.5f);
        FijarTier(2);
        AlsasuaLogger.Warn("Optimizacion",
            $"FPS CRÍTICO ({_fpsActual:F0}) → degrade SUAVE (tier 2, sombras 120 m, LOD {QualitySettings.lodBias:F2}; sin niebla).");
    }

    void SubirNivelCalidad() // calidad más baja = mejor rendimiento
    {
        // Sombras (suelo 120 m, no 50 — evita el corte feo de sombras cercano)
        QualitySettings.shadowDistance = Mathf.Max(120f, QualitySettings.shadowDistance - 40f);
        // LOD: suelo 1.2 (los edificios mantienen geometría, no se "acajonan")
        QualitySettings.lodBias = Mathf.Max(1.2f, QualitySettings.lodBias - 0.2f);
        // (sin inyección de niebla — lavaba la imagen)

        // Subir el tier (peor calidad) y difundirlo al pipeline.
        FijarTier(_tierCalidad + 1);

        AlsasuaLogger.Info("Optimizacion",
            $"FPS bajo ({_fpsActual:F0}) → tier={_tierCalidad} shadow={QualitySettings.shadowDistance:F0}m LOD={QualitySettings.lodBias:F2}");
    }

    void BajarNivelCalidad() // calidad más alta = mejor visual
    {
        QualitySettings.shadowDistance = Mathf.Min(_shadowDistOrig, QualitySettings.shadowDistance + 25f);
        QualitySettings.lodBias        = Mathf.Min(_lodBiasOrig,    QualitySettings.lodBias + 0.1f);

        // Bajar el tier (mejor calidad) y difundirlo.
        FijarTier(_tierCalidad - 1);
    }

    // Fija el tier 0..3 y lo publica como global de shader solo si cambió (evita writes redundantes).
    void FijarTier(int nuevo)
    {
        nuevo = Mathf.Clamp(nuevo, 0, 3);
        if (nuevo == _tierCalidad) return;
        _tierCalidad = nuevo;
        Shader.SetGlobalFloat(ID_QualityTier, _tierCalidad);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  OCLUSIÓN MANUAL POR DISTANCIA
    // ════════════════════════════════════════════════════════════════════════

    // Cuando existe el StreamerMundoEstatico, ÉL es el dueño de la activación por
    // distancia (radio gobernado por GPU + anillo impostor). Cedemos nuestro cull
    // todo-o-nada para no pelearnos por el SetActive de los mismos edificios. El
    // gobernador de calidad por FPS (sombras/LOD bias en Update) sigue activo: es un
    // mando complementario, no el mismo.
    StreamerMundoEstatico _streamer;
    bool _cedidoAlStreamer;

    IEnumerator BucleMedicion()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.5f);

            if (!_cedidoAlStreamer)
            {
                if (_streamer == null) _streamer = FindFirstObjectByType<StreamerMundoEstatico>();
                if (_streamer != null)
                {
                    _cedidoAlStreamer = true;
                    AlsasuaLogger.Info("Optimizacion",
                        "StreamerMundoEstatico presente → cedo el cull de edificios (radio gobernado por GPU).");
                }
            }
            if (_cedidoAlStreamer) continue;   // el streamer manda la activación por distancia

            var jugador = AltsasuCore.Jugador;
            if (jugador == null) continue;

            // Desactivar padres de zonas lejanas al jugador
            yield return StartCoroutine(CullZonasLejanas(jugador.position));
        }
    }

    IEnumerator CullZonasLejanas(Vector3 posJugador)
    {
        const float DIST_LOD0 = 150f;  // full quality
        const float DIST_LOD1 = 300f;  // medium
        const float DIST_LOD2 = 600f;  // low / culled > 600m

        // OPT: cachear resultado de GameObject.Find — se ejecuta 2 veces/s, cada Find
        // recorre todo el hierarchy (O(n) objetos en escena con 1030 edificios).
        // Solo rebuscamos si la referencia se volvió null (ej. recarga de zona).
        if (_edifParentCached == null)
        {
            var go = GameObject.Find("Edificios_Precisos") ?? GameObject.Find("Edificios_OSM");
            if (go == null) yield break;
            _edifParentCached = go.transform;
        }
        var edifParent = _edifParentCached.gameObject;
        if (edifParent == null) { _edifParentCached = null; yield break; }

        // Recoger posiciones en NativeArray para Burst
        // OPT: reutilizar _childrenBuffer — evita 1 alloc cada 0.5s (= 2 GC allocs/s eliminados)
        var children = _childrenBuffer;
        children.Clear();
        foreach (Transform t in edifParent.transform)
            if (t != null) children.Add(t);

        if (children.Count == 0) yield break;

        var posArr    = new NativeArray<float3>(children.Count, Allocator.TempJob);
        var niveles   = new NativeArray<byte>(children.Count,   Allocator.TempJob);
        var pj        = new float3(posJugador.x, posJugador.y, posJugador.z);

        for (int i = 0; i < children.Count; i++)
        {
            var p = children[i].position;
            posArr[i] = new float3(p.x, p.y, p.z);
        }

        // Burst calcula todos los niveles LOD en paralelo
        var job = new JobCalcularNivelesLOD
        {
            posicionesObjetos = posArr,
            posJugador        = pj,
            dist0             = DIST_LOD0,
            dist1             = DIST_LOD1,
            dist2             = DIST_LOD2,
            nivelLOD          = niveles,
        };
        job.Schedule(children.Count, 64).Complete();

        // Aplicar resultados en el hilo principal por lotes
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] == null) continue;
            bool activo = niveles[i] < 3;
            if (children[i].gameObject.activeSelf != activo)
                children[i].gameObject.SetActive(activo);
            if (i % 100 == 0) yield return null;
        }

        posArr.Dispose();
        niveles.Dispose();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  POOL DE PARTÍCULAS
    // ════════════════════════════════════════════════════════════════════════

    void PrecargarPool()
    {
        for (int i = 0; i < POOL_SIZE; i++)
        {
            var go = new GameObject($"PoolPS_{i}");
            go.transform.SetParent(transform);
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();
            _poolParticulas.Enqueue(ps);
        }
    }

    public static ParticleSystem ObtenerParticula()
    {
        if (Instance == null) return null;
        if (Instance._poolParticulas.Count == 0) return null;
        var ps = Instance._poolParticulas.Dequeue();
        ps.gameObject.SetActive(true);
        return ps;
    }

    public static void DevolverParticula(ParticleSystem ps)
    {
        if (Instance == null || ps == null) return;
        ps.Stop(); ps.Clear();
        ps.gameObject.SetActive(false);
        Instance._poolParticulas.Enqueue(ps);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ════════════════════════════════════════════════════════════════════════

    public static float FPSActual => Instance?._fpsActual ?? 60f;
    public static bool  EsLento   => Instance != null && Instance._fpsActual < Instance.fpsMinimo;

    /// <summary>Combina los meshes de calles estáticos para reducir draw calls.</summary>
    public static void CombinarMeshesEstaticos(Transform parent)
    {
        if (parent == null) return;
        var filters  = parent.GetComponentsInChildren<MeshFilter>();
        var combines = new List<CombineInstance>();
        Material mat = null;

        foreach (var mf in filters)
        {
            if (mf.sharedMesh == null) continue;
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null) continue;
            if (mat == null) mat = mr.sharedMaterial;
            combines.Add(new CombineInstance { mesh = mf.sharedMesh, transform = mf.transform.localToWorldMatrix });
            mf.gameObject.SetActive(false);
        }

        if (combines.Count == 0) return;

        var combined = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        combined.CombineMeshes(combines.ToArray());

        var go = new GameObject($"{parent.name}_Combined");
        go.transform.SetParent(parent);
        go.isStatic = true;
        go.AddComponent<MeshFilter>().sharedMesh = combined;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;

        AlsasuaLogger.Info("Optimizacion",
            $"Meshes combinados: {combines.Count} → 1 draw call ({parent.name})");
    }

    void OnGUI()
    {
        if (!Debug.isDebugBuild && !Application.isEditor) return;
        // Contador FPS en esquina superior izquierda (solo en debug)
        Color col = _fpsActual >= fpsMeta    ? Color.green
                  : _fpsActual >= fpsMinimo  ? Color.yellow
                  : Color.red;
        GUI.color = col;
        GUI.Label(new Rect(8, 8, 120, 28),
            $"FPS: {_fpsActual:F0} | {QualitySettings.shadowDistance:F0}m shdw");
        GUI.color = Color.white;
    }
}
