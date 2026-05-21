// Assets/Scripts/Editor/ImportadorAssetsLocales.cs
// Tools → Alsasua → 📦 Importar Assets Locales
//
// Importa los .unitypackage del Asset Store cache local y los conecta
// automáticamente a los sistemas del simulador.
//
// PASO 1: Importa todos los packages (interactivo — Unity pregunta qué importar)
// PASO 2: Conecta assets importados a sus sistemas correspondientes

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

public static class ImportadorAssetsLocales
{
    // Ruta base del cache del Asset Store
    static readonly string CACHE = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "Unity", "Asset Store-5.x");

    // Packages a importar con su ruta relativa dentro del cache
    static readonly (string label, string ruta)[] PACKAGES =
    {
        // Personajes
        ("Police Car & Helicopter",  @"SICS Games\3D ModelsVehiclesLand\Police Car Helicopter.unitypackage"),
        ("City People FREE",         @"Denys Almaral\3D ModelsCharacters\City People FREE Samples.unitypackage"),
        ("Bodyguards",               @"Batewar\3D ModelsCharactersHumanoidsHumans\Bodyguards.unitypackage"),
        ("Survivalist Character",    @"Slayver\3D ModelsCharacters\Survivalist character.unitypackage"),
        // Vehículos
        ("Hatchback and Sedan",      @"RCC Design\3D ModelsVehiclesLand\Hatchback and Sedan.unitypackage"),
        ("UAA City Vehicles",        @"Testmobile\3D ModelsVehiclesLand\UAA - City Props - Vehicles.unitypackage"),
        // Entorno urbano
        ("Street Assets",            @"Rem Storms\3D ModelsPropsExterior\Street Assets.unitypackage"),
        ("Polygon City Pack Free",   @"WAND AND CIRCLES\3D Models\Polygon City Pack - Environment and Interior Free.unitypackage"),
        // Vegetación y fauna
        ("Realistic Tree 9",         @"Pixel Games\3D ModelsVegetationTrees\Realistic Tree 9 Rainbow Tree.unitypackage"),
        ("Realistic Tree 10",        @"Pixel Games\3D ModelsVegetationTrees\Realistic Tree 10.unitypackage"),
        ("German Shepherd",          @"RetroStyle Games\3D ModelsCharactersAnimals\German Shepherd 3D Model.unitypackage"),
        // Efectos
        ("HQ Explosions FREE",       @"SparkFlow\Particle SystemsFire\HQ Explosions Pack FREE.unitypackage"),
        ("Cinematic Explosions",     @"Mirza Beig\Particle SystemsFire\Cinematic Explosions FREE.unitypackage"),
        ("Procedural Fire",          @"Hovl Studio\Particle SystemsFire\Procedural fire.unitypackage"),
        // Audio
        ("Free Sound Effects Pack",  @"Olivier Girardot\AudioSound FX\Free Sound Effects Pack.unitypackage"),
        ("Free Ambience Sounds",     @"Gregor Quendel\AudioAmbientUrban\Free General Ambience Sounds.unitypackage"),
        // Animaciones
        ("Fighting Motions Vol1",    @"Magicpot Inc\AnimationBipedal\Fighting Motions Vol1.unitypackage"),
        ("Ragdoll & Mecanim",        @"Stas Bz\Complete ProjectsSystems\Ragdoll and Transition to Mecanim.unitypackage"),
        // Herramientas
        ("EasyRoads3D Free",         @"AndaSoft\3D ModelsCharacters\EasyRoads3D Free v3.unitypackage"),
    };

    // ── PASO 1: Importar packages ──────────────────────────────────────────

    [MenuItem("Tools/Alsasua/📦 Importar Assets Locales", priority = 1)]
    public static void ImportarTodo()
    {
        var faltantes = new List<string>();
        var disponibles = new List<(string label, string fullPath)>();

        foreach (var (label, ruta) in PACKAGES)
        {
            string full = Path.Combine(CACHE, ruta);
            if (File.Exists(full))
                disponibles.Add((label, full));
            else
                faltantes.Add(label);
        }

        if (disponibles.Count == 0)
        {
            EditorUtility.DisplayDialog("📦 Sin packages",
                "No se encontraron packages en el cache del Asset Store:\n" + CACHE,
                "OK");
            return;
        }

        string lista = string.Join("\n", disponibles.Select(d => $"  ✓ {d.label}"));
        string aviso = faltantes.Count > 0
            ? "\n\nNo encontrados:\n" + string.Join("\n", faltantes.Select(f => $"  ✗ {f}"))
            : "";

        bool ok = EditorUtility.DisplayDialog("📦 Importar Assets",
            $"Se importarán {disponibles.Count} packages:\n\n{lista}{aviso}\n\n" +
            "Unity mostrará un diálogo por cada package.\n" +
            "Pulsa 'Import' en cada uno para aceptar.",
            "Importar todos", "Cancelar");

        if (!ok) return;

        _queue    = new Queue<(string, string)>(disponibles);
        _total    = disponibles.Count;
        _imported = 0;

        EditorApplication.delayCall += ImportarSiguiente;
    }

    static Queue<(string label, string path)> _queue;
    static int _total, _imported;

    static void ImportarSiguiente()
    {
        if (_queue == null || _queue.Count == 0)
        {
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("✅ Importación completada",
                $"{_imported}/{_total} packages importados.\n\n" +
                "SIGUIENTE PASO:\n" +
                "Tools → Alsasua → 🔗 Conectar Assets a Sistemas\n\n" +
                "Esto conectará los assets importados a:\n" +
                "• AlsasuaTreeStreamer → prefabs de árbol\n" +
                "• NPCCivil → modelos de personaje\n" +
                "• WizardCochePrefab → Police Car + Hatchback\n" +
                "• AudioManager → clips reales\n" +
                "• SistemaExplosion → efectos de partículas",
                "OK");
            return;
        }

        var (label, path) = _queue.Dequeue();
        float p = (float)_imported / _total;
        EditorUtility.DisplayProgressBar($"📦 Importando [{_imported+1}/{_total}]", label, p);

        AssetDatabase.ImportPackage(path, true); // true = interactivo
        _imported++;

        EditorApplication.delayCall += ImportarSiguiente;
    }

    // ── PASO 2: Conectar assets a sistemas ────────────────────────────────

    [MenuItem("Tools/Alsasua/🔗 Conectar Assets a Sistemas", priority = 2)]
    public static void ConectarAssets()
    {
        AssetDatabase.Refresh();

        int conexiones = 0;
        conexiones += ConectarArboles();
        conexiones += ConectarPersonajes();
        conexiones += ConectarVehiculos();
        conexiones += ConectarAudio();
        conexiones += ConectarExplosiones();
        conexiones += ConectarFauna();

        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("🔗 Assets conectados",
            $"{conexiones} conexiones realizadas.\n\n" +
            "Ejecuta Tools → Alsasua → ██ BUILD TODO ██\n" +
            "para reconstruir la escena con todos los assets.",
            "OK");
    }

    // ── Árboles → AlsasuaTreeStreamer ──────────────────────────────────────

    static int ConectarArboles()
    {
        var streamer = Object.FindFirstObjectByType<AlsasuaTreeStreamer>();
        if (streamer == null)
        {
            // Buscar también el script en Assets si no hay escena abierta
            var go = new GameObject("_TempTreeConfig");
            streamer = go.AddComponent<AlsasuaTreeStreamer>();
        }

        var prefabs = new List<GameObject>();

        // Buscar prefabs de árbol importados
        string[] patrones = {
            "t:Prefab", "Tree", "Pine", "Oak", "Arbol", "Pino", "Roble"
        };

        // Realistic Trees de Pixel Games
        foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[]{ "Assets/RealisticPine", "Assets/Realistic Tree", "Assets/PixelGames" }))
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
            if (p != null) prefabs.Add(p);
        }

        // Buscar por nombre si la carpeta es distinta
        if (prefabs.Count == 0)
        {
            foreach (var g in AssetDatabase.FindAssets("t:Prefab Tree"))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (path.Contains("Realistic") || path.Contains("Tree") || path.Contains("Pine") || path.Contains("Oak"))
                {
                    var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (p != null) prefabs.Add(p);
                }
            }
        }

        if (prefabs.Count == 0) return 0;

        streamer.treePrefabs = prefabs.Take(8).ToArray();
        EditorUtility.SetDirty(streamer);
        Debug.Log($"[Importador] 🌲 {prefabs.Count} prefabs árbol → AlsasuaTreeStreamer");
        return prefabs.Count;
    }

    // ── Personajes → NPCCivil prefabs ─────────────────────────────────────

    static int ConectarPersonajes()
    {
        // Buscar prefabs de personajes importados
        var personajePrefabs = new Dictionary<string, GameObject>();

        string[] carpetas = {
            "Assets/City People", "Assets/CityPeople", "Assets/Bodyguards",
            "Assets/Survivalist", "Assets/Characters"
        };

        foreach (var g in AssetDatabase.FindAssets("t:Prefab", carpetas))
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            // Solo prefabs con SkinnedMeshRenderer (personaje con mesh)
            if (prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
                personajePrefabs[prefab.name] = prefab;
        }

        if (personajePrefabs.Count == 0) return 0;

        // Actualizar los prefabs de NPCCivil existentes
        int cnt = 0;
        string[] npcPaths = AssetDatabase.FindAssets("t:Prefab NPC_Civil", new[]{"Assets/Prefabs/NPCs"});
        foreach (var g in npcPaths)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var npcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (npcPrefab == null) continue;

            var npc = npcPrefab.GetComponent<NPCCivil>();
            if (npc != null && npc.prefabModelo == null && personajePrefabs.Count > cnt)
            {
                using var edit = new PrefabUtility.EditPrefabContentsScope(path);
                var npcEd = edit.prefabContentsRoot.GetComponent<NPCCivil>();
                if (npcEd != null)
                {
                    npcEd.prefabModelo = personajePrefabs.Values.ElementAt(cnt % personajePrefabs.Count);
                    cnt++;
                }
            }
        }

        Debug.Log($"[Importador] 👥 {cnt} NPCs conectados con modelos de personaje");
        return cnt;
    }

    // ── Vehículos → WizardCochePrefab ────────────────────────────────────

    static int ConectarVehiculos()
    {
        // Police Car — WizardCochePrefab lo busca en esta ruta exacta
        string interceptorPath = "Assets/Police Car & Helicopter/Prefabs/Interceptor.prefab";
        string hatchbackSearch = "t:Prefab";

        int cnt = 0;

        // Verificar que el Interceptor existe
        if (AssetDatabase.LoadAssetAtPath<GameObject>(interceptorPath) != null)
        {
            Debug.Log("[Importador] 🚔 Police Car detectado — ejecuta 🚔 Crear Prefabs de Coche");
            cnt++;
        }

        // Hatchback y Sedan de RCC Design
        foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[]{"Assets/RCC Assets", "Assets/Hatchback", "Assets/Sedan"}))
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            if (path.Contains("Hatchback") || path.Contains("Sedan") || path.Contains("Car"))
            {
                Debug.Log($"[Importador] 🚗 Vehículo encontrado: {Path.GetFileName(path)}");
                cnt++;
            }
        }

        return cnt;
    }

    // ── Audio → AudioManager Resources ───────────────────────────────────

    static int ConectarAudio()
    {
        // Copiar clips de audio útiles a Assets/Resources/Audio/
        string destDir = "Assets/Resources/Audio";
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(destDir))
            AssetDatabase.CreateFolder("Assets/Resources", "Audio");

        var clipMap = new Dictionary<string, string>
        {
            // nombre destino → substring para buscar en los imports
            ["disparo"]       = "gunshot",
            ["explosion"]     = "explosion",
            ["sirena"]        = "siren",
            ["motor"]         = "engine",
            ["pasos"]         = "footstep",
            ["lluvia"]        = "rain",
            ["viento"]        = "wind",
            ["pajaros"]       = "bird",
            ["multitud"]      = "crowd",
            ["click"]         = "click",
        };

        int cnt = 0;
        foreach (var g in AssetDatabase.FindAssets("t:AudioClip"))
        {
            string srcPath = AssetDatabase.GUIDToAssetPath(g);
            string fname   = Path.GetFileNameWithoutExtension(srcPath).ToLower();

            foreach (var (destName, keyword) in clipMap)
            {
                string destPath = $"{destDir}/{destName}{Path.GetExtension(srcPath)}";
                if (fname.Contains(keyword) && !File.Exists(
                    Path.Combine(Application.dataPath.Replace("Assets",""), destPath)))
                {
                    AssetDatabase.CopyAsset(srcPath, destPath);
                    cnt++;
                    break;
                }
            }
        }

        if (cnt > 0)
        {
            AssetDatabase.Refresh();
            Debug.Log($"[Importador] 🔊 {cnt} clips de audio → Resources/Audio/");
        }
        return cnt;
    }

    // ── Explosiones → SistemaExplosion ───────────────────────────────────

    static int ConectarExplosiones()
    {
        // Buscar prefabs de explosión/fuego importados
        var explosionPrefabs = new List<GameObject>();

        foreach (var g in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            string name = Path.GetFileNameWithoutExtension(path).ToLower();
            if (name.Contains("explo") || name.Contains("fire") || name.Contains("smoke") ||
                name.Contains("blast") || name.Contains("fuego") || name.Contains("humo"))
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (p != null && p.GetComponent<ParticleSystem>() != null)
                    explosionPrefabs.Add(p);
            }
        }

        if (explosionPrefabs.Count == 0) return 0;

        // Guardar lista en un ScriptableObject o asset de referencia
        // SistemaExplosion es static — los prefabs se asignan via Instantiate en runtime
        // Creamos un prefab "ExplosionPrefabs" que SistemaExplosion puede cargar via Resources
        string dir = "Assets/Resources/Efectos";
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets","Resources");
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Resources","Efectos");

        // Copiar prefabs de explosión a Resources/Efectos/
        int cnt = 0;
        foreach (var ep in explosionPrefabs.Take(5))
        {
            string dest = $"{dir}/{ep.name}.prefab";
            if (!File.Exists(Path.Combine(Application.dataPath.Replace("Assets",""), dest)))
            {
                AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(ep), dest);
                cnt++;
            }
        }

        if (cnt > 0) AssetDatabase.Refresh();
        Debug.Log($"[Importador] 💥 {cnt} prefabs de explosión → Resources/Efectos/");
        return cnt;
    }

    // ── Fauna → SistemaFauna ──────────────────────────────────────────────

    static int ConectarFauna()
    {
        // German Shepherd → SistemaFauna (si existe el sistema)
        var fauna = Object.FindFirstObjectByType<SistemaFauna>();
        if (fauna == null) return 0;

        int cnt = 0;
        foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[]{"Assets/GermanShepherd", "Assets/German Shepherd", "Assets/Dog"}))
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (p == null) continue;

            // Conectar vía reflexión al campo de prefab de perro
            var f = typeof(SistemaFauna).GetField("prefabPerro",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && f.GetValue(fauna) == null)
            {
                f.SetValue(fauna, p);
                EditorUtility.SetDirty(fauna);
                cnt++;
                Debug.Log($"[Importador] 🐕 German Shepherd → SistemaFauna.prefabPerro");
                break;
            }
        }

        return cnt;
    }
}
#endif
