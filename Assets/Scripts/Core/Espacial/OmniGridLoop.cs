// Assets/Scripts/Core/Espacial/OmniGridLoop.cs
// ═══════════════════════════════════════════════════════════════════════════
//  OMNI-GRID — ARRANQUE E INYECCIÓN EN EL PLAYERLOOP (capa CORE)
//
//  Construye el grid en EarlyUpdate (ANTES de todos los Update) → cuando corren
//  los Update de productores/consumidores, el grid del frame ya está estable.
//  Mismo patrón que GlobalSimulationOrchestrator (que se inyecta en Update, DESPUÉS).
//
//  Orden por frame resultante:
//    EarlyUpdate → OmniGrid.ConstruirFrame()   (grid listo desde el snapshot N-1)
//    Update      → productores .Update()        (Query sobre el grid + Submit para N+1)
//    Update      → GlobalSimulationOrchestrator (puede consultar el grid)
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

public static class OmniGridLoop
{
    static UnifiedSpatialGrid _grid;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        Desinstalar();                       // limpia delegado viejo (re-Play sin domain reload)
        _grid?.Dispose();

        _grid = new UnifiedSpatialGrid();
        ServiceLocator.Registrar<IUnifiedSpatialGrid>(_grid);

        Instalar(_grid.ConstruirFrame);
        Application.quitting += AlSalir;
    }

    static void AlSalir()
    {
        Desinstalar();
        GridConsultas.Liberar();
        _grid?.Dispose();
        _grid = null;
        ServiceLocator.Desregistrar<IUnifiedSpatialGrid>();
    }

    // ── Inyección en EarlyUpdate (al frente de la lista → antes que el resto) ──
    static void Instalar(PlayerLoopSystem.UpdateFunction fn)
    {
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        var nuestro = new PlayerLoopSystem { type = typeof(OmniGridLoop), updateDelegate = fn };

        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            if (loop.subSystemList[i].type != typeof(EarlyUpdate)) continue;
            var sub = new List<PlayerLoopSystem>(loop.subSystemList[i].subSystemList);
            sub.Insert(0, nuestro);                            // antes de los EarlyUpdate normales
            loop.subSystemList[i].subSystemList = sub.ToArray();
            break;
        }
        PlayerLoop.SetPlayerLoop(loop);
    }

    static void Desinstalar()
    {
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        bool tocado = false;
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            var sub = loop.subSystemList[i].subSystemList;
            if (sub == null) continue;
            var lst = new List<PlayerLoopSystem>(sub);
            if (lst.RemoveAll(x => x.type == typeof(OmniGridLoop)) > 0)
            {
                loop.subSystemList[i].subSystemList = lst.ToArray();
                tocado = true;
            }
        }
        if (tocado) PlayerLoop.SetPlayerLoop(loop);
    }

#if UNITY_EDITOR
    // En el editor, Stop NO dispara Application.quitting → limpiar a mano.
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
