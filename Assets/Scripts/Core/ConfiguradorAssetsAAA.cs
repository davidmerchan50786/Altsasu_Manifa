// Assets/Scripts/ConfiguradorAssetsAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CONFIGURADOR AAA+ — repositorio central de TODOS los assets del proyecto
//
//  Cada sistema consulta este singleton en Awake para obtener sus prefabs,
//  materiales, clips de audio y animaciones. Así ningún sistema tiene
//  dependencias hardcodeadas y el Inspector es la única fuente de verdad.
//
//  Poblado automáticamente por CreadorEscenaPrincipal.AsignarAssetsAAA()
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[DefaultExecutionOrder(-98)]   // justo después de AltsasuCore (-100)
public class ConfiguradorAssetsAAA : MonoBehaviour
{
    public static ConfiguradorAssetsAAA Instance { get; private set; }

    // ════════════════════════════════════════════════════════════════════
    //  ÁRBOLES — GreenForest
    // ════════════════════════════════════════════════════════════════════

    [Header("═ ÁRBOLES (GreenForest) ═")]
    [Tooltip("Robles grandes — parques y márgenes de camino")]
    public GameObject[] prefabsRoble;        // Oak2, Oak3

    [Tooltip("Pinos — laderas y periferia")]
    public GameObject[] prefabsPino;         // Pinetree2, Pinetree3

    [Tooltip("Arbustos — jardines y setos")]
    public GameObject[] prefabsArbusto;      // Bush, Bush2

    [Tooltip("Árboles secos / talados — detalle")]
    public GameObject[] prefabsArbolSeco;    // Oak2Dry, OakCut, CutedTree1

    [Tooltip("Rocas — terreno montañoso")]
    public GameObject[] prefabsRoca;         // Rock1-4, Stone1-14

    // ════════════════════════════════════════════════════════════════════
    //  NPCs — MeshyAI + XBot
    // ════════════════════════════════════════════════════════════════════

    [Header("═ NPCS CIVILES (MeshyAI) ═")]
    public GameObject[] prefabsCivil;        // Casual, CodeKicks, DenimOveralls, Doctor,
                                             // Executive, Pruu, SummerStreet, UrbanEdge
    [Header("═ NPCS CIVILES (Kenney crowd) ═")]
    public GameObject[] prefabsCivilKenney;  // NPC_Crowd_character-male/female-a..f (12 variantes)

    [Header("═ MANIFESTANTES ═")]
    public GameObject[] prefabsManifestante; // NPC_Manifestante, NPC_Jarrai (Personajes + skin)

    [Header("═ JARRAI ═")]
    public GameObject[] prefabsJarrai;       // NPC_Jarrai (jóvenes facción)

    [Header("═ GUARDIA CIVIL ═")]
    public GameObject[] prefabsGuardia;      // GuardiaCivil_01, GuardiaCivil_02, NPC_GuardiaCivil_K

    [Header("═ ANIMACIONES PELEA (FightingMotionsVolume1) ═")]
    public AnimationClip animPunetazo;       // hp_straight_right_A2
    public AnimationClip animPatada;         // hk_side_left_A2
    public AnimationClip animCaida;          // knockdown_A2
    public AnimationClip animBloqueo;        // bp_hook_right_A2

    // ════════════════════════════════════════════════════════════════════
    //  VEHÍCULOS
    // ════════════════════════════════════════════════════════════════════

    [Header("═ VEHÍCULOS ═")]
    public GameObject prefabCocheCivil;      // Trabant_Jugador (tráfico)
    public GameObject prefabCocheRetro;      // Simple Retro Car (Polyeler)
    public GameObject prefabTractor;         // Tractor_Rojo (rural/protesta)
    public GameObject prefabAmbulancia;      // Ambulancia_Militar
    public GameObject prefabApisonadora;     // Camion_Apisonadora (corte de calles)
    public GameObject prefabCocheAbandonado; // Coche_Abandonado_Prop (prop estático)
    public GameObject prefabJugador;         // Interceptor_Jugador

    // ════════════════════════════════════════════════════════════════════
    //  FAUNA
    // ════════════════════════════════════════════════════════════════════

    [Header("═ FAUNA ═")]
    public GameObject prefabLobo;            // Wolf_HDRP
    public GameObject prefabPerro;           // P_GermanShepherd

    // ════════════════════════════════════════════════════════════════════
    //  EFECTOS — Explosiones y Fuego
    // ════════════════════════════════════════════════════════════════════

    [Header("═ EXPLOSIONES (HQ Explosions Pack) ═")]
    public GameObject[] prefabsExplosion;    // Explosion01-06
    public GameObject prefabExplosionAire;   // Explosion_Air 1

