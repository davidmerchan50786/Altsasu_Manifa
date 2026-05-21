// Assets/Scripts/Editor/ImportadorAssetsLocales.cs
// Tools → Alsasua → 📦 Importar Assets Locales
// Tools → Alsasua → 🔗 Conectar Assets a Sistemas
//
// Detecta TODOS los .unitypackage del cache local del Asset Store
// e importa + conecta automáticamente a los sistemas del simulador.

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

public static class ImportadorAssetsLocales
{
    static readonly string CACHE = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "Unity", "Asset Store-5.x");

    // Packages que no aportan nada al simulador
    static readonly HashSet<string> IGNORAR = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "Free Island Collection",
        "Free Lava Shader",
        "Pirate Music Pack",
        "Western Audio Music",
        "Horror Ambient Album - 082318",
        "Dark Ambient Music - Into Insanity Vol 2 Freebie",
        "Homing Missile",
        "H70 Air-to-Ground Rocket PBR",
        "Mobile Missiles Pack",
        "Free 3D Missile",
        "Eci Forge Uma Armours Pack1",
        "Free Lava Shader",
    };

    // ── PASO 1: Importar todos ────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/📦 Importar Assets Locales", priority = 1)]
    public static void ImportarTodo()
    {
        if (!Directory.Exists(CACHE))
        {
            EditorUtility.DisplayDialog("Cache no encontrado",
                $"No se encontró:\n{CACHE}\n\nDescarga assets desde el Package Manager primero.", "OK");
            return;
        }

        var packages = Directory
            .GetFiles(CACHE, "*.unitypackage", SearchOption.AllDirectories)
            .Select(p => (nombre: Path.GetFileNameWithoutExtension(p), path: p))
            .Where(x => !IGNORAR.Contains(x.nombre))
            .OrderBy(x => x.nombre)
            .ToList();

        if (packages.Count == 0)
        {
            EditorUtility.DisplayDialog("Sin packages", "No hay packages en el cache.", "OK");
            return;
        }

        // Agrupar por categoría para mostrar resumen
        var chars   = packages.Where(p => p.path.Contains("Characters") || p.path.Contains("Humanoid") || p.path.Contains("AnimationBipedal")).Select(p => p.nombre).ToList();
        var vehs    = packages.Where(p => p.path.Contains("Vehicles")).Select(p => p.nombre).ToList();
        var nature  = packages.Where(p => p.path.Contains("Vegetation") || p.path.Contains("Trees") || p.path.Contains("Environments") || p.path.Contains("Landscapes")).Select(p => p.nombre).ToList();
        var effects = packages.Where(p => p.path.Contains("Particle") || p.path.Contains("Fire")).Select(p => p.nombre).ToList();
        var audio   = packages.Where(p => p.path.Contains("Audio")).Select(p => p.nombre).ToList();
        var props   = packages.Where(p => p.path.Contains("Props") || p.path.Contains("Exterior")).Select(p => p.nombre).ToList();
        var tools   = packages.Where(p => p.path.Contains("Editor")).Select(p => p.nombre).ToList();

        bool ok = EditorUtility.DisplayDialog("📦 Importar Assets — " + packages.Count + " packages",
            $"PERSONAJES ({chars.Count}): {string.Join(", ", chars.Take(3))}{(chars.Count > 3 ? "..." : "")}\n" +
            $"VEHÍCULOS ({vehs.Count}): {string.Join(", ", vehs.Take(3))}{(vehs.Count > 3 ? "..." : "")}\n" +
            $"NATURALEZA ({nature.Count}): {string.Join(", ", nature.Take(3))}{(nature.Count > 3 ? "..." : "")}\n" +
            $"EFECTOS ({effects.Count}): {string.Join(", ", effects.Take(3))}{(effects.Count > 3 ? "..." : "")}\n" +
            $"AUDIO ({audio.Count}): {string.Join(", ", audio.Take(3))}{(audio.Count > 3 ? "..." : "")}\n" +
            $"PROPS ({props.Count}): {string.Join(", ", props.Take(3))}{(props.Count > 3 ? "..." : "")}\n" +
            $"HERRAMIENTAS ({tools.Count}): {string.Join(", ", tools)}\n\n" +
            "Unity mostrará un diálogo por cada package.\nPulsa 'Import' en cada uno.",
            "Importar todos", "Cancelar");

        if (!ok) return;

        _queue    = new Queue<(string, string)>(packages);
        _total    = packages.Count;
        _imported = 0;
        EditorApplication.delayCall += ImportarSiguiente;
    }

    static Queue<(string nombre, string path)> _queue;
    static int _total, _imported;

    static void ImportarSiguiente()
    {
        if (_queue == null || _queue.Count == 0)
        {
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("✅ Importación completada",
                $"{_imported}/{_total} packages importados.\n\n" +
                "SIGUIENTE:\n" +
                "Tools → Alsasua → 🔗 Conectar Assets a Sistemas\n\n" +
                "Después:\n" +
                "Tools → Alsasua → ██ BUILD TODO ██",
                "OK");
            return;
        }

        var (nombre, path) = _queue.Dequeue();
        EditorUtility.DisplayProgressBar(
            $"📦 Importando [{_imported + 1}/{_total}]", nombre,
            (float)_imported / _total);

        AssetDatabase.ImportPackage(path, true);
        _imported++;
        EditorApplication.delayCall += ImportarSiguiente;
    }

    // ── PASO 2: Conectar assets a sistemas ───────────────────────────────

    [MenuItem("Tools/Alsasua/🔗 Conectar Assets a Sistemas", priority = 2)]
    public static void ConectarAssets()
    {
        AssetDatabase.Refresh();
        int total = 0;

        total += ConectarArboles();
        total += ConectarPersonajes();
        total += ConectarAudio();
        total += ConectarExplosiones();
        total += ConectarFauna();
        total += ConectarProps();
        total += ConectarVehiculos();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("🔗 Conexiones completadas",
            $"{total} assets conectados.\n\n" +
            "Tools → Alsasua → ██ BUILD TODO ██  para reconstruir la escena.",
            "OK");
    }

    // ── Árboles ──────────────────────────────────────────────────────────

    static int ConectarArboles()
    {
        var streamer = Object.FindFirstObjectByType<AlsasuaTreeStreamer>();
        if (streamer == null) return 0;

        var prefabs = new List<GameObject>();
        string[] keywords = { "tree","pine","oak","conifer","fir","spruce","beech","birch","arbol","pino" };

        foreach (var g in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            if (path.StartsWith("Assets/Prefabs/") || path.StartsWith("Assets/_Extracted")) continue;
            string low  = path.ToLower();
            if (!keywords.Any(k => low.Contains(k))) continue;
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (p != null && !prefabs.Contains(p)) prefabs.Add(p);
            if (prefabs.Count >= 12) break;
        }

        if (prefabs.Count == 0) return 0;
        streamer.treePrefabs = prefabs.ToArray();
        EditorUtility.SetDirty(streamer);
        Debug.Log($"[Importador] 🌲 {prefabs.Count} árboles → AlsasuaTreeStreamer");
        return prefabs.Count;
    }

    // ── Personajes ────────────────────────────────────────────────────────

    static int ConectarPersonajes()
    {
        var humanoids = new List<GameObject>();
        foreach (var g in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            if (path.StartsWith("Assets/Prefabs/")) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null && go.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
                humanoids.Add(go);
        }

        if (humanoids.Count == 0) return 0;

        int cnt = 0;
        var guids = AssetDatabase.FindAssets("t:Prefab NPC_", new[]{"Assets/Prefabs/NPCs"});
        for (int i = 0; i < guids.Length; i++)
        {
            string npcPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            using var scope = new PrefabUtility.EditPrefabContentsScope(npcPath);
            var npc = scope.prefabContentsRoot.GetComponent<NPCCivil>();
            if (npc != null && npc.prefabModelo == null)
            {
                npc.prefabModelo = humanoids[i % humanoids.Count];
                cnt++;
            }
        }

        Debug.Log($"[Importador] 👥 {cnt} NPCs ← modelos humanoides");
        return cnt;
    }

    // ── Audio ─────────────────────────────────────────────────────────────

    static int ConectarAudio()
    {
        string dest = "Assets/Resources/Audio";
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets","Resources");
        if (!AssetDatabase.IsValidFolder(dest))               AssetDatabase.CreateFolder("Assets/Resources","Audio");

        var mapa = new Dictionary<string, string[]>
        {
            ["disparo"]      = new[]{ "gunshot","gun_shot","pistol","rifle","shot","shoot","bang" },
            ["explosion"]    = new[]{ "explosion","explode","blast","detonate","boom" },
            ["sirena"]       = new[]{ "siren","police","alarm","emergency","wail" },
            ["motor"]        = new[]{ "engine","motor","car_idle","vehicle","car_loop" },
            ["paso_asfalto"] = new[]{ "footstep","foot_step","paso","walk_concrete","walking" },
            ["lluvia"]       = new[]{ "rain","lluvia","drizzle","rainfall" },
            ["viento"]       = new[]{ "wind","viento","breeze","gust","howl" },
            ["pajaros"]      = new[]{ "bird","pajaro","tweet","chirp","sparrow","crow","nightingale" },
            ["multitud"]     = new[]{ "crowd","multitud","cheer","protest","people","mob","gathering" },
            ["impacto"]      = new[]{ "impact","hit","punch","golpe","smash","collision" },
            ["click_ui"]     = new[]{ "click","button","ui_click","select","menu" },
            ["gritos"]       = new[]{ "scream","shout","yell","gritos","cry","pain" },
        };

        var copied = new HashSet<string>();
        int cnt = 0;

        foreach (var g in AssetDatabase.FindAssets("t:AudioClip"))
        {
            string src  = AssetDatabase.GUIDToAssetPath(g);
            string name = Path.GetFileNameWithoutExtension(src).ToLower();

            foreach (var (destName, keys) in mapa)
            {
                if (copied.Contains(destName)) continue;
                if (!keys.Any(k => name.Contains(k))) continue;
                string destPath = $"{dest}/{destName}{Path.GetExtension(src)}";
                if (!File.Exists(Path.Combine(Application.dataPath.Replace("Assets",""), destPath)))
                {
                    AssetDatabase.CopyAsset(src, destPath);
                    cnt++;
                }
                copied.Add(destName);
                break;
            }
        }

        if (cnt > 0) { AssetDatabase.Refresh(); Debug.Log($"[Importador] 🔊 {cnt} clips → Resources/Audio/"); }
        return cnt;
    }

    // ── Explosiones ───────────────────────────────────────────────────────

    static int ConectarExplosiones()
    {
        string dest = "Assets/Resources/Efectos";
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets","Resources");
        if (!AssetDatabase.IsValidFolder(dest))               AssetDatabase.CreateFolder("Assets/Resources","Efectos");

        int cnt = 0;
        foreach (var g in AssetDatabase.FindAssets("t:Prefab"))
        {
            string src  = AssetDatabase.GUIDToAssetPath(g);
            string low  = Path.GetFileNameWithoutExtension(src).ToLower();
            if (!low.Contains("explo") && !low.Contains("fire") && !low.Contains("smoke") &&
                !low.Contains("blast") && !low.Contains("flame") && !low.Contains("spark") &&
                !low.Contains("blood") && !low.Contains("impact_fx")) continue;

            var p = AssetDatabase.LoadAssetAtPath<GameObject>(src);
            if (p == null || p.GetComponent<ParticleSystem>() == null) continue;

            string destPath = $"{dest}/{p.name}.prefab";
            if (!File.Exists(Path.Combine(Application.dataPath.Replace("Assets",""), destPath)))
            {
                AssetDatabase.CopyAsset(src, destPath);
                cnt++;
            }
            if (cnt >= 10) break;
        }

        if (cnt > 0) { AssetDatabase.Refresh(); Debug.Log($"[Importador] 💥 {cnt} efectos → Resources/Efectos/"); }
        return cnt;
    }

    // ── Fauna ─────────────────────────────────────────────────────────────

    static int ConectarFauna()
    {
        var fauna = Object.FindFirstObjectByType<SistemaFauna>();
        if (fauna == null) return 0;

        var campos = new (string field, string[] keys)[]
        {
            ("prefabPerro",    new[]{ "dog","shepherd","perro","hound","k9" }),
            ("prefabLobo",     new[]{ "wolf","lobo","dire_wolf" }),
            ("prefabCaballo",  new[]{ "horse","caballo","stallion","mare" }),
            ("prefabCiervo",   new[]{ "deer","ciervo","stag","doe" }),
            ("prefabOveja",    new[]{ "sheep","oveja","lamb" }),
            ("prefabConejo",   new[]{ "rabbit","conejo","bunny","hare" }),
        };

        int cnt = 0;
        foreach (var (campo, keys) in campos)
        {
            var f = typeof(SistemaFauna).GetField(campo,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null || f.GetValue(fauna) != null) continue;

            foreach (var g in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                string low  = Path.GetFileNameWithoutExtension(path).ToLower();
                if (!keys.Any(k => low.Contains(k))) continue;
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (p == null) continue;
                f.SetValue(fauna, p);
                EditorUtility.SetDirty(fauna);
                Debug.Log($"[Importador] 🐾 {p.name} → SistemaFauna.{campo}");
                cnt++;
                break;
            }
        }
        return cnt;
    }

    // ── Props urbanos ─────────────────────────────────────────────────────

    static int ConectarProps()
    {
        string dest = "Assets/Resources/Props";
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets","Resources");
        if (!AssetDatabase.IsValidFolder(dest))               AssetDatabase.CreateFolder("Assets/Resources","Props");

        string[] keys = { "bench","lamp","barrier","barrel","crate","rock","fence",
                           "sign","trash","bin","bollard","hydrant","mailbox",
                           "barricada","farola","banco","valla","contenedor" };
        int cnt = 0;
        foreach (var g in AssetDatabase.FindAssets("t:Prefab"))
        {
            string src = AssetDatabase.GUIDToAssetPath(g);
            string low = Path.GetFileNameWithoutExtension(src).ToLower();
            if (!keys.Any(k => low.Contains(k))) continue;
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(src);
            if (p == null) continue;
            string destPath = $"{dest}/{p.name}.prefab";
            if (!File.Exists(Path.Combine(Application.dataPath.Replace("Assets",""), destPath)))
            {
                AssetDatabase.CopyAsset(src, destPath);
                cnt++;
            }
            if (cnt >= 25) break;
        }
        if (cnt > 0) { AssetDatabase.Refresh(); Debug.Log($"[Importador] 🏙️ {cnt} props → Resources/Props/"); }
        return cnt;
    }

    // ── Vehículos — detectar Police Car y coches civiles ─────────────────

    static int ConectarVehiculos()
    {
        int cnt = 0;

        // Police Car — WizardCochePrefab lo busca en esta ruta exacta
        if (AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Police Car & Helicopter/Prefabs/Interceptor.prefab") != null)
        {
            Debug.Log("[Importador] 🚔 Interceptor detectado — listo para WizardCochePrefab");
            cnt++;
        }

        // Coches civiles adicionales → Resources/Coches/ para ControladorTrafico
        string dest = "Assets/Resources/Coches";
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets","Resources");
        if (!AssetDatabase.IsValidFolder(dest))               AssetDatabase.CreateFolder("Assets/Resources","Coches");

        string[] carKeys = { "car","vehicle","sedan","hatchback","truck","van","bus","taxi","police" };
        foreach (var g in AssetDatabase.FindAssets("t:Prefab"))
        {
            string src = AssetDatabase.GUIDToAssetPath(g);
            string low = Path.GetFileNameWithoutExtension(src).ToLower();
            if (!carKeys.Any(k => low.Contains(k))) continue;
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(src);
            if (p == null) continue;
            string destPath = $"{dest}/{p.name}.prefab";
            if (!File.Exists(Path.Combine(Application.dataPath.Replace("Assets",""), destPath)))
            {
                AssetDatabase.CopyAsset(src, destPath);
                cnt++;
            }
            if (cnt >= 15) break;
        }

        if (cnt > 0) { AssetDatabase.Refresh(); Debug.Log($"[Importador] 🚗 {cnt} coches → Resources/Coches/"); }
        return cnt;
    }
}
#endif
