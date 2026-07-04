// Assets/Scripts/Editor/ActivadorAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ACTIVADOR AAA — automatiza la activación de la deuda AAA staged.
//
//  Hace por ti TODO lo mecánico de la GUIA_ACTIVACION_DETALLADA.md:
//    1. Mueve los scripts de las carpetas `~` a sus capas (las hace compilables).
//    2. (tras recompilar) crea el asset SintoniaAltsasu, ParanoiaGCConfig y los
//       materiales, y monta los GameObjects de gameplay / clipmap / impostores
//       con sus componentes y referencias ya wireadas.
//
//  Lo que NO puede hacer (queda manual, ver la guía):
//    · Los 2 Shader Graphs (clipmap Lit y impostor Unlit). Crea los materiales con
//      un shader placeholder y loguea dónde asignar el ShaderGraph definitivo.
//    · El bake del atlas de impostores y el Play/validación.
//    · Insertar las llamadas `SistemaTestigos.ReportarDelito(...)` en el código de
//      delito (1 línea por sitio; ver la guía, fase 4.5).
//
//  Diseño:
//    · Este script SOLO usa UnityEditor + reflexión → compila aunque los staged
//      aún no estén movidos (no referencia sus tipos directamente).
//    · IDEMPOTENTE: cada paso comprueba si ya está hecho y se salta. Puedes
//      relanzarlo sin miedo.
//    · POR FASES: si una fase falla, las demás siguen y puedes relanzar solo esa.
//    · RESUME: mover scripts dispara recompilación (domain reload); la creación de
//      assets/escena se reanuda sola tras recompilar (SessionState + InitializeOnLoad).
//
//  ⚠ No se ha podido validar en el editor (se escribió sin Unity). Si al mover los
//  scripts Unity entra en Modo Seguro, hay un error de compilación en algún staged:
//  arréglalo y vuelve a lanzar. Empieza por "▶ TODO" o ve fase a fase.
// ═══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ActivadorAAA
{
    const string RAIZ = "Assets/Scripts";
    const string KEY_RESUME = "ActivadorAAA.resumeCreacion";
    const string SINTONIA_PATH = "Assets/AlsasuaData/SintoniaAltsasu.asset";
    const string PARANOIA_CFG_PATH = "Assets/AlsasuaData/ParanoiaGCConfig.asset";

    // (carpeta staged, fichero, carpeta destino bajo Assets/Scripts)
    static readonly (string src, string file, string dst)[] MOVIMIENTOS =
    {
        // Clipmap V3 → Systems (ese asmdef ya referencia Newtonsoft y Core)
        ("_ClipmapV3~", "ConstructorMallaClipmap.cs",     "Systems/ClipmapV3"),
        ("_ClipmapV3~", "ClipmapTerrenoV3.cs",            "Systems/ClipmapV3"),
        ("_ClipmapV3~", "MuestreadorHeightmapV3.cs",      "Systems/ClipmapV3"),
        ("_ClipmapV3~", "CargadorTexturaHeightmapV3.cs",  "Systems/ClipmapV3"),
        ("_ClipmapV3~", "MuestreadorAlturaClipmapV3.cs",  "Systems/ClipmapV3"),
        ("_ClipmapV3~", "ColliderParcheClipmapV3.cs",     "Systems/ClipmapV3"),
        ("_ClipmapV3~", "ClipmapDisplacement.hlsl",       "Systems/ClipmapV3"),
        // Impostores → Runtime (+ baker a Editor)
        ("_Impostores~", "ImpostorAtlasSO.cs",   "Runtime/Impostores"),
        ("_Impostores~", "ImpostorBillboard.cs", "Runtime/Impostores"),
        ("_Impostores~", "GestorImpostores.cs",  "Runtime/Impostores"),
        ("_Impostores~", "ImpostorUnlit.shader", "Runtime/Impostores"),
        ("_Impostores~", "BakeadorImpostores.cs","Editor"),
        // Gameplay → Runtime
        ("_ParanoiaGC~", "ParanoiaGCConfig.cs",                "Runtime/ParanoiaGC"),
        ("_ParanoiaGC~", "ConvertibleGuardiaCivil.cs",         "Runtime/ParanoiaGC"),
        ("_ParanoiaGC~", "CerebroGuardiaCivil.cs",             "Runtime/ParanoiaGC"),
        ("_ParanoiaGC~", "PatrullaGuardiaCivil.cs",            "Runtime/ParanoiaGC"),
        ("_ParanoiaGC~", "SistemaParanoiaGuardiaCivil.cs",     "Runtime/ParanoiaGC"),
        ("_ParanoiaGC~", "HUDParanoia.cs",                     "Runtime/ParanoiaGC"),
        ("_ControlesGC~", "ControlGuardiaCivil.cs",            "Runtime/ControlesGC"),
        ("_ControlesGC~", "SistemaControlesGC.cs",             "Runtime/ControlesGC"),
        ("_Testigos~", "TestigoNPC.cs",                        "Runtime/Testigos"),
        ("_Testigos~", "SistemaTestigos.cs",                   "Runtime/Testigos"),
        ("_Coartada~", "ZonaCoartada.cs",                      "Runtime/Coartada"),
        ("_Coartada~", "SistemaCoartada.cs",                   "Runtime/Coartada"),
        // Panel de tuning → Core (lo leen varios sistemas)
        ("_Tuning~", "SintoniaAltsasu.cs",                     "Core"),
        // (Misiones data-driven NO se mueven aquí: opcionales y su parser puede
        //  necesitar Newtonsoft en el asmdef de Runtime → se activan a mano, fase 5.)
    };

    static ActivadorAAA()
    {
        if (SessionState.GetBool(KEY_RESUME, false))
        {
            SessionState.SetBool(KEY_RESUME, false);
            EditorApplication.delayCall += () =>
            {
                Debug.Log("[ActivadorAAA] Recompilado tras mover scripts. Creando assets y escena…");
                FaseAssetsYEscena();
            };
        }
    }

    // ── ▶ TODO ────────────────────────────────────────────────────────────────
    [MenuItem("Tools/Alsasua/Activar AAA/▶ TODO (mover + crear + wirear)", priority = 0)]
    public static void TodoElProceso()
    {
        if (!EditorUtility.DisplayDialog("Activar AAA",
            "Voy a:\n• mover los scripts staged (~) a sus capas\n• crear assets y materiales\n• montar los GameObjects wireados\n\n" +
            "Tras mover, Unity recompila y el proceso CONTINÚA solo.\n\n" +
            "⚠ Si el asmdef de Systems no referencia Newtonsoft.Json, los 2 ficheros del clipmap que " +
            "leen JSON no compilarán (Modo Seguro). Si pasa: en el .asmdef de Systems activa 'Override " +
            "References' y añade 'Newtonsoft.Json.dll', o usa el botón de la guía. ¿Seguir?",
            "Sí, activar", "Cancelar")) return;

        int movidos = MoverStaged();
        if (movidos > 0)
        {
            // La creación de assets/escena se reanuda tras la recompilación.
            SessionState.SetBool(KEY_RESUME, true);
            AssetDatabase.Refresh();
        }
        else
        {
            // Nada que mover (ya activado): crea/wirea directamente.
            FaseAssetsYEscena();
        }
    }

    // ── FASE 1: mover scripts ─────────────────────────────────────────────────
    [MenuItem("Tools/Alsasua/Activar AAA/1. Mover scripts staged", priority = 20)]
    public static int MoverStaged()
    {
        int n = 0;
        foreach (var (src, file, dst) in MOVIMIENTOS)
        {
            string srcAbs = Path.Combine(RutaAbs(RAIZ), src, file);
            string dstDirAbs = Path.Combine(RutaAbs(RAIZ), dst);
            string dstAbs = Path.Combine(dstDirAbs, file);

            if (File.Exists(dstAbs)) continue;          // ya movido
            if (!File.Exists(srcAbs)) continue;         // no existe (o ya movido)

            Directory.CreateDirectory(dstDirAbs);
            try
            {
                File.Move(srcAbs, dstAbs);
                n++;
                Debug.Log($"[ActivadorAAA] Movido: {src}/{file} → Scripts/{dst}/");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ActivadorAAA] No se pudo mover {src}/{file}: {e.Message}");
            }
        }
        Debug.Log($"[ActivadorAAA] FASE 1 — {n} ficheros movidos. Recompilando…");
        if (n > 0) AssetDatabase.Refresh();
        return n;
    }

    // ── FASE 2: assets + materiales + escena (tras recompilar) ────────────────
    [MenuItem("Tools/Alsasua/Activar AAA/2. Crear assets y montar escena", priority = 21)]
    public static void FaseAssetsYEscena()
    {
        var sintonia = CrearSO("SintoniaAltsasu", SINTONIA_PATH);
        var paranoiaCfg = CrearSO("ParanoiaGCConfig", PARANOIA_CFG_PATH);

        MontarGameplay(sintonia, paranoiaCfg);
        MontarClipmap();
        MontarImpostores();

        AssetDatabase.SaveAssets();
        if (!Application.isPlaying)
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[ActivadorAAA] ✅ FASE 2 completa. Pendiente MANUAL: 2 Shader Graphs, " +
                  "bake de impostores, líneas ReportarDelito y Play/validación (ver GUIA_ACTIVACION_DETALLADA.md).");
        EditorUtility.DisplayDialog("Activar AAA",
            "✅ Hecho lo automatizable:\n• scripts movidos\n• SintoniaAltsasu + ParanoiaGCConfig\n• GameObjects de gameplay, clipmap e impostores wireados\n\n" +
            "MANUAL pendiente (ver guía):\n• 2 Shader Graphs (clipmap Lit, impostor Unlit)\n• bake del atlas\n• líneas ReportarDelito\n• Play + gates", "Ok");
    }

    // ── Montaje de escena (todo por reflexión, null-safe) ─────────────────────
    static void MontarGameplay(UnityEngine.Object sintonia, UnityEngine.Object paranoiaCfg)
    {
        var go = GameObjectUnico("AAA_Gameplay");

        var paranoia = AddComp(go, "SistemaParanoiaGuardiaCivil");
        SetField(paranoia, "config", paranoiaCfg);
        SetField(paranoia, "sintonia", sintonia);

        SetField(AddComp(go, "SistemaControlesGC"), "sintonia", sintonia);
        SetField(AddComp(go, "SistemaTestigos"),    "sintonia", sintonia);
        SetField(AddComp(go, "SistemaCoartada"),    "sintonia", sintonia);

        Debug.Log("[ActivadorAAA] Gameplay montado en 'AAA_Gameplay'. " +
                  "TODO manual: asignar capas (capaAutoridad/capaObstaculos), colocar ControlGuardiaCivil " +
                  "en los pasos, ZonaCoartada en refugios, TestigoNPC/ConvertibleGuardiaCivil en NPCs.");
    }

    static void MontarClipmap()
    {
        var go = GameObjectUnico("AAA_ClipmapV3");
        var holder = AddComp(go, "ClipmapTerrenoV3");
        AddComp(go, "CargadorTexturaHeightmapV3");
        AddComp(go, "MuestreadorAlturaClipmapV3");
        var collider = AddComp(go, "ColliderParcheClipmapV3");

        // Busca el material del ActivadorClipmapV3 si ya existe; si no, crea uno con el ShaderGraph correcto.
        var matPath = "Assets/Materials/Terrain/M_ClipmapV3_Terrain.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            // ShaderGraph ya existe en Assets/Shaders/ClipmapV3_Terrain.shadergraph → "Alsasua/Terrain/ClipmapV3_Terrain"
            var sh = Shader.Find("Alsasua/Terrain/ClipmapV3_Terrain")
                  ?? Shader.Find("Shader Graphs/ClipmapTerreno")
                  ?? Shader.Find("HDRP/Lit")
                  ?? Shader.Find("Standard");
            mat = new Material(sh) { name = "M_ClipmapV3_Terrain" };
            Directory.CreateDirectory(Path.GetDirectoryName(
                Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                             matPath.Replace('/', Path.DirectorySeparatorChar))));
            AssetDatabase.CreateAsset(mat, matPath);
        }
        SetField(holder, "material", mat);

        var jugador = BuscarJugador();
        if (jugador != null) { SetField(holder, "jugador", jugador); SetField(collider, "jugador", jugador); }

        Debug.Log("[ActivadorAAA] Clipmap montado en 'AAA_ClipmapV3'. ShaderGraph 'Alsasua/Terrain/ClipmapV3_Terrain' ya existe.");
    }

    static void MontarImpostores()
    {
        var go = GameObjectUnico("AAA_Impostores");
        AddComp(go, "GestorImpostores");
        Debug.Log("[ActivadorAAA] Impostores montado en 'AAA_Impostores'. TODO manual: recrear el Shader Graph " +
                  "HDRP/Unlit, bakear el atlas (Tools ▸ Alsasua ▸ Impostores ▸ Bake) y asignar 'atlas' al GestorImpostores.");
    }

    // ── Utilidades ────────────────────────────────────────────────────────────
    static string RutaAbs(string rel) =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, rel.Replace('/', Path.DirectorySeparatorChar));

    static GameObject GameObjectUnico(string nombre)
    {
        var ex = GameObject.Find(nombre);
        return ex != null ? ex : new GameObject(nombre);
    }

    static Component AddComp(GameObject go, string typeName)
    {
        var t = Buscar(typeName);
        if (t == null) { Debug.LogWarning($"[ActivadorAAA] Tipo no encontrado (¿no compiló?): {typeName}"); return null; }
        var ya = go.GetComponent(t);
        return ya != null ? ya : go.AddComponent(t);
    }

    static UnityEngine.Object CrearSO(string typeName, string path)
    {
        var ya = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
        if (ya != null) return ya;
        var t = Buscar(typeName);
        if (t == null || !typeof(ScriptableObject).IsAssignableFrom(t))
        { Debug.LogWarning($"[ActivadorAAA] No se pudo crear SO {typeName} (tipo no disponible)."); return null; }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var so = ScriptableObject.CreateInstance(t);
        AssetDatabase.CreateAsset(so, path);
        Debug.Log($"[ActivadorAAA] Asset creado: {path}");
        return so;
    }

    static void SetField(Component c, string field, object val)
    {
        if (c == null || val == null) return;
        var f = c.GetType().GetField(field, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
        if (f == null) { Debug.LogWarning($"[ActivadorAAA] {c.GetType().Name} no tiene campo '{field}'."); return; }
        if (!f.FieldType.IsInstanceOfType(val)) return;
        f.SetValue(c, val);
    }

    static Component BuscarJugador()
    {
        var t = Buscar("ControladorJugador");
        if (t == null) return null;
        return UnityEngine.Object.FindFirstObjectByType(t) as Component;
    }

    static readonly Dictionary<string, Type> _cache = new();
    static Type Buscar(string nombreSimple)
    {
        if (_cache.TryGetValue(nombreSimple, out var hit)) return hit;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] tipos;
            try { tipos = asm.GetTypes(); } catch { continue; }
            foreach (var t in tipos)
                if (t.Name == nombreSimple) { _cache[nombreSimple] = t; return t; }
        }
        return null;
    }
}
