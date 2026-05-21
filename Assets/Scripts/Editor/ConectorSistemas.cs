// Assets/Scripts/Editor/ConectorSistemas.cs
// Tools → Alsasua → 🔌 Conectar Todos los Sistemas
//
// Asigna CADA asset importado al campo exacto del sistema que lo usa.
// Cubre todos los slots SerializeField del proyecto:
//
//  GameManagerAltsasua   — coche policía, helicóptero, enemigo, árboles
//  SistemaDestruccion    — fuego grande/medio/chico, humo, explosión, audio
//  SistemaArmasExtendido — piedra, molotov, bomba lapa
//  SistemaManifestacion  — manifestante, encapuchado, barricada, fuego, audio
//  SistemaClima          — clips lluvia, trueno, viento
//  SistemaFauna          — perro, lobo, caballo, ciervo, oveja, conejo
//  SistemaVegetacion     — materiales árbol
//  AlsasuaTreeStreamer   — prefabs árbol
//  NPCCivil (prefabs)    — modelos humanoides
//  VehiculoNPC (prefabs) — modelos de coche
//  SistemaMultitud       — NPC manifestantes
//  CreadorEscenaPrincipal— wiring de la escena

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

public static class ConectorSistemas
{
    [MenuItem("Tools/Alsasua/Conectar/🔌 Conectar Todos los Sistemas", priority = 10)]
    public static void ConectarTodo()
    {
        AssetDatabase.Refresh();
        int total = 0;

        EditorUtility.DisplayProgressBar("🔌 Conectando sistemas", "Buscando assets...", 0f);
        try
        {
            // Caché global de assets para no buscar varias veces
            var cache = new AssetCache();

            EditorUtility.DisplayProgressBar("🔌", "GameManager...", 0.05f);
            total += ConectarGameManager(cache);

            EditorUtility.DisplayProgressBar("🔌", "Destrucción...", 0.15f);
            total += ConectarDestruccion(cache);

            EditorUtility.DisplayProgressBar("🔌", "Armas...", 0.25f);
            total += ConectarArmas(cache);

            EditorUtility.DisplayProgressBar("🔌", "Manifestación...", 0.35f);
            total += ConectarManifestacion(cache);

            EditorUtility.DisplayProgressBar("🔌", "Clima...", 0.45f);
            total += ConectarClima(cache);

            EditorUtility.DisplayProgressBar("🔌", "Fauna...", 0.55f);
            total += ConectarFauna(cache);

            EditorUtility.DisplayProgressBar("🔌", "Vegetación y árboles...", 0.65f);
            total += ConectarVegetacion(cache);

            EditorUtility.DisplayProgressBar("🔌", "NPCs...", 0.75f);
            total += ConectarNPCs(cache);

            EditorUtility.DisplayProgressBar("🔌", "Vehículos NPC...", 0.85f);
            total += ConectarVehiculosNPC(cache);

            EditorUtility.DisplayProgressBar("🔌", "Resources...", 0.95f);
            total += PopularResources(cache);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorUtility.DisplayDialog("🔌 Conexiones completadas",
            $"{total} campos conectados en la escena.\n\n" +
            "Si algún sistema no está en escena todavía:\n" +
            "Tools → Alsasua → ██ BUILD TODO ██  primero,\n" +
            "luego vuelve a ejecutar este script.", "OK");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  GAME MANAGER
    // ════════════════════════════════════════════════════════════════════════

    static int ConectarGameManager(AssetCache c)
    {
        var gm = Object.FindFirstObjectByType<GameManagerAltsasua>();
        if (gm == null) return 0;
        int n = 0;

        // Coche policía
        if (gm.prefabCochePolicia == null)
        {
            var interceptor = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Police Car & Helicopter/Prefabs/Interceptor.prefab");
            interceptor ??= c.FindPrefab("interceptor", "police_car", "policeCar", "coche_policia");
            if (interceptor != null) { gm.prefabCochePolicia = interceptor; n++; }
        }

        // Helicóptero
        if (gm.prefabHelicoptero == null)
        {
            var heli = c.FindPrefab("helicopter","helicoptero","heli","chopper");
            if (heli != null) { gm.prefabHelicoptero = heli; n++; }
        }

        // Enemigo (Guardia Civil)
        if (gm.prefabEnemigo == null)
        {
            var gc = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NPCs/NPC_GuardiaCivil_01.prefab");
            gc ??= c.FindPrefab("guardia","police","officer","enemy");
            if (gc != null) { gm.prefabEnemigo = gc; n++; }
        }

        // Árboles para sembrado inicial
        if (gm.prefabsArboles == null || gm.prefabsArboles.Length == 0)
        {
            var trees = c.FindAllPrefabs("tree","pine","oak","conifer","fir","arbol","pino")
                         .Take(6).ToArray();
            if (trees.Length > 0) { gm.prefabsArboles = trees; n += trees.Length; }
        }

        if (n > 0) EditorUtility.SetDirty(gm);
        Debug.Log($"[Conector] GameManager: {n} campos");
        return n;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  SISTEMA DESTRUCCION
    // ════════════════════════════════════════════════════════════════════════

    static int ConectarDestruccion(AssetCache c)
    {
        var sd = Object.FindFirstObjectByType<SistemaDestruccion>();
        if (sd == null) return 0;
        int n = 0;

        Assign(sd, "prefabFuegoGrande",      c.FindParticlePrefab("fire_large","fire_big","fuego_grande","grande_fire","campfire","bonfire"),                ref n);
        Assign(sd, "prefabFuegoMedio",       c.FindParticlePrefab("fire_medium","fire_med","fuego_medio","torch","fire_vol","fire_effect"),                   ref n);
        Assign(sd, "prefabFuegoChico",       c.FindParticlePrefab("fire_small","fire_s","fuego_chico","small_fire","spark","flame"),                          ref n);
        Assign(sd, "prefabHumoNegro",        c.FindParticlePrefab("smoke_black","smoke_dark","humo_negro","black_smoke","smoke"),                             ref n);
        Assign(sd, "prefabCenizas",          c.FindParticlePrefab("ash","cinder","ember","ceniza","sparks"),                                                  ref n);
        Assign(sd, "prefabFragmentoCristal", c.FindParticlePrefab("glass","crystal","cristal","shard","debris"),                                              ref n);
        Assign(sd, "prefabExplosionGrande",  c.FindParticlePrefab("explosion_large","explosion_big","hq_explosion","cinematic_explosion","blast"),            ref n);
        Assign(sd, "prefabMolotov",          c.FindParticlePrefab("fire_bottle","molotov","bottle_fire","cocktail","projectile_fire"),                        ref n);

        // Coche calcinado
        Assign(sd, "prefabCocheCalcinado",
            c.FindPrefab("burnt_car","burned_car","junk_car","junkcar","coche_abandonado","deformed"),
            ref n);

        // Audio
        AssignAudio(sd, "clipExplosion", c.FindAudio("explosion","blast","boom","detonate"),       ref n);
        AssignAudio(sd, "clipCristales", c.FindAudio("glass","crystal","shatter","cristal","break"),ref n);

        if (n > 0) EditorUtility.SetDirty(sd);
        Debug.Log($"[Conector] SistemaDestruccion: {n} campos");
        return n;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  SISTEMA ARMAS EXTENDIDO
    // ════════════════════════════════════════════════════════════════════════

    static int ConectarArmas(AssetCache c)
    {
        var sa = Object.FindFirstObjectByType<SistemaArmasExtendido>();
        if (sa == null) return 0;
        int n = 0;

        Assign(sa, "prefabPiedra",    c.FindPrefab("rock","stone","piedra","pebble","cobble"),           ref n);
        Assign(sa, "prefabMolotov",   c.FindParticlePrefab("molotov","fire_bottle","cocktail","bottle"), ref n);
        Assign(sa, "prefabBombaLapa", c.FindPrefab("bomb","grenade","lapa","mine","explosive"),           ref n);

        if (n > 0) EditorUtility.SetDirty(sa);
        Debug.Log($"[Conector] SistemaArmas: {n} campos");
        return n;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  SISTEMA MANIFESTACION
    // ════════════════════════════════════════════════════════════════════════

    static int ConectarManifestacion(AssetCache c)
    {
        var sm = Object.FindFirstObjectByType<SistemaManifestacion>();
        if (sm == null) return 0;
        int n = 0;

        // Manifestante y encapuchado — usar civiles o bodyguards
        Assign(sm, "prefabManifestante",
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NPCs/NPC_Civil_Casual.prefab")
            ?? c.FindHumanoidPrefab("civilian","civil","people","person","character","male","female"),
            ref n);

        Assign(sm, "prefabEncapuchado",
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NPCs/NPC_Civil_UrbanEdge.prefab")
            ?? c.FindHumanoidPrefab("bodyguard","hood","masked","encapuchado","urban"),
            ref n);

        // Barricada — usar barrera o prop de calle
        Assign(sm, "prefabBarricada",
            c.FindPrefab("barrier","barricade","barricada","fence","valla","railing"),
            ref n);

        // Fuego pequeño para barricadas
        Assign(sm, "prefabFuegoPequeño",
            c.FindParticlePrefab("fire_small","small_fire","fuego","flame_small","campfire"),
            ref n);

        // Humo
        Assign(sm, "prefabHumo",
            c.FindParticlePrefab("smoke","humo","smoke_grenade","tear_gas"),
            ref n);

        // Audio de multitud
        AssignAudio(sm, "clipConsignas",  c.FindAudio("crowd","cheer","protest","multitud","people_chanting"), ref n);
        AssignAudio(sm, "clipDisturbios", c.FindAudio("riot","disturbio","glass","chaos","commotion"),          ref n);

        if (n > 0) EditorUtility.SetDirty(sm);
        Debug.Log($"[Conector] SistemaManifestacion: {n} campos");
        return n;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  SISTEMA CLIMA
    // ════════════════════════════════════════════════════════════════════════

    static int ConectarClima(AssetCache c)
    {
        var sc = Object.FindFirstObjectByType<SistemaClima>();
        if (sc == null) return 0;
        int n = 0;

        AssignAudio(sc, "clipLluvia",  c.FindAudio("rain","lluvia","drizzle","rainfall","shower"), ref n);
        AssignAudio(sc, "clipTrueno",  c.FindAudio("thunder","trueno","lightning","storm"),         ref n);
        AssignAudio(sc, "clipViento",  c.FindAudio("wind","viento","breeze","gust","howl"),         ref n);

        if (n > 0) EditorUtility.SetDirty(sc);
        Debug.Log($"[Conector] SistemaClima: {n} clips");
        return n;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  SISTEMA FAUNA
    // ════════════════════════════════════════════════════════════════════════

    static int ConectarFauna(AssetCache c)
    {
        var sf = Object.FindFirstObjectByType<SistemaFauna>();

        // También buscar en SistemasSimulacion
        var sim = Object.FindFirstObjectByType<SistemasSimulacion>();
        int n = 0;

        var animalMap = new (string campo, string[] keys)[]
        {
            ("prefabPerro",   new[]{ "dog","shepherd","perro","hound","k9","canine" }),
            ("prefabLobo",    new[]{ "wolf","lobo","dire" }),
            ("prefabCaballo", new[]{ "horse","caballo","stallion","mare","equine" }),
            ("prefabCiervo",  new[]{ "deer","ciervo","stag","doe","elk","moose" }),
            ("prefabOveja",   new[]{ "sheep","oveja","lamb","goat" }),
            ("prefabConejo",  new[]{ "rabbit","conejo","bunny","hare" }),
            ("prefabGallina", new[]{ "chicken","hen","gallina","rooster" }),
            ("prefabRata",    new[]{ "rat","mouse","rata","rodent" }),
        };

        if (sf != null)
        {
            foreach (var (campo, keys) in animalMap)
            {
                var f = typeof(SistemaFauna).GetField(campo,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null || f.GetValue(sf) != null) continue;
                var p = c.FindPrefab(keys);
                if (p == null) continue;
                f.SetValue(sf, p);
                n++;
                Debug.Log($"[Conector] SistemaFauna.{campo} ← {p.name}");
            }
            if (n > 0) EditorUtility.SetDirty(sf);
        }

        // SistemasSimulacion tiene prefabsAnimales[]
        if (sim != null)
        {
            var f = typeof(SistemasSimulacion).GetField("prefabsAnimales",
                BindingFlags.Public | BindingFlags.Instance);
            if (f != null && (f.GetValue(sim) as GameObject[])?.Length == 0)
            {
                var lista = animalMap.Select(x => c.FindPrefab(x.keys))
                                     .Where(p => p != null).ToArray();
                if (lista.Length > 0) { f.SetValue(sim, lista); n += lista.Length; EditorUtility.SetDirty(sim); }
            }
        }

        Debug.Log($"[Conector] Fauna: {n} animales");
        return n;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  VEGETACION Y ARBOLES
    // ════════════════════════════════════════════════════════════════════════

    static int ConectarVegetacion(AssetCache c)
    {
        int n = 0;

        // AlsasuaTreeStreamer
        var streamer = Object.FindFirstObjectByType<AlsasuaTreeStreamer>();
        if (streamer != null && (streamer.treePrefabs == null || streamer.treePrefabs.Length == 0))
        {
            var trees = c.FindAllPrefabs("tree","pine","oak","conifer","fir","spruce",
                                          "beech","birch","arbol","pino","roble","haya")
                         .Take(12).ToArray();
            if (trees.Length > 0)
            {
                streamer.treePrefabs = trees;
                EditorUtility.SetDirty(streamer);
                n += trees.Length;
                Debug.Log($"[Conector] TreeStreamer ← {trees.Length} prefabs");
            }
        }

        // SistemasSimulacion.materialesArbol
        var sim = Object.FindFirstObjectByType<SistemasSimulacion>();
        if (sim != null)
        {
            var f = typeof(SistemasSimulacion).GetField("materialesArbol",
                BindingFlags.Public | BindingFlags.Instance);
            if (f != null && (f.GetValue(sim) as Material[])?.Length == 0)
            {
                var mats = c.FindAllMaterials("bark","leaf","tree","wood","pine_mat","oak_mat")
                            .Take(4).ToArray();
                if (mats.Length > 0) { f.SetValue(sim, mats); n += mats.Length; EditorUtility.SetDirty(sim); }
            }
        }

        return n;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  NPCs CIVILES
    // ════════════════════════════════════════════════════════════════════════

    static int ConectarNPCs(AssetCache c)
    {
        int n = 0;
        var guids = AssetDatabase.FindAssets("t:Prefab NPC_", new[]{"Assets/Prefabs/NPCs"});
        var humanoids = c.FindAllHumanoidPrefabs().ToList();
        if (humanoids.Count == 0) return 0;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            using var scope = new PrefabUtility.EditPrefabContentsScope(path);
            var npc = scope.prefabContentsRoot.GetComponent<NPCCivil>();
            if (npc != null && npc.prefabModelo == null)
            {
                npc.prefabModelo = humanoids[i % humanoids.Count];
                n++;
            }
        }

        Debug.Log($"[Conector] NPCCivil: {n} modelos asignados");
        return n;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  VEHICULOS NPC
    // ════════════════════════════════════════════════════════════════════════

    static int ConectarVehiculosNPC(AssetCache c)
    {
        int n = 0;
        var guids = AssetDatabase.FindAssets("t:Prefab VehiculoNPC_", new[]{"Assets/Prefabs/Coches"});
        var coches = c.FindAllPrefabs("car","vehicle","sedan","hatchback","van","truck","taxi")
                      .Take(8).ToList();
        if (coches.Count == 0) return 0;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            using var scope = new PrefabUtility.EditPrefabContentsScope(path);
            var veh = scope.prefabContentsRoot.GetComponent<VehiculoNPC>();
            if (veh != null)
            {
                var f = typeof(VehiculoNPC).GetField("prefabModelo",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.GetValue(veh) == null)
                {
                    f.SetValue(veh, coches[i % coches.Count]);
                    n++;
                }
            }
        }

        Debug.Log($"[Conector] VehiculoNPC: {n} modelos asignados");
        return n;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  RESOURCES — copiar assets clave a carpetas accesibles en build
    // ════════════════════════════════════════════════════════════════════════

    static int PopularResources(AssetCache c)
    {
        int n = 0;

        // Audio
        n += CopiarAResources("Assets/Resources/Audio", new Dictionary<string, string[]>
        {
            ["disparo"]      = new[]{ "gunshot","gun_shot","pistol","shot","shoot","bang","rifle_shot" },
            ["explosion"]    = new[]{ "explosion","explode","blast","boom","detonate" },
            ["sirena"]       = new[]{ "siren","police_siren","alarm","wail" },
            ["motor"]        = new[]{ "engine","motor","car_idle","car_loop","vehicle_engine" },
            ["paso_asfalto"] = new[]{ "footstep","foot_step","concrete_step","asphalt_walk" },
            ["paso_tierra"]  = new[]{ "dirt_step","grass_step","soil_walk","gravel_step" },
            ["lluvia"]       = new[]{ "rain","lluvia","rainfall","drizzle" },
            ["viento"]       = new[]{ "wind","viento","breeze","wind_loop" },
            ["pajaros"]      = new[]{ "bird","tweet","chirp","sparrow","morning_bird" },
            ["multitud"]     = new[]{ "crowd","cheer","protest","people_ambient","mob" },
            ["impacto_metal"]= new[]{ "metal_impact","impact_metal","clang","steel_hit" },
            ["gritos"]       = new[]{ "scream","shout","yell","cry","pain_scream" },
            ["notificacion"] = new[]{ "notification","ding","bell","alert","ui_notify" },
        }, c);

        // Efectos partículas
        n += CopiarPrefabsAResources("Assets/Resources/Efectos",
            new[]{ "explo","fire","smoke","blast","flame","spark","blood" }, 10, c);

        // Props
        n += CopiarPrefabsAResources("Assets/Resources/Props",
            new[]{ "bench","lamp","barrier","barrel","crate","rock","fence","sign","trash" }, 20, c);

        // Coches
        n += CopiarPrefabsAResources("Assets/Resources/Coches",
            new[]{ "sedan","hatchback","car_","vehicle_","truck_","van_","taxi_" }, 10, c);

        return n;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  UTILIDADES
    // ════════════════════════════════════════════════════════════════════════

    static void Assign(object obj, string campo, GameObject val, ref int n)
    {
        if (obj == null || val == null) return;
        var f = obj.GetType().GetField(campo,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null || f.GetValue(obj) != null) return;
        f.SetValue(obj, val);
        n++;
    }

    static void AssignAudio(object obj, string campo, AudioClip clip, ref int n)
    {
        if (obj == null || clip == null) return;
        var f = obj.GetType().GetField(campo,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null || f.GetValue(obj) != null) return;
        f.SetValue(obj, clip);
        n++;
    }

    static int CopiarAResources(string destDir, Dictionary<string, string[]> mapa, AssetCache c)
    {
        EnsureFolder(destDir);
        int cnt = 0;
        var done = new HashSet<string>();

        foreach (var g in AssetDatabase.FindAssets("t:AudioClip"))
        {
            string src  = AssetDatabase.GUIDToAssetPath(g);
            string name = Path.GetFileNameWithoutExtension(src).ToLower();

            foreach (var (dest, keys) in mapa)
            {
                if (done.Contains(dest)) continue;
                if (!keys.Any(k => name.Contains(k))) continue;
                string dp = $"{destDir}/{dest}{Path.GetExtension(src)}";
                if (!AssetExists(dp)) { AssetDatabase.CopyAsset(src, dp); cnt++; }
                done.Add(dest);
                break;
            }
        }

        if (cnt > 0) AssetDatabase.Refresh();
        return cnt;
    }

    static int CopiarPrefabsAResources(string destDir, string[] keywords, int max, AssetCache c)
    {
        EnsureFolder(destDir);
        int cnt = 0;

        foreach (var g in AssetDatabase.FindAssets("t:Prefab"))
        {
            if (cnt >= max) break;
            string src = AssetDatabase.GUIDToAssetPath(g);
            if (src.StartsWith("Assets/Prefabs/") || src.StartsWith("Assets/Resources/")) continue;
            string low = Path.GetFileNameWithoutExtension(src).ToLower();
            if (!keywords.Any(k => low.Contains(k))) continue;

            string dp = $"{destDir}/{Path.GetFileName(src)}";
            if (!AssetExists(dp)) { AssetDatabase.CopyAsset(src, dp); cnt++; }
        }

        if (cnt > 0) AssetDatabase.Refresh();
        return cnt;
    }

    static void EnsureFolder(string path)
    {
        var parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    static bool AssetExists(string path) =>
        File.Exists(Path.Combine(Application.dataPath.Replace("Assets", ""), path));
}

// ── Caché de búsqueda de assets ──────────────────────────────────────────────

class AssetCache
{
    readonly Dictionary<string, GameObject>  _prefabs  = new();
    readonly Dictionary<string, AudioClip>   _clips    = new();
    readonly Dictionary<string, Material>    _mats     = new();

    public AssetCache()
    {
        // Pre-cachear todos los prefabs
        foreach (var g in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            if (path.StartsWith("Assets/Prefabs/") || path.StartsWith("Assets/Resources/")) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null) _prefabs[path.ToLower()] = go;
        }

        // Pre-cachear AudioClips
        foreach (var g in AssetDatabase.FindAssets("t:AudioClip"))
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null) _clips[path.ToLower()] = clip;
        }

        // Pre-cachear Materials
        foreach (var g in AssetDatabase.FindAssets("t:Material"))
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            if (path.StartsWith("Assets/Materials/")) continue; // ya los tenemos
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) _mats[path.ToLower()] = mat;
        }
    }

    public GameObject FindPrefab(params string[] keywords) =>
        _prefabs.FirstOrDefault(kv => keywords.Any(k => kv.Key.Contains(k))).Value;

    public GameObject FindParticlePrefab(params string[] keywords)
    {
        foreach (var kv in _prefabs)
        {
            if (!keywords.Any(k => kv.Key.Contains(k))) continue;
            if (kv.Value.GetComponent<ParticleSystem>() != null ||
                kv.Value.GetComponentInChildren<ParticleSystem>(true) != null)
                return kv.Value;
        }
        return null;
    }

    public GameObject FindHumanoidPrefab(params string[] keywords)
    {
        foreach (var kv in _prefabs)
        {
            if (!keywords.Any(k => kv.Key.Contains(k))) continue;
            if (kv.Value.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
                return kv.Value;
        }
        return null;
    }

    public IEnumerable<GameObject> FindAllPrefabs(params string[] keywords) =>
        _prefabs.Where(kv => keywords.Any(k => kv.Key.Contains(k)))
                .Select(kv => kv.Value);

    public IEnumerable<GameObject> FindAllHumanoidPrefabs() =>
        _prefabs.Values.Where(p => p.GetComponentInChildren<SkinnedMeshRenderer>(true) != null);

    public AudioClip FindAudio(params string[] keywords) =>
        _clips.FirstOrDefault(kv => keywords.Any(k => kv.Key.Contains(k))).Value;

    public IEnumerable<Material> FindAllMaterials(params string[] keywords) =>
        _mats.Where(kv => keywords.Any(k => kv.Key.Contains(k)))
             .Select(kv => kv.Value);
}
#endif
