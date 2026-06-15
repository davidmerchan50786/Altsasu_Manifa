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
    [Tooltip("Activa el muestreador de altura bit-exacto (Mosaico V3 Fase 0): carga el RAW " +
             "lattice 1/64 en RAM (~126 MB en background) y, si AUTO-VALIDA contra la cota " +
             "de Herriko Plaza, pasa a ser la fuente de altura de TODO el juego (más preciso " +
             "y determinista que Terrain.SampleHeight). Si la validación falla, se " +
             "auto-desactiva y el juego sigue con ITerrainService (fallback seguro).")]
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
