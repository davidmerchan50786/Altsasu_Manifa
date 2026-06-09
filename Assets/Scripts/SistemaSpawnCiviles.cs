// Assets/Scripts/SistemaSpawnCiviles.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE SPAWN DE CIVILES — calles vivas en Alsasua
//
//  Mantiene un pool de NPCCivil en vida, teleportándolos a puntos del
//  NavMesh cercanos al jugador cuando se alejan demasiado. El ciclo
//  funciona como el sistema de vehículos de tráfico: pool fijo, zero alloc.
//
//  Densidad por hora del día (coordina con SistemaAgendaNPC):
//    22h–6h   (noche)       → maxCiviles × 0.1
//    6h–8h    (madrugada)   → maxCiviles × 0.3
//    8h–9h    (hora punta)  → maxCiviles × 1.0
//    9h–13h   (mañana)      → maxCiviles × 0.5
//    13h–15h  (mediodía)    → maxCiviles × 0.8
//    15h–17h  (siesta)      → maxCiviles × 0.3
//    17h–20h  (tarde punta) → maxCiviles × 0.9
//    20h–22h  (tarde)       → maxCiviles × 0.6
//
//  Quality tier:
//    0 Ultra      → maxCiviles = 24
//    1 Alto       → maxCiviles = 16
//    2 Medio      → maxCiviles = 8
//    3 Performance → maxCiviles = 3
//
//  Reacción a DirectorMundo:
//    Redada / ControlPolicial → civiles huyen y se reduce densidad al 20%
//    Calma                   → densidad normal
//
//  Prefab:
//    Si ConfiguradorAssetsAAA.GetPrefabCivil() tiene prefabs asignados,
//    los usa. Si no, genera un humanoide procedural de cápsulas (fallback).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

[DefaultExecutionOrder(160)]
public class SistemaSpawnCiviles : MonoBehaviour
{
    public static SistemaSpawnCiviles Instance { get; private set; }

    // ── Config ────────────────────────────────────────────────────────────
    [Tooltip("Máximo de civiles en escena con tier 0 (Ultra)")]
    [Range(4, 40)]
    [SerializeField] int maxCivilesUltra = 24;

    [Tooltip("Radio de spawn alrededor del jugador (m)")]
    [SerializeField] float radioSpawn    = 80f;
    [Tooltip("Distancia a la que un civil se recicla (m desde el jugador)")]
    [SerializeField] float radioReciclaje = 120f;
    [Tooltip("Radio de exclusión — no spawnear a menos de X metros del jugador")]
    [SerializeField] float radioExclusion = 12f;

    // ── Estado interno ────────────────────────────────────────────────────
    readonly List<NPCCivil> _pool      = new();
    bool   _modoRedada;
    float  _timerCiclo;

    static readonly int ID_NightLevel = Shader.PropertyToID("_GlobalNightLevel");

    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(12f);   // esperar NavMesh + terreno

        InicializarPool();
        StartCoroutine(BucleCiviles());

