// SistemaParanoia.cs
// Barra de paranoia: cuando sube, los civiles se convierten en GC disfrazados.
// Si el jugador los mata y no son GC: pierde apoyo y honor.
// Si son GC reales: sube el wanted level.
// Los chivatos avisan a la policía silenciosamente.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SistemaParanoia : MonoBehaviour
{
    public static SistemaParanoia Instance { get; private set; }

    [Header("Prefabs GC disfrazado")]
    public GameObject prefabCivilDisfrGC;  // modelo civil pero con GC debajo
    public GameObject prefabGCRevela;      // modelo GC que se revela al atacar

    [Header("Configuración")]
    public float tiempoRevela       = 0.3f;  // segundos antes de que el GC se revele al acercarse
    public float distanciaRevela    = 2f;    // distancia a la que se revela si toca al jugador
    public float chivatazoChance    = 0.25f; // probabilidad de que un civil chive a la policía
    public float incrementoWanted   = 2;     // wanted levels que sube el GC real al morir

    [Header("Efecto visual paranoia")]
    public Image imagenVignetteRoja;   // viñeta roja de pantalla

    // ── Estado ────────────────────────────────────────────────────────────
    readonly List<NPCParanoia> _npcs = new();
    SistemaApoyoPopular _apoyo;
    GameManagerAltsasua _gm;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        _apoyo = SistemaApoyoPopular.Instance;
        _gm    = GameManagerAltsasua.Instance;

        // Suscribirse a eventos de paranoia
        SistemaApoyoPopular.OnParanoiaCambia   += OnParanoiaCambia;
        SistemaApoyoPopular.OnParanoiaCritica  += OnParanoiaCritica;

        StartCoroutine(BucleNPCsParanoia());
    }

    void OnDestroy()
    {
        SistemaApoyoPopular.OnParanoiaCambia  -= OnParanoiaCambia;
        SistemaApoyoPopular.OnParanoiaCritica -= OnParanoiaCritica;
    }

    // ── Reacción a cambios de paranoia ────────────────────────────────────

    void OnParanoiaCambia(float nivel)
    {
        // Efecto visual: viñeta roja
        if (imagenVignetteRoja != null)
            imagenVignetteRoja.color = new Color(0.8f, 0.1f, 0.1f, Mathf.Clamp01(nivel / 100f) * 0.35f);
    }

    void OnParanoiaCritica()
    {
        // Alerta en pantalla
        StartCoroutine(FlashAlerta("⚠ PARANOIA CRÍTICA — ¿Quién es quién?"));
    }

    IEnumerator FlashAlerta(string mensaje)
    {
        Debug.LogWarning($"[Paranoia] {mensaje}");
        // Aquí se podría mostrar texto en pantalla HUD
        yield return null;
    }

    // ── Registro de NPCs ──────────────────────────────────────────────────

    public void RegistrarNPC(NPCParanoia npc) => _npcs.Add(npc);
    public void DesregistrarNPC(NPCParanoia npc) => _npcs.Remove(npc);

    // ── Lógica de matar NPC ───────────────────────────────────────────────

    /// Llamar desde el sistema de armas cuando el jugador mata a un NPC
    public void ProcesarMuerteNPC(GameObject npc)
    {
        var npcia = npc.GetComponent<NPCParanoia>();
        if (npcia == null)
        {
            // Civil normal muerto → pérdida masiva de apoyo
            _apoyo?.RestarApoyo(25f, "Civil inocente muerto");
            _apoyo?.SumarParanoia(15f);
            _gm?.AumentarBusqueda(2);
            Debug.Log("[Paranoia] ¡Civil inocente eliminado! Apoyo -25, Wanted +2");
            return;
        }

        if (npcia.esGCDisfrazado)
        {
            // Era GC real → sube wanted
            _gm?.AumentarBusqueda((int)incrementoWanted);
            _apoyo?.SumarApoyo(5f, "GC eliminado");
            Debug.Log("[Paranoia] Era Guardia Civil disfrazado. Wanted+" + incrementoWanted);
        }
        else
        {
            // Era civil inocente → pérdida de apoyo y honor
            _apoyo?.RestarApoyo(20f, "Civil inocente confundido con GC");
            _apoyo?.SumarParanoia(20f);
            Debug.Log("[Paranoia] ¡Era un civil inocente! Apoyo -20");
            // Otros NPCs cercanos se vuelven hostiles (testigos)
            NotificarTestigos(npc.transform.position, 40f);
        }
    }

    void NotificarTestigos(Vector3 pos, float radio)
    {
        foreach (var npc in _npcs)
        {
            if (npc == null) continue;
            float dist = Vector3.Distance(npc.transform.position, pos);
            if (dist < radio) npc.FueTestigo();
        }
    }

    // ── Spawn periódico de GC disfrazados según paranoia ──────────────────

    IEnumerator BucleNPCsParanoia()
    {
        while (true)
        {
            yield return new WaitForSeconds(30f);
            if (_apoyo == null) continue;
            float paranoia = _apoyo.paranoia;
            if (paranoia < 50f) continue;

            // Probabilidad de spawn proporcional a paranoia
            if (Random.value > paranoia / 100f) continue;

            var jugador = AltsasuCore.Jugador;
            if (jugador == null) continue;

            SpawnGCDisfrazado(jugador.position + Random.insideUnitSphere.With(y: 0) * 30f);
        }
    }

    void SpawnGCDisfrazado(Vector3 pos)
    {
        float y = Terrain.activeTerrain != null ? Terrain.activeTerrain.SampleHeight(pos) : 240f;
        pos.y = y;

        var prefab = prefabCivilDisfrGC;
        GameObject go;
        if (prefab != null)
            go = Instantiate(prefab, pos, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Civil_GC_Disfrazado";
            go.GetComponent<MeshRenderer>().material.color = new Color(0.4f, 0.5f, 0.7f); // pinta a civil
        }

        var npc = go.AddComponent<NPCParanoia>();
        npc.esGCDisfrazado    = true;
        npc.prefabRevela      = prefabGCRevela;
        npc.distanciaRevela   = distanciaRevela;
        npc.chivatazoChance   = chivatazoChance;
        npc.sistemaPrincipal  = this;

        RegistrarNPC(npc);
    }
}

