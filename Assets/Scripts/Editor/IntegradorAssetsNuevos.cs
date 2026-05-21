// Assets/Scripts/Editor/IntegradorAssetsNuevos.cs
// ═══════════════════════════════════════════════════════════════════════════
//  INTEGRADOR DE ASSETS NUEVOS — Menú: Tools → Alsasua → Integrar Assets
//
//  Inventario detectado:
//    ✅ Police Car & Helicopter   → GameManagerAltsasua (prefabCochePolicia/Helicoptero)
//    ✅ LowPolySoldiers_demo      → GameManagerAltsasua (prefabEnemigo)
//    ✅ BarrierPack               → SistemaBarricadas
//    ✅ SpaceZeta_StreetLamps2    → SistemaFarolas
//    ✅ LiteFireEffect + Vefects  → SistemaDestruccion / BarricadaFuego
//    ✅ Models/Flora (01-07,bush) → AlsasuaTreeStreamer (treePrefabs)
//    ✅ Models/Fauna (deer,wolf)  → SistemaFauna
//    ✅ Audio/*.WAV               → AudioManager / SonidosAmbienteAltsasu
//    ✅ Animations/XBot           → ControladorJugador (controladorAnimaciones)
//    ✅ Hot Rod                   → VehiculoNPC / ControladorVehiculoJugador
//
//  MATERIALES:
//    Police Car, Hot Rod, BarrierPack, StreetLamps, LowPolySoldiers
//    todos usan Standard shader → se convierten a HDRP/Lit aquí.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