        DirectorMundo.OnEvento += ReaccionarDirector;
        AlsasuaLogger.Info("SpawnCiviles", $"Pool de {_pool.Count} civiles listo");
    }

    void OnDestroy() => DirectorMundo.OnEvento -= ReaccionarDirector;

    // ════════════════════════════════════════════════════════════════════════
    //  POOL
    // ════════════════════════════════════════════════════════════════════════

    void InicializarPool()
    {
        for (int i = 0; i < maxCivilesUltra; i++)
        {
            var go = CrearCivil(i);
            go.SetActive(false);

            var npc = go.GetComponent<NPCCivil>();
            if (npc == null) npc = go.AddComponent<NPCCivil>();
            _pool.Add(npc);

            // Variar apariencia
            if (go.GetComponent<VariadorAparienciaNPC>() == null)
                go.AddComponent<VariadorAparienciaNPC>();
        }
    }

    GameObject CrearCivil(int idx)
    {
        // 1. SistemaAssets (Resources/Prefabs/NPCs) — fuente principal
        var assets = SistemaAssets.Instance;
        if (assets != null && assets.ContarCiviles() > 0)
        {
            var prefab = assets.CivilAleatorio();
            if (prefab != null)
            {
                var go = Instantiate(prefab, Vector3.down * 500f, Quaternion.identity, transform);
                go.name = $"Civil_{idx:00}";
                return go;
            }
        }
        // 2. Fallback: ConfiguradorAssetsAAA (SerializeField asignado en Inspector)
        var cfg = ConfiguradorAssetsAAA.Instance;
        if (cfg != null)
        {
            var prefab = cfg.GetPrefabCivil();
            if (prefab != null)
            {
                var go = Instantiate(prefab, Vector3.down * 500f, Quaternion.identity, transform);
                go.name = $"Civil_{idx:00}";
                return go;
            }
        }
        // 3. Último recurso: cápsula procedural
        return CrearCivilProcedural(idx);
    }

    static GameObject CrearCivilProcedural(int idx)
    {
        var go   = new GameObject($"Civil_{idx:00}");
        var rb   = go.AddComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

        // Cuerpo — cápsula
        var cuerpo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        cuerpo.transform.SetParent(go.transform);
        cuerpo.transform.localPosition = new Vector3(0f, 1f, 0f);
        cuerpo.transform.localScale    = new Vector3(0.35f, 0.9f, 0.35f);
        cuerpo.GetComponent<Collider>().enabled = false;

        // Cabeza
        var cabeza = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        cabeza.transform.SetParent(go.transform);
        cabeza.transform.localPosition = new Vector3(0f, 1.75f, 0f);
        cabeza.transform.localScale    = Vector3.one * 0.22f;
        cabeza.name = "Head";
        cabeza.GetComponent<Collider>().enabled = false;

        // Color de piel aleatorio (paleta vasca variada)
        Color[] pieles = {
            new Color(0.95f, 0.80f, 0.70f), new Color(0.85f, 0.65f, 0.50f),
            new Color(0.70f, 0.50f, 0.38f), new Color(0.55f, 0.38f, 0.28f)
        };
        Color[] ropa = {
            Color.gray, Color.black, new Color(0.2f,0.3f,0.6f),
            new Color(0.6f,0.2f,0.2f), new Color(0.2f,0.5f,0.2f)
        };
        cuerpo.GetComponent<MeshRenderer>().material.color  = ropa[idx % ropa.Length];
        cabeza.GetComponent<MeshRenderer>().material.color  = pieles[idx % pieles.Length];

        go.AddComponent<CapsuleCollider>().height = 1.8f;
        return go;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  BUCLE PRINCIPAL
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator BucleCiviles()
    {
        while (true)
        {
            yield return new WaitForSeconds(3f);

            var jugador = AltsasuCore.Jugador;
            if (jugador == null) continue;
            if (!SistemaNavMesh.EstaListo) continue;

            int objetivo = CalcularObjetivo();
            int activos  = ContarActivos();

            // Reciclar civiles demasiado lejanos
            foreach (var c in _pool)
            {
                if (c == null || !c.gameObject.activeSelf) continue;
                if (Vector3.Distance(c.transform.position, jugador.position) > radioReciclaje)
                    c.gameObject.SetActive(false);
            }

            // Activar más si hace falta
            int deficit = objetivo - ContarActivos();
            for (int i = 0; i < deficit; i++)
                SpawnUnoCivil(jugador.position);
        }
    }

    void SpawnUnoCivil(Vector3 centro)
    {
        var libre = _pool.Find(c => c != null && !c.gameObject.activeSelf);
        if (libre == null) return;

        // Punto aleatorio en NavMesh dentro del radio de spawn
        for (int intentos = 0; intentos < 8; intentos++)
        {
            Vector2 rnd   = Random.insideUnitCircle.normalized * Random.Range(radioExclusion + 2f, radioSpawn);
            Vector3 cand  = centro + new Vector3(rnd.x, 0f, rnd.y);
            if (!NavMesh.SamplePosition(cand, out NavMeshHit hit, 5f, NavMesh.AllAreas)) continue;

            libre.transform.position = hit.position;
            libre.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            libre.gameObject.SetActive(true);

            // Registrar en SistemaReaccionNPCs para look-at
            SistemaReaccionNPCs.RegistrarNPC(libre);
            return;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  DENSIDAD
    // ════════════════════════════════════════════════════════════════════════

    int CalcularObjetivo()
    {
        int max = MaxPorTier();
        if (_modoRedada) return Mathf.Max(1, Mathf.RoundToInt(max * 0.15f));

        float hora = HoraSimulada();
        float factor = hora switch
        {
            var h when h >= 22f || h < 6f  => 0.10f,
            var h when h >= 8f  && h < 9f  => 1.00f,
            var h when h >= 17f && h < 20f => 0.90f,
            var h when h >= 13f && h < 15f => 0.80f,
            var h when h >= 20f && h < 22f => 0.60f,
            var h when h >= 9f  && h < 13f => 0.50f,
            var h when h >= 6f  && h < 8f  => 0.30f,
            _                              => 0.30f,
        };
        return Mathf.Max(1, Mathf.RoundToInt(max * factor));
    }

    int MaxPorTier() => SistemaOptimizacion.TierCalidad switch
    {
        0 => maxCivilesUltra,
        1 => Mathf.RoundToInt(maxCivilesUltra * 0.67f),
        2 => Mathf.RoundToInt(maxCivilesUltra * 0.33f),
        _ => Mathf.Max(2, Mathf.RoundToInt(maxCivilesUltra * 0.12f)),
    };

    float HoraSimulada()
    {
        float night = Shader.GetGlobalFloat(ID_NightLevel);
        return Mathf.Lerp(12f, 0f, night);
    }

    int ContarActivos() => _pool.FindAll(c => c != null && c.gameObject.activeSelf).Count;

    // ── Director ──────────────────────────────────────────────────────────

    void ReaccionarDirector(DirectorMundo.EventoMundo ev)
    {
        switch (ev)
        {
            case DirectorMundo.EventoMundo.Redada:
            case DirectorMundo.EventoMundo.ControlPolicial:
                _modoRedada = true;
                // Alertar a todos los civiles activos para que huyan
                var jugador = AltsasuCore.Jugador;
                if (jugador != null)
                    foreach (var c in _pool)
                        if (c != null && c.gameObject.activeSelf)
                            c.Alertar(jugador.position);
                break;

            case DirectorMundo.EventoMundo.Calma:
            case DirectorMundo.EventoMundo.MercadoDia:
                _modoRedada = false;
                break;
        }
    }
}
