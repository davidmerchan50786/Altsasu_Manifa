// Assets/Scripts/Core/Simulacion/PoolGhosts.cs
// ═══════════════════════════════════════════════════════════════════════════
//  POOL GHOSTS — capa de DATOS del Nivel 2 (Ghost) del Sim-LOD (capa CORE)
//
//  Completa el Pilar 2 del Orquestador: un Ghost no solo se aparca (eso ya lo
//  hace PoolNPCSimulacion por desactivación del GameObject) sino que su posición
//  SIGUE EVOLUCIONANDO mientras está aparcado, calculada por un Job Burst @1 Hz
//  ("probabilidades de movimiento" del spec) — sin Transform ni componentes.
//
//  · El GO aparcado queda CONGELADO (coste ~0); el movimiento vive aquí, en un
//    NativeArray. Al promocionar, NPCBase lee la posición simulada y teletransporta
//    el GO ahí → el ghost "reaparece donde habría caminado".
//  · Slots ESTABLES con free-list (sin swap-remove) → NPCBase guarda un int slot
//    y nunca hay que reindexar. id-opaco: Core no sabe qué es un NPC.
//  · Se registra como ITickable (@Hz1) en el Director → entra en el time-slicing
//    existente sin tubería nueva.
//
//  Diseño: Docs/arquitectura_orquestador.md §2.3
// ═══════════════════════════════════════════════════════════════════════════

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>Contrato del pool de datos de ghosts. API en Vector3 → los consumidores
/// (Runtime) no necesitan conocer float3 ni la estructura interna.</summary>
public interface IPoolGhosts
{
    /// <summary>Registra un ghost; devuelve su slot ESTABLE (guárdalo). El Job lo
    /// hará derivar hacia `destino` mientras esté vivo. `tipo` se publica al Omni-Grid.</summary>
    int  Registrar(Vector3 pos, Vector3 destino, float velocidad, TipoEspacial tipo);
    /// <summary>Lee la posición simulada del slot. False si el slot ya no está vivo.</summary>
    bool LeerPos(int slot, out Vector3 pos);
    /// <summary>Libera el slot (al promocionar el ghost a Proxy/Actor o al destruirse).</summary>
    void Liberar(int slot);
    /// <summary>Ghosts vivos ahora mismo (debug/overlay).</summary>
    int  Vivos { get; }
}

public sealed class PoolGhosts : IPoolGhosts, ITickable
{
    public static PoolGhosts Instancia { get; private set; }

    public struct GhostData
    {
        public float3       pos;
        public float3       destino;
        public float        vel;
        public int          activo;     // 1 vivo, 0 libre (blittable; bool también valdría)
        public TipoEspacial tipo;       // para el Omni-Grid
        public int          handleGrid; // handle de la entidad retenida en el grid (-1 si no inscrita)
    }

    NativeList<GhostData> _ghosts;
    NativeList<int>       _libres;
    uint _semilla = 0x9E3779B9u;
    bool _tickRegistrado;
    int  _vivos;

    public int Vivos => _vivos;
    public Frecuencia Frecuencia => Frecuencia.Hz1;   // simulación barata y lejana

