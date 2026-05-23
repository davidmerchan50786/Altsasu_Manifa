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

    // ── Estado ────────────────────────────────────────────────────────────
    float _shadowDistOrig;
    float _lodBiasOrig;

    // ── Pool de efectos ───────────────────────────────────────────────────
    readonly Queue<ParticleSystem> _poolParticulas = new();
    const int POOL_SIZE = 20;

    protected override void OnAwake() { GuardarConfigOriginal(); PrecargarPool(); }

    void Start()
    {
        Application.targetFrameRate = fpsMeta;
        QualitySettings.vSyncCount  = 0; // vsync off — controlamos nosotros
        StartCoroutine(BucleMedicion());
        AlsasuaLogger.Info("Optimizacion", $"Sistema activo — objetivo {fpsMeta}fps");
    }

    void GuardarConfigOriginal()
    {
        _shadowDistOrig = QualitySettings.shadowDistance;
        _lodBiasOrig    = QualitySettings.lodBias;
        _nivelCalidad   = QualitySettings.GetQualityLevel();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MEDICIÓN Y AJUSTE DINÁMICO
    // ════════════════════════════════════════════════════════════════════════

    void Update()
    {
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
        if (fps < fpsMinimo * 0.75f)       SubirNivelCalidad();   // muy bajo → bajar calidad
        else if (fps > fpsMeta * 0.95f)    BajarNivelCalidad();   // excelente → subir calidad
    }

    void SubirNivelCalidad() // calidad más baja = mejor rendimiento
    {
        // Sombras
        QualitySettings.shadowDistance = Mathf.Max(50f, QualitySettings.shadowDistance - 50f);
        // LOD más agresivo
        QualitySettings.lodBias = Mathf.Max(0.5f, QualitySettings.lodBias - 0.25f);
        // Reducir distancia de niebla
        if (RenderSettings.fogDensity < 0.005f) RenderSettings.fogDensity += 0.0005f;

        AlsasuaLogger.Info("Optimizacion",
            $"FPS bajo ({_fpsActual:F0}) → shadow={QualitySettings.shadowDistance:F0}m LOD={QualitySettings.lodBias:F2}");
    }

    void BajarNivelCalidad() // calidad más alta = mejor visual
    {
        QualitySettings.shadowDistance = Mathf.Min(_shadowDistOrig, QualitySettings.shadowDistance + 25f);
        QualitySettings.lodBias        = Mathf.Min(_lodBiasOrig,    QualitySettings.lodBias + 0.1f);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  OCLUSIÓN MANUAL POR DISTANCIA
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator BucleMedicion()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.5f);

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

        // Prioridad: geometría precisa > base OSM
        var edifParent = GameObject.Find("Edificios_Precisos")
                      ?? GameObject.Find("Edificios_OSM");
        if (edifParent == null) yield break;

        // Recoger posiciones en NativeArray para Burst
        var children = new List<Transform>();
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
