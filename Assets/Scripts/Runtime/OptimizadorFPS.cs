// Assets/Scripts/Runtime/OptimizadorFPS.cs
// ═══════════════════════════════════════════════════════════════════════════
//  OPTIMIZADOR FPS — trucos de render/física que no cubren los otros sistemas
//
//  Lo que hace cada sección:
//
//  1. LAYER CULL DISTANCES — la cámara deja de procesar objetos de capas pequeñas
//     más allá de umbrales ajustados. Un NPC a 90m no necesita draw call; una farola
//     a 60m tampoco. Ahorro tipico: 20-40% draw calls en el bloque de la ciudad.
//
//  2. NAVMESH THROTTLE — 150 NavMeshAgents a 50Hz = mucho CPU. Este sistema deja
//     activos solo los N agentes más cercanos al jugador; el resto suspende pathfinding
//     (Agent.isStopped=true) con movimiento congelado. Integra con Sim-LOD: los Ghost
//     ya no tienen agente activo.
//
//  3. PHYSICS LAYER MATRIX — los NPCs no necesitan detectar colisiones entre sí
//     (NavMesh gestiona la evasión). Desactivar NPC↔NPC en la matriz = broadphase
//     más barato, especialmente con 150+ cuerpos en la manifestación.
//
//  4. FIXED TIMESTEP 30 Hz — reducir de 50Hz (0.02) a 30Hz (0.033) para física
//     y NavMesh. Los vehículos y el jugador no notan la diferencia a 60 FPS visuales.
//
//  5. RENDERER.forceRenderingOff — apagar renderers de NPCs en banda Oculto del
//     StreamerMundo sin llamar SetActive (que destruye el NavMeshAgent). Más barato
//     que enable/disable de componente.
//
//  6. STATIC BATCHING TARDÍO — combinar mallas de los edificios horneados en runtime
//     (StaticBatchingUtility.Combine). Se llama una vez tras BaselineListo.
//
//  7. CAMERA FRUSTUM EXTRA — recortar el far clip de la cámara según GobernadorRender
//     (si el radio de activación es 800m, el far clip no necesita ser 5000m).
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DefaultExecutionOrder(-50)]   // antes que los sistemas de gameplay
public class OptimizadorFPS : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoBoot()
    {
        if (FindFirstObjectByType<OptimizadorFPS>() != null) return;
        var go = new GameObject("[OptimizadorFPS]");
        go.AddComponent<OptimizadorFPS>();
        DontDestroyOnLoad(go);
    }

    public static OptimizadorFPS Instance { get; private set; }

    // ── Config ────────────────────────────────────────────────────────────
    [Header("1 · Layer cull distances (0 = sin límite)")]
    [Tooltip("NPCs y Manifestantes (Layer 'NPC')")]
    [SerializeField] float cullDistNPC        = 80f;
    [Tooltip("Props pequeños (farolas, papeleras, señales)")]
    [SerializeField] float cullDistPropSmall  = 55f;
    [Tooltip("Props medianos (coches aparcados, contenedores)")]
    [SerializeField] float cullDistPropMed    = 110f;
    [Tooltip("Vegetación pequeña (arbustos)")]
    [SerializeField] float cullDistBush       = 45f;

    [Header("2 · NavMesh throttle")]
    [Tooltip("Máximo de NavMeshAgents activos simultáneamente")]
    [SerializeField] int   maxAgentesActivos  = 40;
    [Tooltip("Intervalo de re-evaluación del ranking por distancia (s)")]
    [SerializeField] float intervaloThrottle  = 0.5f;

    [Header("4 · Physics")]
    [Tooltip("Hz de physics (50→30 ahorra ~35% de physics CPU)")]
    [SerializeField] float physicsHz          = 30f;
    [Tooltip("Desactivar colisión NPC↔NPC en la matrix de capas")]
    [SerializeField] bool  desactivarNPCvsNPC = true;

    [Header("6 · Static batching")]
    [Tooltip("Raíz de la ciudad horneada para StaticBatchingUtility.Combine")]
    [SerializeField] string nombreRaizCiudad  = "CiudadHorneada";
    [Tooltip("Combinar mallas de edificios al arrancar (puede costar 1-2s una sola vez)")]
    [SerializeField] bool  batchingAlArrancar = true;

    [Header("7 · Far clip dinámico")]
    [Tooltip("Far clip = radioActivacion × este multiplicador (0 = sin cambio)")]
    [SerializeField] float farClipMultiplier  = 1.4f;

    // ── Estado ────────────────────────────────────────────────────────────
    Camera   _cam;
    float    _tThrottle;
    readonly List<NavMeshAgent> _agentes = new(256);

    // ── Boot ──────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        // Esperar la cámara principal
        while (Camera.main == null) yield return null;
        _cam = Camera.main;

        AplicarLayerCullDistances();
        AplicarPhysicsMatrix();
        AplicarFixedTimestep();

        // Esperar baseline antes del batching (los edificios deben estar generados)
        while (!ArranqueMundo.BaselineListo) yield return new WaitForSeconds(0.5f);

        if (batchingAlArrancar) AplicarStaticBatching();
        AplicarSombrasProps();
    }

    // ── Update ────────────────────────────────────────────────────────────
    void Update()
    {
        // 2. NavMesh throttle
        _tThrottle -= Time.deltaTime;
        if (_tThrottle <= 0f)
        {
            _tThrottle = intervaloThrottle;
            ThrottleAgentes();
        }

        // 7. Far clip dinámico
        if (_cam != null && farClipMultiplier > 0f && GobernadorRender.Instancia != null)
        {
            float radioActivo = GobernadorRender.Instancia.RadioActivacion;
            float farDeseado  = Mathf.Max(500f, radioActivo * farClipMultiplier);
            _cam.farClipPlane = Mathf.Lerp(_cam.farClipPlane, farDeseado, Time.deltaTime * 0.5f);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  1 · LAYER CULL DISTANCES
    // ════════════════════════════════════════════════════════════════════════
    void AplicarLayerCullDistances()
    {
        if (_cam == null) return;

        float[] distancias = new float[32]; // Unity tiene 32 capas

        AsignarCapa(distancias, "NPC",        cullDistNPC);
        AsignarCapa(distancias, "Manifestante", cullDistNPC);
        AsignarCapa(distancias, "PropSmall",  cullDistPropSmall);
        AsignarCapa(distancias, "PropMedium", cullDistPropMed);
        AsignarCapa(distancias, "Vegetation", cullDistBush);
        // Edificios, terreno, etc. → 0 (sin límite, el streamer los gestiona)

        _cam.layerCullDistances = distancias;
        _cam.layerCullSpherical = true; // esfera en vez de plano → más correcto en mundo abierto

        Debug.Log("[OptimizadorFPS] layerCullDistances aplicadas. " +
                  $"NPC={cullDistNPC}m PropS={cullDistPropSmall}m Bush={cullDistBush}m");
    }

    static void AsignarCapa(float[] arr, string nombreCapa, float dist)
    {
        int idx = LayerMask.NameToLayer(nombreCapa);
        if (idx >= 0) arr[idx] = dist;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  2 · NAVMESH THROTTLE
    // ════════════════════════════════════════════════════════════════════════
    public void RegistrarAgente(NavMeshAgent ag) { if (ag != null && !_agentes.Contains(ag)) _agentes.Add(ag); }
    public void DesregistrarAgente(NavMeshAgent ag) { _agentes.Remove(ag); }

    void ThrottleAgentes()
    {
        if (_cam == null || _agentes.Count == 0) return;
        _agentes.RemoveAll(a => a == null || !a.gameObject.activeInHierarchy);

        Vector3 camPos = _cam.transform.position;

        // Ordenar por distancia (sólo re-ordenamos cada intervaloThrottle, no cada frame)
        _agentes.Sort((a, b) =>
            Vector3.SqrMagnitude(a.transform.position - camPos)
                .CompareTo(Vector3.SqrMagnitude(b.transform.position - camPos)));

        for (int i = 0; i < _agentes.Count; i++)
        {
            var ag = _agentes[i];
            bool debeEstarActivo = i < maxAgentesActivos;
            if (ag.isStopped == debeEstarActivo) // XOR: cambiar solo si necesario
            {
                ag.isStopped = !debeEstarActivo;
                if (!debeEstarActivo) ag.velocity = Vector3.zero;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  3 · PHYSICS LAYER MATRIX
    // ════════════════════════════════════════════════════════════════════════
    void AplicarPhysicsMatrix()
    {
        if (!desactivarNPCvsNPC) return;

        // Las capas NPC y Manifestante no necesitan colisionar entre sí
        // (NavMesh avoidance gestiona la separación)
        int npc  = LayerMask.NameToLayer("NPC");
        int mani = LayerMask.NameToLayer("Manifestante");

        if (npc  >= 0) Physics.IgnoreLayerCollision(npc,  npc,  true);
        if (npc  >= 0 && mani >= 0) Physics.IgnoreLayerCollision(npc, mani, true);
        if (mani >= 0) Physics.IgnoreLayerCollision(mani, mani, true);

        Debug.Log("[OptimizadorFPS] Physics: NPC↔NPC y NPC↔Manifestante ignorados.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  4 · FIXED TIMESTEP
    // ════════════════════════════════════════════════════════════════════════
    void AplicarFixedTimestep()
    {
        float paso = 1f / Mathf.Clamp(physicsHz, 15f, 60f);
        if (Mathf.Abs(Time.fixedDeltaTime - paso) > 0.001f)
        {
            Time.fixedDeltaTime = paso;
            Debug.Log($"[OptimizadorFPS] Physics Hz → {physicsHz:F0} (fixedDT={paso:F4}s)");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  5 · RENDERER.forceRenderingOff (API pública para StreamerMundoEstatico)
    // ════════════════════════════════════════════════════════════════════════
    /// Apaga/enciende el render de un NPC sin destruir su NavMeshAgent.
    /// Llamar desde StreamerMundoEstatico o el sistema de bandas.
    public static void SetRenderingNPC(GameObject go, bool visible)
    {
        if (go == null) return;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            r.forceRenderingOff = !visible;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  5b · SHADOW CASTING OFF EN PROPS PEQUEÑOS
    //
    //  Las capas PropSmall y Vegetation están culled a ≤55m y ≤45m.
    //  Más allá de esa distancia no se renderizan, pero SÍ pueden seguir
    //  generando shadow pass si están en el frustum de la luz.
    //  Desactivar ShadowCastingMode en estos objetos elimina ese coste.
    //  Se hace UNA SOLA VEZ tras baseline (objects static, no cambian).
    // ════════════════════════════════════════════════════════════════════════
    void AplicarSombrasProps()
    {
        // Capas que no necesitan proyectar sombras (pequeños / vegetación rasa)
        int maskSinSombra = LayerMask.GetMask("PropSmall", "Vegetation");
        int apagados = 0;
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (((1 << r.gameObject.layer) & maskSinSombra) == 0) continue;
            if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                apagados++;
            }
        }
        if (apagados > 0)
            Debug.Log($"[OptimizadorFPS] ShadowCasting OFF en {apagados} renderers de PropSmall/Vegetation → shadow pass más rápido.");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  6 · STATIC BATCHING TARDÍO
    // ════════════════════════════════════════════════════════════════════════
    void AplicarStaticBatching()
    {
        var raiz = GameObject.Find(nombreRaizCiudad);
        if (raiz == null)
        {
            Debug.LogWarning($"[OptimizadorFPS] Static batching: no se encontró '{nombreRaizCiudad}'.");
            return;
        }
        var renderers = raiz.GetComponentsInChildren<MeshRenderer>(false);
        if (renderers.Length == 0) return;

        StaticBatchingUtility.Combine(
            System.Array.ConvertAll(renderers, r => r.gameObject),
            raiz);

        Debug.Log($"[OptimizadorFPS] Static batching: {renderers.Length} mesh renderers " +
                  $"de '{nombreRaizCiudad}' combinados → draw calls reducidos.");
    }
}
