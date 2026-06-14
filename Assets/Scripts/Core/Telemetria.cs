// Assets/Scripts/Core/Telemetria.cs
// ═══════════════════════════════════════════════════════════════════════════
//  TELEMETRÍA — sink ligero para herramientas de auditoría (capa CORE)
//
//  Los sistemas (ServicioTerreno/CargadorMosaicoTerreno…) empujan muestras; las
//  herramientas de editor (VisualizadorHeatmap) las leen. Escrituras O(1) y
//  acotadas: NO es un hot-path (el cosido de costuras ocurre una vez por tile al
//  cargar), así que el coste es despreciable y no hace falta compilarlo fuera.
//
//  La VISUALIZACIÓN sí es por frame y vive bajo #if UNITY_EDITOR → cero impacto
//  en el build de release.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;

public static class Telemetria
{
    /// <summary>Coste de "coser" (SetHeights del lattice) un tile del mosaico.</summary>
    public struct Costura
    {
        public Vector3 centro;   // centro mundo del tile
        public float   lado;     // ancho del tile (m)
        public float   ms;       // tiempo SÍNCRONO de cosido (sin contar yields)
    }

    static readonly List<Costura> _costuras = new(64);

    public static IReadOnlyList<Costura> Costuras => _costuras;
    public static float PeorCosturaMs { get; private set; }

    /// <summary>Registra (upsert por tile) el tiempo de cosido de un tile.</summary>
    public static void RegistrarCostura(Vector3 centro, float lado, float ms)
    {
        for (int i = 0; i < _costuras.Count; i++)
            if ((_costuras[i].centro - centro).sqrMagnitude < 1f)   // mismo tile
            {
                _costuras[i] = new Costura { centro = centro, lado = lado, ms = ms };
                RecalcularPeor();
                return;
            }
        _costuras.Add(new Costura { centro = centro, lado = lado, ms = ms });
        RecalcularPeor();
    }

    static void RecalcularPeor()
    {
        float m = 0f;
        for (int i = 0; i < _costuras.Count; i++) if (_costuras[i].ms > m) m = _costuras[i].ms;
        PeorCosturaMs = m;
    }

    public static void Limpiar() { _costuras.Clear(); PeorCosturaMs = 0f; }
}
