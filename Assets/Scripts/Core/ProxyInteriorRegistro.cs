// Assets/Scripts/Core/ProxyInteriorRegistro.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PROXY DE INTERIOR — registro de ventanas (capa CORE)
//
//  SistemaEdificiosAAA (Runtime) registra aquí cada cristal de ventana al
//  generarlo. El gobernador ProxyInterior (Systems) lo lee para decidir, por
//  MIRADA + PRESUPUESTO, a qué ventanas activar el shader de "falso interior"
//  (Altsasu/InteriorMapping) — la capacidad que faltaba sobre los generadores
//  de interiores por radio ya existentes (GeneradorInterioresAAA/Simples).
//
//  Vive en Core porque Runtime (que registra) no puede ver Systems (que gobierna).
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;

/// <summary>Una ventana candidata a recibir parallax interior.</summary>
public sealed class VentanaProxy
{
    public Renderer cristal;       // el MeshRenderer del "Vidrio"
    public Vector3  centro;        // posición mundo
    public Vector3  normal;        // hacia fuera de la fachada (rot * forward)
    public bool     esPlantaBaja;

    // Estado runtime — lo gestiona EXCLUSIVAMENTE ProxyInterior.
    public Material matOriginal;   // cristal liso, para revertir
    public int      tier;          // 0 = liso, 1 = parallax
    public float    score;
}

/// <summary>Registro global de ventanas. Lista pública por pragmatismo (cero GC al escanear).</summary>
public static class ProxyInteriorRegistro
{
    public static readonly List<VentanaProxy> Ventanas = new(2048);

    // Cristales que el Proxy tiene AHORA en parallax. Lo mantiene ProxyInterior.
    // SistemaEdificiosAAA lo consulta para NO pisar su MPB en el bucle nocturno.
    public static readonly HashSet<Renderer> ParallaxActivas = new();

    /// <summary>True si ese cristal está bajo control del parallax del Proxy (no tocar su MPB).</summary>
    public static bool EsParallaxActivo(Renderer r) => r != null && ParallaxActivas.Contains(r);

    public static void Registrar(Renderer cristal, Vector3 centro, Vector3 normal, bool esPlantaBaja)
    {
        if (cristal == null) return;
        Ventanas.Add(new VentanaProxy
        {
            cristal = cristal, centro = centro, normal = normal, esPlantaBaja = esPlantaBaja
        });
    }

    /// <summary>Vaciar (cambio de escena / nueva partida).</summary>
    public static void Limpiar() { Ventanas.Clear(); ParallaxActivas.Clear(); }
}
