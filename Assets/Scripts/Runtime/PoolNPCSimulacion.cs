// Assets/Scripts/Runtime/PoolNPCSimulacion.cs
// ═══════════════════════════════════════════════════════════════════════════
//  POOL DE SIMULACIÓN POR DESACTIVACIÓN — capa RUNTIME
//
//  No es un pool clásico de objetos intercambiables (como balas): cada NPC es
//  ÚNICO y conserva su identidad/estado. La idea es NO destruir el GameObject
//  cuando un NPC pasa a Ghost (lejos/ocluido), sino aparcarlo bajo un contenedor
//  INACTIVO. Un GameObject hijo de un transform inactivo no recibe Update, no
//  renderiza, no simula físicas ni NavMesh → coste ~0, sin GC ni picos de Destroy.
//
//  Al PROMOCIONAR de vuelta (Ghost → Proxy/Actor) se reactiva el MISMO GO,
//  se devuelve a la raíz de escena y se hace Warp del NavMeshAgent para no
//  corromper el path con la teletransportación.
//
//  TRABAJO FUTURO (fuera de alcance): el "Ghost real" sin GameObject — el NPC
//  lejano se representa como una fila en el NativeArray de la multitud BRG y su
//  posición la calcula un Job Burst (sin Transform ni componentes). Este pool es
//  el paso intermedio por desactivación; cuando exista el merge BRG, Ghost dejará
//  de aparcar el GO y lo cederá al sistema de multitud.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Aparca/reactiva NPCs en Ghost sin destruirlos. Singleton perezoso: se
/// auto-crea la primera vez que un NPC necesita aparcar (no requiere estar en
/// la escena de antemano).
/// </summary>
public sealed class PoolNPCSimulacion : SingletonMono<PoolNPCSimulacion>
{
    const string TAG = "PoolNPC";

    // Contenedor INACTIVO bajo el que cuelgan los NPC aparcados. Estar bajo un
    // padre inactivo desactiva toda la jerarquía hija sin tocar su activeSelf.
    Transform _contenedor;
    int _aparcados;

    public int Aparcados => _aparcados;

    /// <summary>Obtiene (creando si hace falta) la instancia del pool.</summary>
    public static PoolNPCSimulacion ObtenerOCrear()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("PoolNPCSimulacion");
        return go.AddComponent<PoolNPCSimulacion>();
    }

    protected override void OnAwake()
    {
        var cont = new GameObject("[NPCs Aparcados]");
        cont.transform.SetParent(transform, false);
        cont.SetActive(false);               // raíz inactiva → hijos no se simulan
        _contenedor = cont.transform;
    }

    /// <summary>
    /// Aparca el NPC: lo recuelga bajo el contenedor inactivo. El llamador
    /// (NPCBase) ya ha desactivado sus componentes pesados antes; esto garantiza
    /// además que ningún Update/corrutina del GO siga vivo.
    /// </summary>
    public void Aparcar(NPCBase npc)
    {
        if (npc == null) return;
        if (_contenedor == null) return;     // pool a medio destruir; no-op seguro
        // Conserva la posición mundial para poder hacer Warp correcto al volver.
        npc.transform.SetParent(_contenedor, true);
        _aparcados++;
    }

    /// <summary>
    /// Devuelve el NPC a la raíz de escena y lo reactiva. El reenganche al
    /// NavMesh (Warp) lo hace NPCBase, que conoce su _agente.
    /// </summary>
    public void Reactivar(NPCBase npc)
    {
        if (npc == null) return;
        // Sacarlo del contenedor inactivo lo "re-despierta" (vuelve a recibir Update).
        npc.transform.SetParent(null, true);
        if (_aparcados > 0) _aparcados--;
    }

    protected override void OnDestroyed()
    {
        // Si el pool muere con NPCs aún aparcados, NO los dejamos huérfanos bajo
        // un GO destruido: se reparentan a la raíz (quedarán inactivos por su
        // propio activeSelf si NPCBase lo puso así, pero al menos no se destruyen).
        if (_contenedor != null)
        {
            for (int i = _contenedor.childCount - 1; i >= 0; i--)
                _contenedor.GetChild(i).SetParent(null, true);
        }
        _aparcados = 0;
        AlsasuaLogger.Info(TAG, "Pool destruido; NPCs aparcados liberados a la raíz.");
    }
}