// ── Componente individual de NPC con mecánica de paranoia ─────────────────

public class NPCParanoia : MonoBehaviour
{
    public bool  esGCDisfrazado;
    public GameObject prefabRevela;
    public float distanciaRevela;
    public float chivatazoChance;
    public SistemaParanoia sistemaPrincipal;

    bool  _revelado;
    bool  _chivato;
    float _timerChivatazo;

    void Start()
    {
        sistemaPrincipal?.RegistrarNPC(this);
        // Los chivatos esperan 20-60s antes de avisar
        _chivato = Random.value < chivatazoChance;
        _timerChivatazo = Random.Range(20f, 60f);
    }

    void OnDestroy() => sistemaPrincipal?.DesregistrarNPC(this);

    void Update()
    {
        if (_revelado) return;

        var jugador = AltsasuCore.Jugador;
        if (jugador == null) return;

        float dist = Vector3.Distance(transform.position, jugador.position);

        // Revelar si el jugador está muy cerca o lo ataca
        if (esGCDisfrazado && dist < distanciaRevela)
            Revelarse();

        // Chivatazo silencioso
        if (_chivato && !esGCDisfrazado)
        {
            _timerChivatazo -= Time.deltaTime;
            if (_timerChivatazo <= 0)
                EjecutarChivatazo();
        }
    }

    public void Revelarse()
    {
        if (_revelado) return;
        _revelado = true;

        // Cambiar modelo al GC revelado
        if (prefabRevela != null)
        {
            var gc = Instantiate(prefabRevela, transform.position, transform.rotation);
            gc.name = "GC_Revelado";
            // Dar el mismo componente de paranoia
            var npc2 = gc.AddComponent<NPCParanoia>();
            npc2.esGCDisfrazado = true;
            npc2.sistemaPrincipal = sistemaPrincipal;
            sistemaPrincipal?.RegistrarNPC(npc2);
        }

        // Subir wanted
        GameManagerAltsasua.Instance?.AumentarBusqueda(1);
        Debug.Log($"[Paranoia] ¡{gameObject.name} revela ser Guardia Civil!");
        Destroy(gameObject);
    }

    public void FueTestigo()
    {
        // El NPC fue testigo de un crimen → puede chivarse antes
        _chivato = true;
        _timerChivatazo = Mathf.Min(_timerChivatazo, 10f);
    }

    void EjecutarChivatazo()
    {
        _chivato = false;
        GameManagerAltsasua.Instance?.AumentarBusqueda(1);
        SistemaApoyoPopular.Instance?.RestarApoyo(5f, "Chivatazo de civil");
        Debug.Log($"[Paranoia] {gameObject.name} ha chivado a la policía. Wanted +1");
        // El chivato huye de la zona
        Destroy(gameObject, 3f);
    }
}

static class Vec3Ext
{
    public static Vector3 With(this Vector3 v, float? x = null, float? y = null, float? z = null)
        => new Vector3(x ?? v.x, y ?? v.y, z ?? v.z);
}
