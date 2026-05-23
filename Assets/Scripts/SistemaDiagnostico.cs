// Assets/Scripts/SistemaDiagnostico.cs
// ═══════════════════════════════════════════════════════════════════════════
//  DIAGNÓSTICO DE ARRANQUE — detecta todos los bugs al entrar en Play Mode
//
//  Al arrancar muestra en Consola y en pantalla una lista de:
//    ✅ Sistemas correctamente inicializados
//    ❌ Sistemas con errores o null
//    ⚠  Configuraciones subóptimas
//
//  Permite al usuario saber exactamente qué arreglar en Unity
//  antes de reportar un bug.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text;

[DefaultExecutionOrder(200)] // después de que todo arranque
public class SistemaDiagnostico : SingletonMono<SistemaDiagnostico>
{
    bool   _mostrandoPanel;
    string _informe = "";
    GUIStyle _estiloPanel, _estiloOK, _estiloError, _estiloWarn, _estiloTitulo;
    bool   _estilosInit;
    Vector2 _scroll;

    void Start() => StartCoroutine(DiagnosticarTodo());

    IEnumerator DiagnosticarTodo()
    {
        // Esperar a que todos los sistemas arranquen
        yield return new WaitForSeconds(4f);

        var sb = new StringBuilder();
        var errores = new List<string>();
        var warnings = new List<string>();

        sb.AppendLine("═══ DIAGNÓSTICO ALTSASUA SIMULATOR ═══\n");

        // ── Core ──────────────────────────────────────────────────────────
        sb.AppendLine("CORE:");
        Chequear(sb, errores, "AltsasuCore",           AltsasuCore.I != null);
        Chequear(sb, errores, "Jugador (tag=Player)",  AltsasuCore.Jugador != null);
        Chequear(sb, warnings,"FPS actual",            SistemaOptimizacion.FPSActual >= 30f,
            $"{SistemaOptimizacion.FPSActual:F0} fps");

        // ── Terreno ───────────────────────────────────────────────────────
        sb.AppendLine("\nTERRENO:");
        bool terrain = Terrain.activeTerrain != null;
        Chequear(sb, errores, "Terrain activo", terrain);
        if (terrain)
        {
            var t = Terrain.activeTerrain.terrainData;
            Chequear(sb, warnings, "TerrainLayers asignados",
                t.terrainLayers.Length > 0, $"{t.terrainLayers.Length} layers");
            Chequear(sb, warnings, "DEM cargado (heightmap > plano)",
                t.heightmapResolution == 1025, $"res={t.heightmapResolution}");
        }

        // ── NavMesh ───────────────────────────────────────────────────────
        sb.AppendLine("\nNAVMESH:");
        Chequear(sb, errores, "SistemaNavMesh",   SistemaNavMesh.Instance != null);
        Chequear(sb, errores, "NavMesh horneado", SistemaNavMesh.EstaListo,
            SistemaNavMesh.EstaListo ? "OK" : "Espera ~2s después de Play");
        int triCount = UnityEngine.AI.NavMesh.CalculateTriangulation().vertices.Length;
        Chequear(sb, warnings, $"NavMesh vértices ({triCount})",
            triCount > 100, triCount > 100 ? "OK" : "NavMesh vacío — terrain sin collider?");

        // ── Sistemas de gameplay ───────────────────────────────────────────
        sb.AppendLine("\nGAMEPLAY:");
        Chequear(sb, errores, "GameManager",       GameManagerAltsasua.Instance != null);
        Chequear(sb, errores, "SistemaMisiones",   SistemaMisiones.Instance != null);
        Chequear(sb, errores, "SistemaApoyoPopular", SistemaApoyoPopular.Instance != null);
        Chequear(sb, errores, "SistemaGuardado",   SistemaGuardado.Instance != null);
        Chequear(sb, errores, "AudioManager",      AudioManager.I != null);
        Chequear(sb, errores, "SistemaLogros",     SistemaLogros.Instance != null);

        // ── Mundo ─────────────────────────────────────────────────────────
        sb.AppendLine("\nMUNDO (Zone Streaming):");
        Chequear(sb, errores, "SistemaZonas activo",        SistemaZonas.Instance != null);
        Chequear(sb, warnings,"GeneradorMundoOSM indexado", GeneradorMundoOSM.MundoListo);
        Chequear(sb, warnings,"SistemaEdificiosAAA listo",  SistemaEdificiosAAA.Listo);
        Chequear(sb, warnings,"SistemaTerreno pintado",     SistemaTerreno.Listo);

        // Contar edificios activos en escena (zona streaming — no hay parent único)
        var zonaRoots = GameObject.FindGameObjectsWithTag("Untagged");
        int nEdifActivos = 0;
        foreach (var go in zonaRoots)
            if (go.name.StartsWith("Zona_")) nEdifActivos += go.transform.childCount;
        Chequear(sb, warnings, $"Edificios en zonas activas ({nEdifActivos})",
            nEdifActivos > 0 || !GeneradorMundoOSM.MundoListo,
            nEdifActivos > 0 ? "OK" : "Jugador fuera del área o indexado incompleto");

        // ── Animaciones ───────────────────────────────────────────────────
        sb.AppendLine("\nANIMACIONES:");
        var jugGO = GameObject.FindGameObjectWithTag("Player");
        Chequear(sb, errores, "Jugador tiene tag 'Player'", jugGO != null);
        if (jugGO != null)
        {
            var anim = jugGO.GetComponent<Animator>() ?? jugGO.GetComponentInChildren<Animator>();
            Chequear(sb, warnings, "Jugador tiene Animator", anim != null);
            if (anim != null)
            {
                Chequear(sb, warnings, "Controller asignado",
                    anim.runtimeAnimatorController != null,
                    anim.runtimeAnimatorController?.name ?? "NULL");
                Chequear(sb, warnings, "Parámetro VelocidadMovimiento existe",
                    anim.runtimeAnimatorController != null &&
                    TieneParametro(anim, "VelocidadMovimiento"));
            }
        }

        // ── Audio ─────────────────────────────────────────────────────────
        sb.AppendLine("\nAUDIO:");
        var clips = Resources.LoadAll<AudioClip>("Audio");
        Chequear(sb, warnings, $"Clips en Resources/Audio ({clips.Length})",
            clips.Length >= 10, $"{clips.Length} clips — mínimo 10 para experiencia completa");
        Chequear(sb, warnings, "AudioListener en escena",
            FindFirstObjectByType<AudioListener>() != null);

        // ── Cámara y render ───────────────────────────────────────────────
        sb.AppendLine("\nRENDER:");
        var cam = Camera.main;
        Chequear(sb, errores, "Main Camera existe", cam != null);
        if (cam != null)
        {
            Chequear(sb, warnings, "HDRP camera data",
                cam.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>() != null);
            Chequear(sb, warnings, $"FOV configurado ({cam.fieldOfView:F0}°)",
                cam.fieldOfView >= 60f && cam.fieldOfView <= 90f);
        }
        var pipeline = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
        Chequear(sb, errores, "HDRP Pipeline asignado",
            pipeline != null && pipeline.GetType().Name.Contains("HDRender"),
            pipeline?.GetType().Name ?? "NULL — materiales serán magenta");

        // ── Resumen ───────────────────────────────────────────────────────
        sb.AppendLine($"\n═══ RESUMEN ═══");
        sb.AppendLine($"Errores:    {errores.Count}");
        sb.AppendLine($"Warnings:   {warnings.Count}");
        sb.AppendLine(errores.Count == 0 ? "✅ Sin errores críticos" : "❌ Hay errores críticos");

        _informe = sb.ToString();
        _mostrandoPanel = errores.Count > 0 || warnings.Count > 3;

        // Log siempre
        Debug.Log($"[Diagnóstico]\n{_informe}");

        if (errores.Count > 0)
        {
            Debug.LogError($"[Diagnóstico] {errores.Count} errores críticos:\n" +
                string.Join("\n", errores));
        }

        AlsasuaLogger.Info("Diagnostico",
            $"Completado — {errores.Count} errores, {warnings.Count} warnings");
    }

