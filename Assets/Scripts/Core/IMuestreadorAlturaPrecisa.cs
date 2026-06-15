// Assets/Scripts/Core/IMuestreadorAlturaPrecisa.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MUESTREADOR DE ALTURA PRECISA — contrato Core
//
//  Diseño completo: Docs/arquitectura_mosaico_v3.md §1
//
//  Objetivo: separar la altura matemática del suelo (gameplay, IA, spawn,
//  NavMesh, árboles, Foot IK) de la presencia del componente Terrain.
//
//  Hoy `ITerrainService.AlturaMundo` resuelve por `Terrain.SampleHeight`
//  (tile-aware en ServicioTerreno). Funciona pero:
//    · Requiere que exista un Terrain (no compatible con Mosaico V3 GPU-driven).
//    · Es más caro que un muestreo directo del RAW en RAM (recorrido API + IL).
//    · Devuelve la altura del LOD activo, no la bit-exacta del lattice.
//
//  IMuestreadorAlturaPrecisa expone alturas matemáticas leídas directamente
//  del RAW en RAM con el decode lattice 1/64 → bit-exactas con el bake y con
//  el gate Python (`ValidarMosaicoV2.py`).
//
//  Es ADITIVO: no sustituye `ITerrainService.AlturaMundo` (que sigue siendo
//  la entrada por defecto). Quien quiera precisión bit-exacta lo pide
//  explícitamente: `ServiceLocator.Get<IMuestreadorAlturaPrecisa>()?.AlturaMundo(p)`.
//  Si el servicio no está, el caller usa `ITerrainService` como fallback.
//
//  Implementación: MuestreadorAlturaMosaico (capa Systems).
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public interface IMuestreadorAlturaPrecisa
{
    /// <summary>True cuando los RAWs están cargados y el muestreo es válido.</summary>
    bool Listo { get; }

    /// <summary>Altura del suelo (Y mundo) bajo (x,z) en metros Unity, leída del
    /// RAW lattice 1/64 con muestreo bilineal. Bit-exacta con el bake.
    /// Si la posición cae fuera del mosaico, devuelve `datumYBase`
    /// (cota base del mundo en Unity, = 0 con la convención de Alsasua).</summary>
    float AlturaMundo(float x, float z);

    /// <summary>Atajo Vector3 → AlturaMundo(p.x, p.z). La Y de p se ignora.</summary>
    float AlturaMundo(Vector3 posicionMundo);

    /// <summary>Normal del suelo en (x,z) — diferencias finitas sobre el lattice.
    /// Útil para Foot IK avanzado / spine bending sin raycast.</summary>
    Vector3 NormalMundo(float x, float z);
}
