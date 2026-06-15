// Assets/Scripts/Systems/BootstrapMuestreadorAltura.cs
// ═══════════════════════════════════════════════════════════════════════════
//  BOOTSTRAP — Muestreador de Altura Mosaico V3 Fase 0 (opt-in)
//
//  Diseño: Docs/arquitectura_mosaico_v3.md §8 (fase 0)
//
//  MuestreadorAlturaMosaico es ADITIVO y caro (~126 MB RAM, todos los RAW del
//  Mosaico V2 cargados para decode bit-exacto). No se autoarranca: hace falta
//  un GameObject con ese componente en escena. Este bootstrap es ese punto
//  único de decisión — lo crea SceneBootstrapper (EnsureSistemasAssets) y, por
//  defecto, NO HACE NADA (activar=false): el juego sigue con
//  ITerrainService/TerrenoGlobal (Terrain.SampleHeight) como hasta ahora.
//
//  Para activar la precisión bit-exacta (Foot IK avanzado, spawn reproducible,
//  NavMesh local), marca `activar` en el Inspector del GameObject
//  "SistemasAssets" → BootstrapMuestreadorAltura.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public sealed class BootstrapMuestreadorAltura : MonoBehaviour
{
    [Tooltip("Activa el muestreador bit-exacto del Mosaico V3 Fase 0 (~126 MB RAM, " +
             "carga en background). OFF por defecto: el juego sigue usando " +
             "ITerrainService/TerrenoGlobal (Terrain.SampleHeight) sin cambios.")]
    // TEMPORAL (2026-06-15): activado a true para la primera prueba en Play de
    // esta sesión. Revertir a 'false' tras validar (ver Docs/arquitectura_mosaico_v3.md §8).
    [SerializeField] bool activar = true;

    void Start()
    {
        if (!activar)
        {
            AlsasuaLogger.Info("BootstrapAltura",
                "Muestreador Mosaico V3 Fase 0 desactivado (activar=false) — " +
                "usando TerrenoGlobal/ITerrainService.");
            return;
        }

        if (FindFirstObjectByType<MuestreadorAlturaMosaico>() != null) return;

        new GameObject("MuestreadorAlturaMosaico").AddComponent<MuestreadorAlturaMosaico>();
        AlsasuaLogger.Info("BootstrapAltura",
            "Muestreador Mosaico V3 Fase 0 activado — cargando RAW a RAM en background.");
    }
}
