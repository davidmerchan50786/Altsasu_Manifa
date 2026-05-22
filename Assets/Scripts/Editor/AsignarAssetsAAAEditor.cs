// Assets/Scripts/Editor/AsignarAssetsAAAEditor.cs
// Tools → Alsasua → ⭐ Asignar Todos los Assets AAA+

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public static class AsignarAssetsAAAEditor
{
    /// Llamado desde CreadorEscenaPrincipal al construir la escena.
    public static void AsignarDesdeCreador(ConfiguradorAssetsAAA cfg)
    {
        if (cfg == null) return;
        AsignarArboles(cfg);
        AsignarNPCs(cfg);
        AsignarVehiculos(cfg);
        AsignarFauna(cfg);
        AsignarEfectos(cfg);
        AsignarAudio(cfg);
        AsignarProps(cfg);
        Debug.Log($"[AAA] ConfiguradorAssetsAAA poblado desde CreadorEscenaPrincipal");
    }

    [MenuItem("Tools/Alsasua/⭐ Asignar Todos los Assets AAA+", priority = 1)]
    public static void AsignarTodo()
    {
        var cfg = FindOrCreateConfigurador();
        if (cfg == null)
        {
            EditorUtility.DisplayDialog("Error",
                "No se encontró ConfiguradorAssetsAAA en la escena.\n" +
                "Ejecuta primero:\nTools → Alsasua → Escena → 🎬 Crear Escena Principal", "OK");
            return;
        }

        EditorUtility.DisplayProgressBar("⭐ Assets AAA+", "Asignando...", 0f);

        try
        {
            AsignarArboles(cfg);
            EditorUtility.DisplayProgressBar("⭐ Assets AAA+", "NPCs...", 0.2f);
            AsignarNPCs(cfg);
            EditorUtility.DisplayProgressBar("⭐ Assets AAA+", "Vehículos...", 0.35f);
            AsignarVehiculos(cfg);
            EditorUtility.DisplayProgressBar("⭐ Assets AAA+", "Fauna...", 0.45f);
            AsignarFauna(cfg);
            EditorUtility.DisplayProgressBar("⭐ Assets AAA+", "Explosiones y fuego...", 0.55f);
            AsignarEfectos(cfg);
            EditorUtility.DisplayProgressBar("⭐ Assets AAA+", "Audio...", 0.65f);
            AsignarAudio(cfg);
            EditorUtility.DisplayProgressBar("⭐ Assets AAA+", "Props urbanos...", 0.75f);
            AsignarProps(cfg);
            EditorUtility.DisplayProgressBar("⭐ Assets AAA+", "Conectando sistemas...", 0.85f);
            ConectarSistemas(cfg);
            EditorUtility.DisplayProgressBar("⭐ Assets AAA+", "Materiales y texturas...", 0.92f);
            AsignarMaterialesAAA();

            EditorUtility.SetDirty(cfg.gameObject);
            AssetDatabase.SaveAssets();
            EditorUtility.ClearProgressBar();

            // Contar asignaciones
            int asignados = ContarAsignaciones(cfg);
            EditorUtility.DisplayDialog("✅ Assets AAA+ asignados",
                $"Se han conectado {asignados} assets al proyecto:\n\n" +
                $"🌳 Árboles GreenForest: {(cfg.prefabsRoble?.Length??0) + (cfg.prefabsPino?.Length??0) + (cfg.prefabsArbusto?.Length??0)}\n" +
                $"🧍 NPCs MeshyAI: {cfg.prefabsCivil?.Length??0} civiles + {cfg.prefabsGuardia?.Length??0} guardias\n" +
                $"🚗 Vehículos: {ContarVehiculos(cfg)}\n" +
                $"🐺 Fauna: {(cfg.prefabLobo != null ? 1 : 0) + (cfg.prefabPerro != null ? 1 : 0)}\n" +
                $"💥 Explosiones: {cfg.prefabsExplosion?.Length??0}\n" +
                $"🔊 AudioClips: {ContarAudio(cfg)}\n\n" +
                $"Dale Play para ver todo en acción.", "OK");
        }
        catch (System.Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"[AssetsAAA] Error: {e.Message}\n{e.StackTrace}");
            EditorUtility.DisplayDialog("Error", e.Message, "OK");
        }
    }

    // ── Árbol helpers ─────────────────────────────────────────────────

    static void AsignarArboles(ConfiguradorAssetsAAA cfg)
    {
        string p = "Assets/GreenForest/Prefabs/";
        cfg.prefabsRoble = CargarPrefabs(new[] {
            p+"Oak2.prefab", p+"Oak3.prefab" });
        cfg.prefabsPino = CargarPrefabs(new[] {
            p+"Pinetree2.prefab", p+"Pinetree3.prefab" });
        cfg.prefabsArbusto = CargarPrefabs(new[] {
            p+"Bush.prefab", p+"Bush2.prefab" });
        cfg.prefabsArbolSeco = CargarPrefabs(new[] {
            p+"Oak2Dry.prefab", p+"OakCut.prefab", p+"CutedTree1.prefab" });
        cfg.prefabsRoca = CargarPrefabs(new[] {
            p+"Rock1.prefab", p+"Rock2.prefab", p+"Rock4.prefab",
            p+"Stone1.prefab", p+"Stone2.prefab", p+"Stone3.prefab",
            p+"Stone4.prefab", p+"Stone9.prefab" });
        Debug.Log($"[AAA] Árboles GreenForest: {(cfg.prefabsRoble?.Length??0) + (cfg.prefabsPino?.Length??0) + (cfg.prefabsArbusto?.Length??0)} prefabs");
    }

    static void AsignarNPCs(ConfiguradorAssetsAAA cfg)
    {
        string p = "Assets/Prefabs/NPCs/";
        cfg.prefabsCivil = CargarPrefabs(new[] {
            p+"NPC_Civil_Casual.prefab",
            p+"NPC_Civil_CodeKicks.prefab",
            p+"NPC_Civil_DenimOveralls.prefab",
            p+"NPC_Civil_Doctor.prefab",
            p+"NPC_Civil_Executive.prefab",
            p+"NPC_Civil_Pruu.prefab",
            p+"NPC_Civil_SummerStreet.prefab",
            p+"NPC_Civil_UrbanEdge.prefab" });
        cfg.prefabsGuardia = CargarPrefabs(new[] {
            p+"NPC_GuardiaCivil_01.prefab",
            p+"NPC_GuardiaCivil_02.prefab" });

        // Animaciones de pelea
        string fa = "Assets/FightingMotionsVolume1/FBX/nomove/";
        cfg.animPunetazo = Load<AnimationClip>(fa + "hp_straight_right_A2.fbx");
        cfg.animPatada   = Load<AnimationClip>(fa + "hk_side_left_A2.fbx");
        cfg.animCaida    = Load<AnimationClip>(fa + "knockdown_A2.fbx");
        cfg.animBloqueo  = Load<AnimationClip>(fa + "bp_hook_right_A2.fbx");

        Debug.Log($"[AAA] NPCs: {cfg.prefabsCivil?.Length??0} civiles + {cfg.prefabsGuardia?.Length??0} guardias");
    }

    static void AsignarVehiculos(ConfiguradorAssetsAAA cfg)
    {
        string p = "Assets/Prefabs/Coches/";
        cfg.prefabCocheCivil     = Load<GameObject>(p + "Trabant_Jugador.prefab");
        cfg.prefabCocheRetro     = Load<GameObject>("Assets/Polyeler/Simple Retro Car/Prefabs/Simple Retro Car.prefab");
        cfg.prefabTractor        = Load<GameObject>(p + "Tractor_Rojo.prefab");
        cfg.prefabAmbulancia     = Load<GameObject>(p + "Ambulancia_Militar.prefab");
        cfg.prefabApisonadora    = Load<GameObject>(p + "Camion_Apisonadora.prefab");
        cfg.prefabCocheAbandonado= Load<GameObject>(p + "Coche_Abandonado_Prop.prefab");
        cfg.prefabJugador        = Load<GameObject>(p + "Interceptor_Jugador.prefab");
        Debug.Log($"[AAA] Vehículos: {ContarVehiculos(cfg)}");
    }

    static void AsignarFauna(ConfiguradorAssetsAAA cfg)
    {
        cfg.prefabLobo  = Load<GameObject>("Assets/Wolf/HDRP/Wolf/Prefab/Wolf_HDRP.prefab");
        cfg.prefabPerro = Load<GameObject>("Assets/RSG_DogsPack/HDRP/Prefabs/P_GermanShepherd.prefab");
        Debug.Log($"[AAA] Fauna: Lobo={cfg.prefabLobo!=null} Perro={cfg.prefabPerro!=null}");
    }

    static void AsignarEfectos(ConfiguradorAssetsAAA cfg)
    {
        string he = "Assets/HQ Explosions Pack FREE/Prefabs/";
        cfg.prefabsExplosion = CargarPrefabs(new[] {
            he+"Explosion01.prefab", he+"Explosion02.prefab", he+"Explosion03.prefab",
            he+"Explosion04.prefab", he+"Explosion05.prefab", he+"Explosion06.prefab" });
        cfg.prefabExplosionAire  = Load<GameObject>("Assets/#Xtra/Prefabs/Explosion_Air 1.prefab");
        cfg.prefabFuego          = Load<GameObject>("Assets/LiteFireEffect/Prafab/BaseFire000.prefab");
        cfg.prefabFuegoGrande    = Load<GameObject>("Assets/LiteFireEffect/Prafab/BaseFire001.prefab");
        cfg.prefabMolotov        = Load<GameObject>("Assets/LiteFireEffect/Prafab/FireSkill001.prefab");
        cfg.prefabRPG            = Load<GameObject>("Assets/#Xtra/Prefabs/RPG_Rocket.prefab");
        cfg.prefabGranada        = Load<GameObject>("Assets/#Xtra/Prefabs/Grenade.prefab");

        // Impactos
        string bi = "Assets/#Tools/Resources/Particles/BulletHoles/";
        cfg.prefabImpactoHormigon = Load<GameObject>(bi + "DirtHole.prefab");
        cfg.prefabImpactoMetal    = Load<GameObject>(bi + "MetalHole.prefab");
        cfg.prefabImpactoMadera   = Load<GameObject>(bi + "WoodHole.prefab");
        cfg.prefabSangre          = Load<GameObject>(bi + "Blood Hole.prefab");
        Debug.Log($"[AAA] Explosiones: {cfg.prefabsExplosion?.Length??0} + fuego + RPG");
    }

    static void AsignarAudio(ConfiguradorAssetsAAA cfg)
    {
        string n = "Assets/Nature Sounds Pack/";
        cfg.ambientePajaros  = Load<AudioClip>(n + "Ambient/AmbientNatureBirdsWater01.wav");
        cfg.ambienteNocheRain= Load<AudioClip>(n + "Ambient/AmbientNatureNightRainy.wav");
        cfg.ambienteExterior = Load<AudioClip>(n + "Ambient/AmbientNatureOutside.wav");
        cfg.ambienteTormenta = Load<AudioClip>(n + "Ambient/AmbientNatureStormRainThunder.wav");
        cfg.ambienteTormenta2= Load<AudioClip>(n + "Ambient/AmbientNatureStormRainThunder02.wav");
        cfg.audioRio         = Load<AudioClip>(n + "Stream/Stream.wav");

        var pasos = new List<AudioClip>();
        for (int i = 1; i <= 9; i++)
        {
            var c = Load<AudioClip>($"{n}Footstep/FootstepGrass{i:D2}.wav");
            if (c != null) pasos.Add(c);
        }
        cfg.pasosHierba = pasos.ToArray();

        var foliage = new List<AudioClip>();
        for (int i = 1; i <= 6; i++)
        {
            var c = Load<AudioClip>($"{n}Foliage/Foliage{i:D2}.wav");
            if (c != null) foliage.Add(c);
        }
        cfg.pasosFoliage = foliage.ToArray();

        // Armas
        cfg.sfxDisparo   = Load<AudioClip>("Assets/#Tools/Resources/Sounds/Weapons/M4 fire.wav");
        cfg.sfxRecarga   = Load<AudioClip>("Assets/#Sounds/m4_reload.mp3");
        cfg.sfxExplosion = Load<AudioClip>("Assets/#Tools/Resources/Sounds/Weapons/explosion_1.wav");
        cfg.sfxRocket    = Load<AudioClip>("Assets/#Tools/Resources/Sounds/Rocket.mp3");

        Debug.Log($"[AAA] Audio: {pasos.Count} pasos + {foliage.Count} foliage + 4 SFX + ambiente");
    }

    static void AsignarProps(ConfiguradorAssetsAAA cfg)
    {
        string wc = "Assets/Wand and Circles/Polygon City Free Pack - Environment and Interior/Prefabs/Buildings/";
        // Cargar grupos como combinación de Foundation+Walls+Roof → usar sólo Walls como referencia visual
        cfg.prefabGaraje      = Load<GameObject>(wc + "Garage/Walls.prefab");
        cfg.prefabHangar      = Load<GameObject>(wc + "Little angar/Walls.prefab");
        cfg.prefabGarajeGrande= Load<GameObject>(wc + "Big Garage/Walls.prefab");
        Debug.Log($"[AAA] Props Polygon City: {(cfg.prefabGaraje!=null?1:0)+(cfg.prefabHangar!=null?1:0)+(cfg.prefabGarajeGrande!=null?1:0)}");
    }

    // ── Conectar sistemas con los assets ─────────────────────────────

    static void ConectarSistemas(ConfiguradorAssetsAAA cfg)
    {
        // PosicionadorPrecisionUrbana ← árboles GreenForest
        var pos = FindComponent<PosicionadorPrecisionUrbana>();
        if (pos != null)
        {
            // El primer árbol disponible como fallback
            pos.prefabArbol = cfg.prefabsRoble?.Length > 0 ? cfg.prefabsRoble[0] : null;
            EditorUtility.SetDirty(pos);
        }

        // SistemaTrafico ← vehículos civiles
        var trafico = FindComponent<SistemaTrafico>();
        if (trafico != null)
        {
            SetField(trafico, "prefabCoche",  cfg.prefabCocheCivil ?? cfg.prefabCocheRetro);
            SetField(trafico, "prefabCamion", cfg.prefabTractor ?? cfg.prefabApisonadora);
            EditorUtility.SetDirty(trafico);
        }

        // SistemaFauna ← fauna
        var fauna = FindComponent<SistemaFauna>();
        if (fauna != null)
        {
            SetField(fauna, "prefabLobo",  cfg.prefabLobo);
            SetField(fauna, "prefabPerro", cfg.prefabPerro);
            EditorUtility.SetDirty(fauna);
        }

        // SistemaExplosion ← prefabs explosión
        var explo = FindComponent<SistemaExplosion>();
        if (explo != null && cfg.prefabsExplosion?.Length > 0)
        {
            SetField(explo, "prefabExplosionPequena",  cfg.prefabsExplosion[0]);
            SetField(explo, "prefabExplosionMediana",  cfg.prefabsExplosion[2]);
            SetField(explo, "prefabExplosionGrande",   cfg.prefabsExplosion[4]);
            SetField(explo, "prefabFuego",             cfg.prefabFuego);
            EditorUtility.SetDirty(explo);
        }

        // SistemaImpactos ← bullet holes
        var impactos = FindComponent<SistemaImpactos>();
        if (impactos != null)
        {
            SetField(impactos, "prefabImpactoHormigon", cfg.prefabImpactoHormigon);
            SetField(impactos, "prefabImpactoMetal",    cfg.prefabImpactoMetal);
            SetField(impactos, "prefabImpactoMadera",   cfg.prefabImpactoMadera);
            SetField(impactos, "prefabSangre",          cfg.prefabSangre);
            EditorUtility.SetDirty(impactos);
        }

        // SistemaAtmosfera ← sonidos ambiente
        var atmos = FindComponent<SistemaAtmosfera>();
        if (atmos != null)
        {
            SetField(atmos, "audioAmbiente",     cfg.ambientePajaros);
            SetField(atmos, "audioAmbienteLluvia", cfg.ambienteNocheRain);
            SetField(atmos, "audioAmbienteTormenta", cfg.ambienteTormenta);
            EditorUtility.SetDirty(atmos);
        }

        // SistemaMultitud ← prefabs NPCs civiles
        var multitud = FindComponent<SistemaMultitud>();
        if (multitud != null && cfg.prefabsCivil?.Length > 0)
        {
            SetField(multitud, "prefabsNPC",     cfg.prefabsCivil);
            SetField(multitud, "prefabNPC",      cfg.prefabsCivil[0]);
            EditorUtility.SetDirty(multitud);
        }

        // SistemaManifestacion ← prefab civil como manifestante base
        // (usa prefabManifestante singular, no array)
        var manif = FindComponent<SistemaManifestacion>();
        if (manif != null && cfg.prefabsCivil?.Length > 0)
        {
            SetField(manif, "prefabManifestante", cfg.prefabsCivil[0]);
            // GuardiaCivil
            // No hay campo prefabGuardiaCivil en SistemaManifestacion todavía (se añadirá si es necesario)
            EditorUtility.SetDirty(manif);
        }

        // ControladorJugador — los pasos se obtienen en runtime vía
        // ConfiguradorAssetsAAA.Instance.pasosHierba, no hay campo serializado

        // SistemaBombas ← projectiles
        var bombas = FindComponent<SistemaBombas>();
        if (bombas != null)
        {
            SetField(bombas, "prefabRPG",      cfg.prefabRPG);
            SetField(bombas, "prefabGranada",  cfg.prefabGranada);
            EditorUtility.SetDirty(bombas);
        }

        Debug.Log("[AAA] Sistemas conectados con assets");
    }

    // ── Asignar materiales PBR al GestorMaterialesAlsasua ────────────

    static void AsignarMaterialesAAA()
    {
        var gm = FindComponent<GestorMaterialesAlsasua>();
        if (gm == null)
        {
            Debug.LogWarning("[AAA] GestorMaterialesAlsasua no encontrado");
            return;
        }
        // Reutilizar el método ya implementado en CreadorEscenaPrincipal
        CreadorEscenaPrincipal.AsignarMaterialesAlGestorPublic(gm);
        EditorUtility.SetDirty(gm);
    }

    // ── Utilidades ────────────────────────────────────────────────────

    static T Load<T>(string path) where T : Object
        => AssetDatabase.LoadAssetAtPath<T>(path);

    static GameObject[] CargarPrefabs(string[] paths)
    {
        var lista = new List<GameObject>();
        foreach (var p in paths)
        {
            var go = Load<GameObject>(p);
            if (go != null) lista.Add(go);
            else Debug.LogWarning($"[AAA] Prefab no encontrado: {p}");
        }
        return lista.Count > 0 ? lista.ToArray() : null;
    }

    static void SetField(object target, string campo, object valor)
    {
        if (target == null || valor == null) return;
        var f = target.GetType().GetField(campo,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
        f?.SetValue(target, valor);
    }

    static T FindComponent<T>() where T : Component
        => Object.FindFirstObjectByType<T>();

    static ConfiguradorAssetsAAA FindOrCreateConfigurador()
    {
        var cfg = Object.FindFirstObjectByType<ConfiguradorAssetsAAA>();
        if (cfg != null) return cfg;
        // Buscar en el GO de la escena
        var go = GameObject.Find("AltsasuCore") ?? GameObject.Find("GameManager");
        if (go == null) return null;
        return go.AddComponent<ConfiguradorAssetsAAA>();
    }

    static int ContarAsignaciones(ConfiguradorAssetsAAA cfg)
    {
        int n = 0;
        foreach (var f in typeof(ConfiguradorAssetsAAA).GetFields(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            var v = f.GetValue(cfg);
            if (v != null) n++;
        }
        return n;
    }

    static int ContarVehiculos(ConfiguradorAssetsAAA cfg) =>
        (cfg.prefabCocheCivil != null ? 1 : 0) + (cfg.prefabCocheRetro != null ? 1 : 0) +
        (cfg.prefabTractor != null ? 1 : 0) + (cfg.prefabAmbulancia != null ? 1 : 0) +
        (cfg.prefabApisonadora != null ? 1 : 0) + (cfg.prefabCocheAbandonado != null ? 1 : 0);

    static int ContarAudio(ConfiguradorAssetsAAA cfg) =>
        (cfg.pasosHierba?.Length ?? 0) + (cfg.pasosFoliage?.Length ?? 0) + 6;
}
#endif