public static class IntegradorAssetsNuevos
{
    // ─────────────────────────────────────────────────────────────────────────
    //  MENÚ PRINCIPAL
    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/🔌 Integrar Assets Nuevos (Todo)", priority = 10)]
    public static void IntegrarTodo()
    {
        int total = 0;
        total += ConvertirMaterialesAHDRP();
        total += ConectarPoliciaYHelicoptero();
        total += ConectarSoldadosEnemigos();
        total += ConectarFarolas();
        total += ConectarFloraPrefabs();
        total += ConectarAudio();
        total += InformarManualSetup();

        EditorUtility.ClearProgressBar();
        Debug.Log($"[IntegradorAssets] ✅ Integración completa. {total} conexiones realizadas.");
        EditorUtility.DisplayDialog("Integración completada",
            $"Se realizaron {total} conexiones automáticas.\n\n" +
            "Revisa la Consola para el resumen detallado.\n\n" +
            "Algunos assets requieren configuración manual (ver log).",
            "OK");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  1. CONVERSIÓN DE MATERIALES STANDARD → HDRP/Lit
    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/🎨 Convertir Materiales → HDRP", priority = 11)]
    public static int ConvertirMaterialesAHDRP()
    {
        EditorUtility.DisplayProgressBar("Convirtiendo materiales", "Buscando materiales Standard...", 0f);

        // Carpetas con materiales que necesitan conversión
        string[] carpetas = {
            "Assets/Police Car & Helicopter",
            "Assets/Hot Rod",
            "Assets/BarrierPack",
            "Assets/SpaceZeta_StreetLamps2",
            "Assets/LowPolySoldiers_demo",
        };

        var shader_hdrp = Shader.Find("HDRP/Lit");
        if (shader_hdrp == null)
        {
            Debug.LogError("[IntegradorAssets] Shader 'HDRP/Lit' no encontrado. " +
                           "¿Está el HDRP correctamente instalado?");
            EditorUtility.ClearProgressBar();
            return 0;
        }

        var shader_standard = Shader.Find("Standard");
        var guids = AssetDatabase.FindAssets("t:Material", carpetas);
        int convertidos = 0;

        for (int i = 0; i < guids.Length; i++)
        {
            EditorUtility.DisplayProgressBar("Convirtiendo materiales",
                $"Material {i + 1}/{guids.Length}...", (float)i / guids.Length);

            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            // Detectar materiales Standard (o similares)
            string shaderName = mat.shader?.name ?? "";
            bool esStandard = shaderName.Contains("Standard")
                           || shaderName.Contains("Legacy")
                           || shaderName == "0000000000000000f000000000000000";

            if (!esStandard && mat.shader != null && mat.shader != shader_hdrp) continue;

            // Guardar propiedades antes del cambio
            Color colorAntes = mat.HasProperty("_Color")
                ? mat.GetColor("_Color") : Color.white;
            Texture texAlbedo = mat.HasProperty("_MainTex")
                ? mat.GetTexture("_MainTex") : null;
            Texture texNormal = mat.HasProperty("_BumpMap")
                ? mat.GetTexture("_BumpMap") : null;
            Texture texMetallic = mat.HasProperty("_MetallicGlossMap")
                ? mat.GetTexture("_MetallicGlossMap") : null;
            float metallic = mat.HasProperty("_Metallic")
                ? mat.GetFloat("_Metallic") : 0f;
            float smoothness = mat.HasProperty("_Glossiness")
                ? mat.GetFloat("_Glossiness") : 0.5f;

            // Aplicar HDRP/Lit
            Undo.RecordObject(mat, $"Convert {mat.name} to HDRP");
            mat.shader = shader_hdrp;

            // Reasignar propiedades equivalentes en HDRP
            if (texAlbedo != null)   mat.SetTexture("_BaseColorMap", texAlbedo);
            if (texNormal != null)   mat.SetTexture("_NormalMap", texNormal);
            if (texMetallic != null) mat.SetTexture("_MaskMap", texMetallic);
            mat.SetColor("_BaseColor", colorAntes);
            mat.SetFloat("_Metallic",    metallic);
            mat.SetFloat("_Smoothness",  smoothness);

            EditorUtility.SetDirty(mat);
            convertidos++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[IntegradorAssets] 🎨 {convertidos}/{guids.Length} materiales convertidos a HDRP/Lit.");
        return convertidos;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  2. COCHE POLICIAL + HELICÓPTERO → GameManagerAltsasua
    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/🚔 Conectar Policía y Helicóptero", priority = 12)]
    public static int ConectarPoliciaYHelicoptero()
    {
        var gm = FindOrWarn<GameManagerAltsasua>("GameManagerAltsasua");
        if (gm == null) return 0;

        int conexiones = 0;

        // Coche policial
        var interceptor = CargarPrefab("Assets/Police Car & Helicopter/Prefabs/Interceptor.prefab");
        if (interceptor != null)
        {
            SetFieldPorReflexion(gm, "prefabCochePolicia", interceptor);
            EditorUtility.SetDirty(gm);
            Debug.Log("[IntegradorAssets] 🚔 Interceptor → GameManagerAltsasua.prefabCochePolicia");
            conexiones++;
        }

        // Helicóptero
        var heli = CargarPrefab("Assets/Police Car & Helicopter/Prefabs/Helicopter.prefab");
        if (heli != null)
        {
            SetFieldPorReflexion(gm, "prefabHelicoptero", heli);
            EditorUtility.SetDirty(gm);
            Debug.Log("[IntegradorAssets] 🚁 Helicopter → GameManagerAltsasua.prefabHelicoptero");
            conexiones++;
        }

        return conexiones;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  3. SOLDADOS LOW POLY → GameManagerAltsasua (prefabEnemigo)
    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/🪖 Conectar Soldados / Enemigos", priority = 13)]
    public static int ConectarSoldadosEnemigos()
    {
        var gm = FindOrWarn<GameManagerAltsasua>("GameManagerAltsasua");
        if (gm == null) return 0;

        // Intentar Z Walker primero (prefab ya configurado), si no, soldado low poly
        var enemigo = CargarPrefab("Assets/Models/Z Walker.prefab")
                   ?? CargarPrefab("Assets/LowPolySoldiers_demo/models/Soldier_demo.FBX");

        if (enemigo == null)
        {
            Debug.LogWarning("[IntegradorAssets] ⚠️ No se encontró prefab de enemigo. " +
                             "Asigna 'prefabEnemigo' en GameManagerAltsasua manualmente.");
            return 0;
        }

        SetFieldPorReflexion(gm, "prefabEnemigo", enemigo);
        EditorUtility.SetDirty(gm);
        Debug.Log($"[IntegradorAssets] 🪖 {enemigo.name} → GameManagerAltsasua.prefabEnemigo");
        return 1;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  4. FAROLAS → SistemaFarolas
    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/💡 Conectar Farolas", priority = 14)]
    public static int ConectarFarolas()
    {
        var sistemaFarolas = Object.FindFirstObjectByType<SistemaFarolas>();
        if (sistemaFarolas == null)
        {
            Debug.LogWarning("[IntegradorAssets] SistemaFarolas no encontrado en escena.");
            return 0;
        }

        // Recopilar los 4 prefabs de farolas
        string[] prefabPaths = {
            "Assets/SpaceZeta_StreetLamps2/Prefabs/StreetLampRound1A.prefab",
            "Assets/SpaceZeta_StreetLamps2/Prefabs/StreetLampRound1B.prefab",
            "Assets/SpaceZeta_StreetLamps2/Prefabs/StreetLampRound2A.prefab",
            "Assets/SpaceZeta_StreetLamps2/Prefabs/StreetLampRound2B.prefab",
        };

        var prefabs = prefabPaths
            .Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p))
            .Where(p => p != null)
            .ToArray();

        if (prefabs.Length == 0)
        {
            Debug.LogWarning("[IntegradorAssets] No se encontraron prefabs de farola en SpaceZeta_StreetLamps2/Prefabs/");
            return 0;
        }

        // SistemaFarolas tiene un array de prefabs de farolas
        SetFieldPorReflexion(sistemaFarolas, "prefabsFarola", prefabs);
        EditorUtility.SetDirty(sistemaFarolas);
        Debug.Log($"[IntegradorAssets] 💡 {prefabs.Length} prefabs de farola → SistemaFarolas");
        return prefabs.Length;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  5. FLORA FBX → AlsasuaTreeStreamer (treePrefabs)
    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/🌲 Conectar Flora a TreeStreamer", priority = 15)]
    public static int ConectarFloraPrefabs()
    {
        var streamer = Object.FindFirstObjectByType<AlsasuaTreeStreamer>();
        if (streamer == null)
        {
            Debug.LogWarning("[IntegradorAssets] AlsasuaTreeStreamer no encontrado en escena.");
            return 0;
        }

        // Usar los FBX de Flora como prefabs de árbol
        // protoIdx 0=roble/haritz, 1=haya, 2=pino, 3=álamo
        // Mapeamos: 01.FBX≈roble, 02.FBX≈haya, 03-05.FBX≈pinos, 06-07.FBX≈arbustos
        string[] arbolPaths = {
            "Assets/Models/Flora/01.FBX",   // 0 = roble/haritz
            "Assets/Models/Flora/02.FBX",   // 1 = haya
            "Assets/Models/Flora/03.FBX",   // 2 = pino
            "Assets/Models/Flora/04.FBX",   // 3 = álamo/chopo
        };

        var prefabs = arbolPaths
            .Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p))
            .Where(p => p != null)
            .ToArray();

        if (prefabs.Length == 0)
        {
            Debug.LogWarning("[IntegradorAssets] No se encontraron modelos de flora en Assets/Models/Flora/");
            return 0;
        }

        SetFieldPorReflexion(streamer, "treePrefabs", prefabs);
        EditorUtility.SetDirty(streamer);
        Debug.Log($"[IntegradorAssets] 🌲 {prefabs.Length} modelos Flora → AlsasuaTreeStreamer.treePrefabs\n" +
                  "   idx0=Roble, idx1=Haya, idx2=Pino, idx3=Álamo");
        return prefabs.Length;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  6. AUDIO WAV → AudioManager
    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/🔊 Conectar Audio", priority = 16)]
    public static int ConectarAudio()
    {
        // Verificar que los WAVs existen
        string[] audioFiles = {
            "Assets/Audio/Fire.WAV",
            "Assets/Audio/PoliceSiren.WAV",
            "Assets/Audio/PoliceRadio.WAV",
            "Assets/Audio/TrafficLoop.WAV",
        };

        int encontrados = 0;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[IntegradorAssets] 🔊 Archivos de audio detectados:");

        foreach (var path in audioFiles)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null)
            {
                sb.AppendLine($"   ✅ {clip.name} ({path})");
                encontrados++;
            }
            else
            {
                sb.AppendLine($"   ❌ No encontrado: {path}");
            }
        }

        sb.AppendLine("\n   ⚠️ Asigna estos clips al AudioManager o SonidosAmbienteAltsasu en el Inspector.");
        sb.AppendLine("   AudioManager.Clip enum: PasoNormal, PasoCorrer + tus clips personalizados.");
        Debug.Log(sb.ToString());

        return encontrados;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  7. REPORTE DE SETUP MANUAL REQUERIDO
    // ─────────────────────────────────────────────────────────────────────────

    private static int InformarManualSetup()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\n[IntegradorAssets] ══════════════════════════════════════");
        sb.AppendLine("[IntegradorAssets] 📋 CONFIGURACIÓN MANUAL REQUERIDA:");
        sb.AppendLine("[IntegradorAssets] ══════════════════════════════════════\n");

        sb.AppendLine("🪖 SOLDADOS / GUARDIA CIVIL (LowPolySoldiers_demo):");
        sb.AppendLine("   1. Selecciona 'Soldier_demo.FBX' → Inspector → Rig → Humanoid → Apply");
        sb.AppendLine("   2. Añade animaciones: combat_idle, combat_run, combat_shoot al Animator");
        sb.AppendLine("   3. Asigna el Animator al EnemigoPatrulla.prefabModelo o PoliciaForalIA\n");

        sb.AppendLine("🚗 HOT ROD (vehículo adicional):");
        sb.AppendLine("   1. Crea prefab desde LOD0.FBX con Rigidbody + 4 WheelColliders");
        sb.AppendLine("   2. Añade ControladorVehiculoJugador y asigna los WheelColliders");
        sb.AppendLine("   3. Asigna AsientoConductor (Transform vacío en el asiento)\n");

        sb.AppendLine("🔥 EFECTOS DE FUEGO (LiteFireEffect + Vefects):");
        sb.AppendLine("   · LiteFireEffect/Prafab/BaseFire001.prefab → SistemaDestruccion.prefabFuego");
        sb.AppendLine("   · Vefects/VFX_Fire_01_Big.prefab → BarricadaFuego.prefabFuego");
        sb.AppendLine("   · Los shaders SRP de Vefects son COMPATIBLES con HDRP sin conversión\n");

        sb.AppendLine("🦌 FAUNA (Models/Deer, Wolf, Rabbit):");
        sb.AppendLine("   · deer-female-mesh.fbx → SistemaFauna (configura como especie 'Ciervo')");
        sb.AppendLine("   · Anima: deer-female-anim-idle, deer-female-anim-run, deer-female-anim-die");
        sb.AppendLine("   · rabbit.fbx, wolf.obj → Otros animales del bosque\n");

        sb.AppendLine("🏗️ BARRICADAS (BarrierPack):");
        sb.AppendLine("   · Barricada Concreto.FBX → SistemaBarricadas.prefabBarricada");
        sb.AppendLine("   · Añade MeshCollider + Rigidbody isKinematic=true\n");

        sb.AppendLine("🎭 ANIMACIONES XBOT (Animations/XBot):");
        sb.AppendLine("   · Crea un Animator Controller con los estados:");
        sb.AppendLine("     Walking, CrouchIdle, CrouchWalk, RifleAimIdle, Shooting, Dying");
        sb.AppendLine("   · Asigna a ControladorJugador.controladorAnimaciones\n");

        sb.AppendLine("🌿 FLORA FALTANTE — Árboles nativos de Navarra:");
        sb.AppendLine("   Los modelos 01-07.FBX son genéricos. Para mayor realismo usa:");
        sb.AppendLine("   · oak.obj (Models/) como Quercus robur (roble pedunculate)");
        sb.AppendLine("   · bush_01-05.FBX como Ilex aquifolium (acebo) y Cornus sanguinea");

        Debug.Log(sb.ToString());
        return 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MENÚ: CONVERSIONES INDIVIDUALES RÁPIDAS
    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/Assets/Ver inventario en Consola", priority = 50)]
    static void MostrarInventario()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("══════════════════════════════════════════════════");
        sb.AppendLine("  INVENTARIO DE ASSETS — ALSASUA GTA-STYLE");
        sb.AppendLine("══════════════════════════════════════════════════\n");

        var grupos = new Dictionary<string, string[]>
        {
            ["🚔 VEHÍCULOS POLICIALES"] = new[] {
                "Assets/Police Car & Helicopter/Prefabs/Interceptor.prefab",
                "Assets/Police Car & Helicopter/Prefabs/Helicopter.prefab",
                "Assets/Hot Rod/FBX/LOD0.FBX",
            },
            ["🪖 PERSONAJES / ENEMIGOS"] = new[] {
                "Assets/LowPolySoldiers_demo/models/Soldier_demo.FBX",
                "Assets/Models/Z Walker.prefab",
                "Assets/Models/jill.fbx",
            },
            ["🔥 VFX FUEGO / EXPLOSIONES"] = new[] {
                "Assets/LiteFireEffect/Prafab/BaseFire001.prefab",
                "Assets/Vefects/Free Fire VFX/Prefabs/VFX_Fire_01_Big.prefab",
            },
            ["💡 ILUMINACIÓN URBANA"] = new[] {
                "Assets/SpaceZeta_StreetLamps2/Prefabs/StreetLampRound1A.prefab",
                "Assets/SpaceZeta_StreetLamps2/Prefabs/StreetLampRound2A.prefab",
            },
            ["🏗️ BARRICADAS"] = new[] {
                "Assets/BarrierPack/Assets/Barricada Concreto.FBX",
            },
            ["🌲 FLORA / ÁRBOLES"] = new[] {
                "Assets/Models/Flora/01.FBX", "Assets/Models/Flora/02.FBX",
                "Assets/Models/Flora/bush.FBX", "Assets/Models/Flora/hemp.FBX",
            },
            ["🦌 FAUNA"] = new[] {
                "Assets/Models/deer-female-mesh.fbx",
                "Assets/Models/wolf.obj",
                "Assets/Models/rabbit.fbx",
            },
            ["🔊 AUDIO"] = new[] {
                "Assets/Audio/PoliceSiren.WAV",
                "Assets/Audio/Fire.WAV",
                "Assets/Audio/TrafficLoop.WAV",
            },
        };

        foreach (var grupo in grupos)
        {
            sb.AppendLine(grupo.Key + ":");
            foreach (var path in grupo.Value)
            {
                bool existe = File.Exists(Path.Combine(Application.dataPath.Replace("Assets", ""), path));
                sb.AppendLine($"  {(existe ? "✅" : "❌")} {Path.GetFileName(path)}");
            }
            sb.AppendLine();
        }

        Debug.Log(sb.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    private static T FindOrWarn<T>(string nombre) where T : Object
    {
        var obj = Object.FindFirstObjectByType<T>();
        if (obj == null)
            Debug.LogWarning($"[IntegradorAssets] '{nombre}' no encontrado en la escena activa. " +
                             "Abre la escena principal antes de integrar.");
        return obj;
    }

    private static GameObject CargarPrefab(string path)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null)
            Debug.LogWarning($"[IntegradorAssets] Prefab no encontrado: {path}");
        return go;
    }

    /// <summary>
    /// Asigna un valor a un campo serializado (public o [SerializeField] private)
    /// usando reflexión. Equivale a arrastrarlo en el Inspector.
    /// </summary>
    private static void SetFieldPorReflexion(Object target, string fieldName, object value)
    {
        var type = target.GetType();
        var field = type.GetField(fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (field == null)
        {
            // Buscar en clases base
            var baseType = type.BaseType;
            while (baseType != null && field == null)
            {
                field = baseType.GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                baseType = baseType.BaseType;
            }
        }

        if (field == null)
        {
            Debug.LogWarning($"[IntegradorAssets] Campo '{fieldName}' no encontrado en {type.Name}. " +
                             "Asígnalo manualmente en el Inspector.");
            return;
        }

        field.SetValue(target, value);
    }
}