    // ════════════════════════════════════════════════════════════════════════
    //  BOOT
    // ════════════════════════════════════════════════════════════════════════
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        Instancia?.Cerrar();
        Instancia = new PoolGhosts();
        ServiceLocator.Registrar<IPoolGhosts>(Instancia);
        Application.quitting += AlSalir;
    }

    static void AlSalir() { Instancia?.Cerrar(); Instancia = null; }

    PoolGhosts()
    {
        _ghosts = new NativeList<GhostData>(256, Allocator.Persistent);
        _libres = new NativeList<int>(256, Allocator.Persistent);
    }

    // Registro perezoso como ITickable: el Director puede no existir aún en Boot
    // (mismo tipo de RuntimeInitialize). Cuando un NPC ghostea, ya existe seguro.
    void AsegurarTick()
    {
        if (_tickRegistrado) return;
        var orq = GlobalSimulationOrchestrator.Instancia;
        if (orq != null) { orq.Registrar((ITickable)this); _tickRegistrado = true; }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  IPoolGhosts
    // ════════════════════════════════════════════════════════════════════════
    public int Registrar(Vector3 pos, Vector3 destino, float velocidad, TipoEspacial tipo)
    {
        AsegurarTick();
        _vivos++;

        // Elegir slot estable PRIMERO (free-list) → podemos publicar id=slot al grid.
        int slot;
        if (_libres.Length > 0)
        {
            slot = _libres[_libres.Length - 1];
            _libres.RemoveAt(_libres.Length - 1);
        }
        else { slot = _ghosts.Length; _ghosts.Add(default); }

        // Publicar como entidad RETENIDA en el Omni-Grid → IA/streaming "ven" al ghost
        // aunque no tenga GameObject. Su posición se refresca en Tick().
        var grid = ServiceLocator.Get<IUnifiedSpatialGrid>();
        int handle = grid != null
            ? grid.SubmitRetenido(new SpatialData { pos = pos, id = slot, tipo = tipo, radio = 0.5f })
            : -1;

        _ghosts[slot] = new GhostData
        {
            pos = pos, destino = destino, vel = math.max(0.1f, velocidad),
            activo = 1, tipo = tipo, handleGrid = handle
        };
        return slot;
    }

    public bool LeerPos(int slot, out Vector3 pos)
    {
        if (slot < 0 || slot >= _ghosts.Length || _ghosts[slot].activo == 0)
        { pos = Vector3.zero; return false; }
        pos = _ghosts[slot].pos;
        return true;
    }

    public void Liberar(int slot)
    {
        if (slot < 0 || slot >= _ghosts.Length || _ghosts[slot].activo == 0) return;
        var g = _ghosts[slot];
        if (g.handleGrid >= 0) ServiceLocator.Get<IUnifiedSpatialGrid>()?.QuitarRetenido(g.handleGrid);
        g.activo = 0; g.handleGrid = -1; _ghosts[slot] = g;
        _libres.Add(slot);
        if (_vivos > 0) _vivos--;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  TICK (lo llama el Director @Hz1)
    // ════════════════════════════════════════════════════════════════════════
    public void Tick(float dtAcumulado)
    {
        if (_ghosts.Length == 0) return;
        new SimGhostJob
        {
            ghosts  = _ghosts.AsArray(),
            dt      = dtAcumulado,
            semilla = _semilla
        }.Schedule(_ghosts.Length, 128).Complete();
        _semilla += 0x6D2B79F5u;   // varía la deriva estocástica entre ticks

        // Empujar las posiciones derivadas al Omni-Grid (entidades retenidas) → IA y
        // streaming las "ven" aunque el ghost no tenga GameObject.
        var grid = ServiceLocator.Get<IUnifiedSpatialGrid>();
        if (grid != null)
            for (int i = 0; i < _ghosts.Length; i++)
            {
                var g = _ghosts[i];
                if (g.activo == 1 && g.handleGrid >= 0) grid.ActualizarRetenido(g.handleGrid, g.pos);
            }
    }

    void Cerrar()
    {
        if (_tickRegistrado)
        {
            GlobalSimulationOrchestrator.Instancia?.Desregistrar((ITickable)this);
            _tickRegistrado = false;
        }
        if (ReferenceEquals(ServiceLocator.Get<IPoolGhosts>(), this))
            ServiceLocator.Desregistrar<IPoolGhosts>();
        if (_ghosts.IsCreated) _ghosts.Dispose();
        if (_libres.IsCreated) _libres.Dispose();
        _vivos = 0;
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    static void HookEditor()
    {
        UnityEditor.EditorApplication.playModeStateChanged += s =>
        {
            if (s == UnityEditor.PlayModeStateChange.ExitingPlayMode) AlSalir();
        };
    }
#endif
}

// ════════════════════════════════════════════════════════════════════════════
//  JOB — deriva de los ghosts hacia su destino + ruido ("probabilidad de mov.")
// ════════════════════════════════════════════════════════════════════════════
[BurstCompile]
public struct SimGhostJob : IJobParallelFor
{
    public NativeArray<PoolGhosts.GhostData> ghosts;
    public float dt;
    public uint  semilla;

    public void Execute(int i)
    {
        var g = ghosts[i];
        if (g.activo == 0) return;

        float3 dir = math.normalizesafe(g.destino - g.pos);   // →0 al llegar (se queda merodeando)
        var rnd = new Unity.Mathematics.Random(semilla + (uint)(i + 1) * 747796405u);
        float3 ruido = (rnd.NextFloat3() - 0.5f) * 0.3f; ruido.y = 0f;

        g.pos += (dir * 0.6f + ruido) * g.vel * dt;   // XZ; la Y la re-fija el NavMesh al promocionar
        ghosts[i] = g;
    }
}
