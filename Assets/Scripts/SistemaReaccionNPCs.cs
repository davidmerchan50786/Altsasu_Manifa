// Assets/Scripts/SistemaReaccionNPCs.cs
// ═══════════════════════════════════════════════════════════════════════════
//  REACCIÓN DE NPCs A EVENTOS GLOBALES
//
//  Suscriptor de DirectorMundo.OnEvento que hace reaccionar a todos los
//  NPCs civiles en escena según el tipo de evento:
//
//    ControlPolicial / Redada  → NPCs en radio de la Herriko Plaza huyen
//                                 (llama NPCBase.Alertar desde el centro del evento)
//    Disturbio                 → NPCs se dispersan con radio más amplio
//    Calma / MercadoDia        → NPCs reanuden su agenda normal (Idle)
//
//  Look-At procedural:
//    Cada NPC tiene un LookAtProxy que gira la cabeza hacia el jugador
//    cuando está a menos de lookAtRadio metros. Sin Animator IK — solo
//    rotación suave del nodo de cabeza. Zero GC en steady state.
//
//  Activación:
//    Añadir este componente a cualquier GO de la escena. Se auto-configura.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;

[DefaultExecutionOrder(180)]
public class SistemaReaccionNPCs : MonoBehaviour
{
    public static SistemaReaccionNPCs Instance { get; private set; }

    // ── Config ────────────────────────────────────────────────────────────
    [Header("Dispersión")]
    [SerializeField] float radioRedada    = 300f;   // m alrededor del centro del evento
    [SerializeField] float radioDisturbio = 500f;

    [Header("Look-At")]
    [SerializeField] float lookAtRadio   = 8f;    // m: distancia a la que el NPC mira al jugador
    [SerializeField] float lookAtVelocidad = 3f;  // Hz de suavizado de giro de cabeza
    [SerializeField] float lookAtAnguloMax = 70f; // °: límite de giro lateral de cabeza

    // ── Estado ────────────────────────────────────────────────────────────
    readonly List<LookAtProxy> _proxies = new();
    float _timerLookAt;

    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnEnable()  => DirectorMundo.OnEvento += ReaccionarEvento;
    void OnDisable() => DirectorMundo.OnEvento -= ReaccionarEvento;

    void Start()
    {
        // Registrar todos los NPCBase de la escena para look-at
        foreach (var npc in FindObjectsByType<NPCBase>(FindObjectsSortMode.None))
            RegistrarNPC(npc);
    }

    // ── API pública ───────────────────────────────────────────────────────

    public static void RegistrarNPC(NPCBase npc)
    {
        if (Instance == null || npc == null) return;

        // Buscar nodo de cabeza por nombre
        Transform cabeza = BuscarCabeza(npc.transform);
        if (cabeza == null) return;

        Instance._proxies.Add(new LookAtProxy { npc = npc, cabeza = cabeza });
    }

    // ════════════════════════════════════════════════════════════════════════
    //  REACCIÓN A DIRECTOR
    // ════════════════════════════════════════════════════════════════════════

    void ReaccionarEvento(DirectorMundo.EventoMundo ev)
    {
        Vector3 centro = AltsasuCore.Jugador != null
            ? AltsasuCore.Jugador.position
            : GeoDataAlsasua.HerrikoPlaza;

        switch (ev)
        {
            case DirectorMundo.EventoMundo.ControlPolicial:
            case DirectorMundo.EventoMundo.Redada:
                AlertarNPCsEnRadio(centro, radioRedada);
                break;

            case DirectorMundo.EventoMundo.Disturbio:
                AlertarNPCsEnRadio(centro, radioDisturbio);
                break;
        }
    }

    void AlertarNPCsEnRadio(Vector3 centro, float radio)
    {
        foreach (var p in _proxies)
        {
            if (p.npc == null) continue;
            if (Vector3.Distance(p.npc.transform.position, centro) <= radio)
                p.npc.Alertar(centro);
        }
        AlsasuaLogger.Info("ReaccionNPCs", $"Alertados NPCs en radio {radio}m");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  LOOK-AT PROCEDURAL — actualización time-sliced
    // ════════════════════════════════════════════════════════════════════════

    void Update()
    {
        _timerLookAt += Time.deltaTime;
        if (_timerLookAt < 0.05f) return;   // 20 Hz suficiente para look-at
        _timerLookAt = 0f;

        var jugador = AltsasuCore.Jugador;
        if (jugador == null) return;
        float dt = Time.deltaTime * 20f;    // compensar el throttle

        for (int i = _proxies.Count - 1; i >= 0; i--)
        {
            var p = _proxies[i];
            if (p.npc == null || p.cabeza == null) { _proxies.RemoveAt(i); continue; }

            float dist = Vector3.Distance(p.npc.transform.position, jugador.position);
            if (dist > lookAtRadio)
            {
                // Fuera de rango: restaurar rotación de cabeza suavemente
                p.cabeza.localRotation = Quaternion.Slerp(
                    p.cabeza.localRotation, Quaternion.identity, dt * lookAtVelocidad * 0.5f);
                continue;
            }

            // Calcular dirección hacia el jugador en espacio local del NPC
            Vector3 dirMundo = (jugador.position + Vector3.up * 1.6f) - p.cabeza.position;
            Vector3 dirLocal = p.npc.transform.InverseTransformDirection(dirMundo);

            // Clamp ángulo horizontal
            float anguloH = Mathf.Atan2(dirLocal.x, dirLocal.z) * Mathf.Rad2Deg;
            anguloH = Mathf.Clamp(anguloH, -lookAtAnguloMax, lookAtAnguloMax);

            // Clamp ángulo vertical (no romper el cuello)
            float anguloV = Mathf.Atan2(-dirLocal.y, dirLocal.z) * Mathf.Rad2Deg;
            anguloV = Mathf.Clamp(anguloV, -30f, 30f);

            Quaternion objetivo = Quaternion.Euler(anguloV, anguloH, 0f);
            p.cabeza.localRotation = Quaternion.Slerp(
                p.cabeza.localRotation, objetivo, dt * lookAtVelocidad);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════════════

    static Transform BuscarCabeza(Transform raiz)
    {
        // Nombres de hueso de cabeza habituales (Mixamo / Humanoid)
        foreach (var nombre in new[] { "Head", "head", "Cabeza", "cabeza", "Bip001 Head", "mixamorig:Head" })
        {
            var t = raiz.Find(nombre);
            if (t != null) return t;
            // Búsqueda recursiva un nivel (modelos con rig anidado)
            foreach (Transform hijo in raiz)
            {
                var t2 = hijo.Find(nombre);
                if (t2 != null) return t2;
            }
        }
        // Fallback: usar la raíz del NPC con offset de cabeza
        return null;
    }

    struct LookAtProxy
    {
        public NPCBase  npc;
        public Transform cabeza;
    }
}
