// Assets/Scripts/SistemaDeteccionIA.cs
// Batch de RaycastCommand para los raycasts de visión de PoliciaForalIA.
//
// Cada policía se registra al inicializarse y obtiene un slot fijo (0..MAX-1).
// En su tick de visión (cada VISION_TICK frames), escribe sus RaycastCommands
// en su bloque del NativeArray. SistemaDeteccionIA.LateUpdate() ejecuta el job
// batch y escribe los resultados booleanos en _visionResultado[].
//
// 1-frame lag en la detección: imperceptible para el jugador, elimina todos los
// Physics.Raycast síncronos del main thread durante la visión.

using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[DefaultExecutionOrder(-5)]
public class SistemaDeteccionIA : SingletonMono<SistemaDeteccionIA>
{
    const int MAX_AGENTES   = 32;
    const int RAYOS_POR_NPC = 3;           // pies / pecho / cabeza
    const int TOTAL_RAYOS   = MAX_AGENTES * RAYOS_POR_NPC;

    NativeArray<RaycastCommand> _comandos;
    NativeArray<RaycastHit>     _resultados;
    bool[]  _visionResultado = new bool[MAX_AGENTES];
    int     _slotSiguiente;
    readonly System.Collections.Generic.Stack<int> _libres = new();  // slots liberados reutilizables (auditoría: evita tope 32 permanente)
    JobHandle _job;
    bool      _jobVivo;

    protected override void OnAwake()
    {
        _comandos   = new NativeArray<RaycastCommand>(TOTAL_RAYOS, Allocator.Persistent);
        _resultados = new NativeArray<RaycastHit>    (TOTAL_RAYOS, Allocator.Persistent);
    }

    protected override void OnDestroyed()
    {
        if (_jobVivo) _job.Complete();
        if (_comandos.IsCreated)   _comandos.Dispose();
        if (_resultados.IsCreated) _resultados.Dispose();
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>Registrar un agente; devuelve su slotId fijo (-1 si lleno).</summary>
    public static int Registrar()
    {
        if (Instance == null) return -1;
        if (Instance._libres.Count > 0) return Instance._libres.Pop();   // reutilizar slot liberado
        if (Instance._slotSiguiente >= MAX_AGENTES) return -1;
        return Instance._slotSiguiente++;
    }

    /// <summary>Liberar el slot de un agente al morir/destruirse para que se reutilice (auditoría).</summary>
    public static void Liberar(int slot)
    {
        if (Instance == null || slot < 0 || slot >= MAX_AGENTES) return;
        Instance._libres.Push(slot);
    }

    /// <summary>
    /// Escribe los RaycastCommands del agente en su bloque del NativeArray.
    /// Llamar en el tick de visión (cada VISION_TICK frames).
    /// </summary>
    public static void EscribirComandos(int slotId, Vector3 origen,
                                        Transform jugador, float[] alturasLOS,
                                        int maskObstaculo)
    {
        if (Instance == null || slotId < 0 || jugador == null) return;
        Instance.ActualizarBloque(slotId, origen, jugador, alturasLOS, maskObstaculo);
    }

    /// <summary>¿Detectó el agente LOS libre en el último job ejecutado?</summary>
    public static bool TieneVision(int slotId)
        => Instance != null && slotId >= 0 && slotId < MAX_AGENTES
           && Instance._visionResultado[slotId];

    // ── Pipeline interno ──────────────────────────────────────────────────────

    void LateUpdate()
    {
        // Completar job del frame anterior y distribuir resultados
        if (_jobVivo)
        {
            _job.Complete();
            _jobVivo = false;
            DistribuirResultados();
        }

        // Programar job con los comandos actuales (incluye los de frames anteriores
        // para slots que no actualizaron este frame — sus comandos siguen siendo válidos)
        if (_slotSiguiente == 0) return;

        int n = _slotSiguiente * RAYOS_POR_NPC;
        // Cada RaycastCommand ya lleva su propia QueryParameters con la máscara correcta.
        _job = RaycastCommand.ScheduleBatch(
            _comandos.GetSubArray(0, n),
            _resultados.GetSubArray(0, n),
            minCommandsPerJob: 4,
            maxHits: 1,
            dependsOn: default);
        _jobVivo = true;
    }

    void ActualizarBloque(int slotId, Vector3 origen, Transform jugador,
                           float[] alturasLOS, int maskObstaculo)
    {
        int base_ = slotId * RAYOS_POR_NPC;
        var qp = new QueryParameters(layerMask: maskObstaculo,
                     hitTriggers: QueryTriggerInteraction.Ignore);

        for (int k = 0; k < RAYOS_POR_NPC; k++)
        {
            float altura = k < alturasLOS.Length ? alturasLOS[k] : 0.9f;
            Vector3 destino = jugador.position + Vector3.up * altura;
            Vector3 dir     = destino - origen;
            _comandos[base_ + k] = new RaycastCommand(origen, dir.normalized, qp, dir.magnitude);
        }
    }

    void DistribuirResultados()
    {
        for (int id = 0; id < _slotSiguiente; id++)
        {
            int base_ = id * RAYOS_POR_NPC;
            bool libre = false;
            for (int k = 0; k < RAYOS_POR_NPC && !libre; k++)
                libre = _resultados[base_ + k].collider == null;
            _visionResultado[id] = libre;
        }
    }
}
