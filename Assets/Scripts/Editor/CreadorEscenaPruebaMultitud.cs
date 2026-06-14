// Assets/Scripts/Editor/CreadorEscenaPruebaMultitud.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CREADOR ESCENA PRUEBA MULTITUD — Tools/Alsasua/Escena
//
//  Construye Prueba_Multitud.unity: un banco de pruebas AISLADO para validar
//  SistemaMultitudBRG (la multitud de fondo por BatchRendererGroup, 5.000
//  agentes) pulsando Play, SIN tocar Alsasua_Main ni SistemaManifestacion.
//
//  Es deliberadamente autosuficiente: no arranca SceneBootstrapper, ni AltsasuCore,
//  ni el mosaico de terreno. Como no hay ITerrainService registrado, el sistema
//  cae a TerrenoGlobal.AlturaMundo() → 0, así que los agentes marchan sobre un
//  plano plano en y=0 (suficiente para ver flocking, render BRG y rendimiento).
//
//  Jerarquía creada:
//    Multitud_BRG   — SistemaMultitudBRG (5.000 agentes, objetivo = Plaza)
//    Plaza_Objetivo — destino de la marcha, 80 m al frente (+Z)
//    Suelo          — plano plano en y=0 (referencia visual)
//    Sol            — Directional Light (para que el HDRP/Lit se ilumine)
//    Camara_Prueba  — vista cenital-lateral que encuadra toda la marcha
//
//  Mide rendimiento con el Stats del Game view o el Profiler: 1 draw command
//  para toda la multitud, lógica en jobs Burst.
// ═══════════════════════════════════════════════════════════════════════════

using System.IO;
using System.Reflection;
using Alsasua.Crowd;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class CreadorEscenaPruebaMultitud
{
    const string RUTA_ESCENA = "Assets/#Scenes/Prueba_Multitud.unity";

    // Geometría del banco de pruebas (espacio local, origen = spawn de la marcha).
    static readonly Vector3 SPAWN    = new Vector3(0f, 0f, 0f);
    static readonly Vector3 OBJETIVO = new Vector3(0f, 0f, 80f);   // 80 m al frente (+Z)

    [MenuItem("Tools/Alsasua/Escena/🧪 Crear Escena Prueba Multitud", priority = 12)]
    public static void Crear()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var escena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── 1. Objetivo de la marcha (la "plaza") ─────────────────────────
        var plaza = new GameObject("Plaza_Objetivo");
        plaza.transform.position = OBJETIVO;

        // ── 2. Multitud BRG ────────────────────────────────────────────────
        var multitudGO = new GameObject("Multitud_BRG");
        multitudGO.transform.position = SPAWN;
        var sis = multitudGO.AddComponent<SistemaMultitudBRG>();
        Set(sis, "cantidadAgentes",  5000);
        Set(sis, "objetivo",         plaza.transform);              // Transform cercano
        Set(sis, "objetivoFallback", OBJETIVO);                     // por si se desasigna
        Set(sis, "zonaSpawn",        new Vector3(70f, 0f, 40f));    // frente ancho, fondo corto

        // ── 3. Suelo plano de referencia (y=0, donde caen los agentes) ─────
        var suelo = GameObject.CreatePrimitive(PrimitiveType.Plane);
        suelo.name = "Suelo";
        suelo.transform.position   = new Vector3(0f, 0f, 40f);      // centrado en la marcha
        suelo.transform.localScale = new Vector3(40f, 1f, 40f);     // 400×400 m
        Object.DestroyImmediate(suelo.GetComponent<MeshCollider>()); // no hace falta física
        var shLit = Shader.Find("HDRP/Lit");                         // el material default sale magenta en HDRP
        if (shLit != null)
        {
            var matSuelo = new Material(shLit) { name = "MatSuelo_Prueba" };
            matSuelo.SetColor("_BaseColor", new Color(0.22f, 0.23f, 0.25f));
            suelo.GetComponent<MeshRenderer>().sharedMaterial = matSuelo;
        }

        // ── 4. Sol (para que el material HDRP/Lit no salga negro) ──────────
        var solGO = new GameObject("Sol");
        var luz = solGO.AddComponent<Light>();
        luz.type      = LightType.Directional;
        luz.intensity = 2.2f;
        luz.color     = new Color(1f, 0.96f, 0.9f);
        solGO.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

        // ── 4b. Volume global — cielo/niebla/exposición del proyecto ───────
        //  Una escena HDRP vacía sin Volume se queda a merced solo del
        //  DefaultSettingsVolumeProfile; reutilizamos el perfil de cielo+fog
        //  del proyecto para que la exposición case con el resto del juego
        //  (evita el clásico "todo negro"). Si no carga, el default basta.
        var perfil = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
            "Assets/Settings/SkyandFogSettingsProfile.asset");
        if (perfil != null)
        {
            var volGO = new GameObject("Volume_Cielo");
            var vol = volGO.AddComponent<Volume>();
            vol.isGlobal       = true;
            vol.sharedProfile  = perfil;
        }
        else
        {
            Debug.LogWarning("[Escena] SkyandFogSettingsProfile no encontrado; " +
                "la escena usará el DefaultSettingsVolumeProfile de HDRP.");
        }

        // ── 5. Cámara de prueba — vista cenital-lateral de toda la marcha ──
        var camGO = new GameObject("Camara_Prueba");
        var cam = camGO.AddComponent<Camera>();
        cam.tag = "MainCamera";
        camGO.transform.position = new Vector3(75f, 45f, -25f);
        camGO.transform.rotation = Quaternion.LookRotation(
            (new Vector3(0f, 0f, 40f) - camGO.transform.position).normalized, Vector3.up);
        camGO.AddComponent<AudioListener>();

        // ── Guardar + build settings ───────────────────────────────────────
        Directory.CreateDirectory(Path.GetDirectoryName(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", RUTA_ESCENA))));
        EditorSceneManager.SaveScene(escena, RUTA_ESCENA);
        AnadirBuildSettings(RUTA_ESCENA);
        AssetDatabase.Refresh();

        Debug.Log("[Escena] ✅ Prueba_Multitud.unity creada.");
        EditorUtility.DisplayDialog("🧪 Escena Prueba Multitud",
            "Prueba_Multitud.unity lista (banco aislado):\n\n" +
            "• 5.000 agentes BRG marchando hacia Plaza_Objetivo (+Z)\n" +
            "• Suelo plano en y=0, sol y cámara ya encuadrados\n" +
            "• No arranca el mundo ni el sistema de manifestación\n\n" +
            "Dale Play. Abre Stats (Game view) y verás 1 draw call\n" +
            "para toda la multitud; la lógica corre en jobs Burst.", "OK");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    static void Set(object target, string campo, object valor)
    {
        if (target == null || valor == null) return;
        var f = target.GetType().GetField(campo,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        f?.SetValue(target, valor);
    }

    static void AnadirBuildSettings(string ruta)
    {
        var scenes = EditorBuildSettings.scenes;
        foreach (var s in scenes)
            if (s.path == ruta) return;
        var lista = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes);
        lista.Add(new EditorBuildSettingsScene(ruta, true));
        EditorBuildSettings.scenes = lista.ToArray();
    }
}