    [Header("═ FUEGO (LiteFireEffect) ═")]
    public GameObject prefabFuego;           // BaseFire000
    public GameObject prefabFuegoGrande;     // BaseFire001
    public GameObject prefabMolotov;         // FireSkill001

    [Header("═ PROYECTILES ═")]
    public GameObject prefabRPG;             // RPG_Rocket
    public GameObject prefabGranada;         // Grenade
    public GameObject prefabMisil;           // Missile (si existe)
    public GameObject prefabRocket;          // H70 Rocket

    // ════════════════════════════════════════════════════════════════════
    //  AUDIO — Nature Sounds Pack + efectos
    // ════════════════════════════════════════════════════════════════════

    [Header("═ AUDIO AMBIENTE ═")]
    public AudioClip ambientePajaros;        // AmbientNatureBirdsWater01
    public AudioClip ambienteNocheRain;      // AmbientNatureNightRainy
    public AudioClip ambienteExterior;       // AmbientNatureOutside
    public AudioClip ambienteTormenta;       // AmbientNatureStormRainThunder
    public AudioClip ambienteTormenta2;      // AmbientNatureStormRainThunder02

    [Header("═ AUDIO PASOS ═")]
    public AudioClip[] pasosHierba;          // FootstepGrass01-09
    public AudioClip[] pasosFoliage;         // Foliage01-06

    [Header("═ AUDIO AGUA ═")]
    public AudioClip audioRio;               // Stream

    [Header("═ AUDIO ARMAS ═")]
    public AudioClip sfxDisparo;             // M4 fire
    public AudioClip sfxRecarga;             // m4_reload
    public AudioClip sfxExplosion;           // explosion_1
    public AudioClip sfxRocket;              // Rocket

    // ════════════════════════════════════════════════════════════════════
    //  PROPS URBANOS — Polygon City Free Pack (Wand & Circles)
    // ════════════════════════════════════════════════════════════════════

    [Header("═ PROPS URBANOS (Polygon City) ═")]
    public GameObject prefabGaraje;          // Garage walls+roof
    public GameObject prefabHangar;          // Little angar
    public GameObject prefabGarajeGrande;    // Big Garage

    // ════════════════════════════════════════════════════════════════════
    //  BULLET HOLES / IMPACTOS
    // ════════════════════════════════════════════════════════════════════

    [Header("═ IMPACTOS ═")]
    public GameObject prefabImpactoHormigon; // DirtHole
    public GameObject prefabImpactoMetal;    // MetalHole
    public GameObject prefabImpactoMadera;   // WoodHole
    public GameObject prefabSangre;          // Blood Hole

    // ════════════════════════════════════════════════════════════════════
    //  BOOT
    // ════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    // ── API helpers ───────────────────────────────────────────────────

    /// Devuelve un prefab de árbol adecuado para el radio de copa dado.
    public GameObject GetPrefabArbol(float radio, float altura)
    {
        // Pinos en laderas (altura > 15m), robles en zonas urbanas, arbustos pequeños
        if (radio < 2f && prefabsArbusto?.Length > 0)
            return prefabsArbusto[Random.Range(0, prefabsArbusto.Length)];
        if (altura > 15f && prefabsPino?.Length > 0)
            return prefabsPino[Random.Range(0, prefabsPino.Length)];
        if (prefabsRoble?.Length > 0)
            return prefabsRoble[Random.Range(0, prefabsRoble.Length)];
        return null;
    }

    /// Devuelve un prefab de NPC civil aleatorio.
    public GameObject GetPrefabCivil()
        => prefabsCivil?.Length > 0
            ? prefabsCivil[Random.Range(0, prefabsCivil.Length)]
            : null;

    /// Devuelve una explosión por nivel de intensidad (0=pequeña … 5=grande).
    public GameObject GetExplosion(int nivel = 2)
        => prefabsExplosion?.Length > 0
            ? prefabsExplosion[Mathf.Clamp(nivel, 0, prefabsExplosion.Length - 1)]
            : null;

    /// AudioClip de ambiente según el clima.
    public AudioClip GetAmbienteClima(bool lluvia, bool tormenta)
    {
        if (tormenta) return ambienteTormenta ?? ambienteTormenta2;
        if (lluvia)   return ambienteNocheRain;
        return ambientePajaros ?? ambienteExterior;
    }

    /// AudioClip de paso según la superficie.
    public AudioClip GetPaso(string superficie)
    {
        if (pasosHierba?.Length > 0 &&
            (superficie == "grass" || superficie == "dirt" || superficie == ""))
            return pasosHierba[Random.Range(0, pasosHierba.Length)];
        return null;
    }
}
