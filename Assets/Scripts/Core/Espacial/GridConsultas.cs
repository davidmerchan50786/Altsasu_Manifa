// Assets/Scripts/Core/Espacial/GridConsultas.cs
// ═══════════════════════════════════════════════════════════════════════════
//  OMNI-GRID — WRAPPER ERGONÓMICO (capa CORE)
//
//  Para los ~140 call-sites de OverlapSphere/Vector3.Distance que viven en
//  MonoBehaviours y no quieren tocar Jobs ni NativeContainers. Hilo principal.
//
//  Reemplazo típico:
//    // antes:
//    foreach (var c in Physics.OverlapSphere(p, r)) if (c.CompareTag("Policia")) ...
//    // después:
//    GridConsultas.Vecinos(p, r, TipoEspacial.Policia, _ids);   // _ids = List<int> reutilizada
//
//  El List<int> de salida son IDs OPACOS: cada sistema los resuelve en SU array.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public static class GridConsultas
{
    static NativeList<int> _buf;

    static NativeList<int> Buffer()
    {
        if (!_buf.IsCreated) _buf = new NativeList<int>(64, Allocator.Persistent);
        return _buf;
    }

    /// <summary>Llena `salida` con los IDs de entidades del tipo dado en el radio.
    /// Devuelve cuántas hay. Si el grid aún no está listo, salida queda vacía y devuelve 0.</summary>
    public static int Vecinos(Vector3 p, float radio, TipoEspacial tipo, List<int> salida)
    {
        salida.Clear();
        var g = ServiceLocator.Get<IUnifiedSpatialGrid>();
        if (g == null || !g.Listo) return 0;

        var buf = Buffer();
        g.QueryRadio(p, radio, tipo, buf);
        for (int i = 0; i < buf.Length; i++) salida.Add(buf[i]);
        return buf.Length;
    }

    /// <summary>Sólo cuenta vecinos del tipo dado en el radio (sin asignar lista).</summary>
    public static int Contar(Vector3 p, float radio, TipoEspacial tipo)
    {
        var g = ServiceLocator.Get<IUnifiedSpatialGrid>();
        return (g != null && g.Listo) ? g.QueryRadioContar(p, radio, tipo) : 0;
    }

    /// <summary>True si hay al menos un vecino del tipo en el radio (corta antes con count>0).</summary>
    public static bool Hay(Vector3 p, float radio, TipoEspacial tipo) => Contar(p, radio, tipo) > 0;

    /// <summary>Libera el buffer nativo. Lo llama OmniGridLoop al salir de Play.</summary>
    public static void Liberar()
    {
        if (_buf.IsCreated) _buf.Dispose();
    }
}