    static void Chequear(StringBuilder sb, List<string> bucket, string nombre, bool ok,
                          string detalle = "", List<string> errors = null)
    {
        string icono = ok ? "✅" : (bucket == errors || errors == null ? "❌" : "⚠ ");
        string linea = $"  {icono} {nombre}" + (detalle.Length > 0 ? $"  ({detalle})" : "");
        sb.AppendLine(linea);
        if (!ok) bucket.Add($"{nombre}: {detalle}");
    }

    bool TieneParametro(Animator anim, string param)
    {
        if (anim.runtimeAnimatorController == null) return false;
        foreach (var p in anim.parameters)
            if (p.name == param) return true;
        return false;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  UI — panel de diagnóstico (D para abrir/cerrar)
    // ════════════════════════════════════════════════════════════════════════

    void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.f1Key.wasPressedThisFrame)
            _mostrandoPanel = !_mostrandoPanel;
    }

    void OnGUI()
    {
        if (!_mostrandoPanel) return;
        InicializarEstilos();

        float w = 540f, h = Mathf.Min(Screen.height * 0.8f, 600f);
        float x = (Screen.width - w) / 2f;
        float y = (Screen.height - h) / 2f;

        // Fondo
        GUI.color = new Color(0,0,0,0.92f);
        GUI.DrawTexture(new Rect(x-8, y-8, w+16, h+16), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(x, y, w, h));
        GUI.Label(new Rect(0, 0, w, 28), "F1 = cerrar | DIAGNÓSTICO DE ARRANQUE", _estiloTitulo);
        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(h - 36));
        GUI.Label(new Rect(0, 0, w - 20, 2000), _informe, _estiloPanel);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void InicializarEstilos()
    {
        if (_estilosInit) return; _estilosInit = true;
        _estiloPanel  = new GUIStyle(GUI.skin.label)
            { fontSize = 12, wordWrap = true, richText = true,
              normal = { textColor = new Color(0.88f, 0.88f, 0.92f) } };
        _estiloTitulo = new GUIStyle(GUI.skin.label)
            { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
              normal   = { textColor = new Color(1f, 0.92f, 0.4f) } };
        _estiloOK     = new GUIStyle(_estiloPanel) { normal = { textColor = Color.green } };
        _estiloError  = new GUIStyle(_estiloPanel) { normal = { textColor = Color.red } };
        _estiloWarn   = new GUIStyle(_estiloPanel) { normal = { textColor = Color.yellow } };
    }
}
