using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using System.IO;

/// <summary>
/// AltsasuSceneSetup — Monta automáticamente la escena GTA de Altsasu.
///
/// CÓMO USAR:
///   Menú Unity → Altsasu GTA → Montar Escena Completa
///
/// Qué hace:
///   1. Crea el GameManager con todos los componentes y prefabs asignados
///   2. Crea spawn points de jugador, policía y enemigos alrededor del terreno
///   3. Añade OptimizadorTerreno al mesh de CloudCompare y asigna textura
///   4. Crea un HUD Canvas con dinero, estrellas de búsqueda y vida
///   5. Siembra árboles Tree10 por el escenario
/// </summary>
public static class AltsasuSceneSetup
{
    // ─── Rutas de assets conocidas ────────────────────────────────────────────
    const string PATH_INTERCEPTOR   = "Assets/Police Car & Helicopter/Prefabs/Interceptor.prefab";
    const string PATH_HELICOPTERO   = "Assets/Police Car & Helicopter/Prefabs/Helicopter.prefab";
    const string PATH_ENEMIGO       = "Assets/Models/Z Walker.prefab";
    const string PATH_DEATH_PREFAB  = "Assets/Models/Death Prefab.prefab";
    const string PATH_HITMARKER     = "Assets/#Tools/Hitmarker Canvas.prefab";
    const string PATH_TEXTURA_CC    = "Assets/EscenarioSuelo/Alsasua_Color.png";
    const string PATH_TERRENO_OBJ   = "Assets/EscenarioSuelo/GroundPointsMesh.obj";
    const string PATH_TREE_1        = "Assets/Tree10/Tree10_1.prefab";
    const string PATH_TREE_2        = "Assets/Tree10/Tree10_2.prefab";
    const string PATH_TREE_3        = "Assets/Tree10/Tree10_3.prefab";
    const string PATH_TREE_4        = "Assets/Tree10/Tree10_4.prefab";
    const string PATH_TREE_5        = "Assets/Tree10/Tree10_5.prefab";

    // ─── Posiciones spawn (adaptadas al centro del terreno de Alsasua) ────────
    // El terreno CloudCompare está centrado cerca del origen.
    // Ajusta estos valores si tu OBJ está desplazado.
    static readonly Vector3 CENTRO_ALSASUA = new Vector3(0f, 2f, 0f);

    // =========================================================================
    //  MENÚ PRINCIPAL
    // =========================================================================

    [MenuItem("Altsasu GTA/Montar Escena Completa", false, 1)]
    static void MontarEscenaCompleta()
    {
        if (!EditorUtility.DisplayDialog("Altsasu GTA - Setup",
            "Esto creará el GameManager, HUD, spawn points y configurará el terreno.\n\n¿Continuar?",
            "Sí, montar", "Cancelar"))
            return;

        Undo.SetCurrentGroupName("Altsasu GTA Scene Setup");
        int undoGroup = Undo.GetCurrentGroup();

        int paso = 0;
        int total = 5;

        EditorUtility.DisplayProgressBar("Altsasu GTA Setup", "Configurando terreno...", ++paso / (float)total);
        ConfigurarTerreno();

        EditorUtility.DisplayProgressBar("Altsasu GTA Setup", "Creando spawn points...", ++paso / (float)total);
        var spawns = CrearSpawnPoints();

        EditorUtility.DisplayProgressBar("Altsasu GTA Setup", "Creando GameManager...", ++paso / (float)total);
        CrearGameManager(spawns);

        EditorUtility.DisplayProgressBar("Altsasu GTA Setup", "Creando HUD Canvas...", ++paso / (float)total);
        CrearHUD();

        EditorUtility.DisplayProgressBar("Altsasu GTA Setup", "Sembrando vegetación...", ++paso / (float)total);
        SembrarArboles();

        EditorUtility.ClearProgressBar();
        Undo.CollapseUndoOperations(undoGroup);

        EditorUtility.DisplayDialog("Altsasu GTA",
            "✅ Escena montada correctamente.\n\n" +
            "• Pulsa Play para probar.\n" +
            "• Revisa el GameManager en el Inspector para ajustar prefabs o spawn points.",
            "OK");

        Debug.Log("[AltsasuSetup] ✅ Escena GTA de Altsasua montada correctamente.");
    }

    [MenuItem("Altsasu GTA/Solo GameManager", false, 20)]
    static void SoloGameManager()
    {
        var spawns = CrearSpawnPoints();
        CrearGameManager(spawns);
    }

    [MenuItem("Altsasu GTA/Solo HUD", false, 21)]
    static void SoloHUD() => CrearHUD();

    [MenuItem("Altsasu GTA/Solo Terreno", false, 22)]
    static void SoloTerreno() => ConfigurarTerreno();

    [MenuItem("Altsasu GTA/Solo Árboles", false, 23)]
    static void SoloArboles() => SembrarArboles();

    // =========================================================================
    //  PASO 1 — TERRENO CLOUD COMPARE
    // =========================================================================

    static void ConfigurarTerreno()
    {
        // Buscar el objeto del terreno en la escena (puede tener cualquier nombre)
        GameObject terreno = GameObject.Find("GroundPointsMesh");
        if (terreno == null)
        {
            // Intentar encontrar por mesh name
            var allRenderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            foreach (var r in allRenderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null &&
                    (mf.sharedMesh.name.Contains("Ground") || mf.sharedMesh.name.Contains("ground") ||
                     mf.sharedMesh.name.Contains("Mesh") || mf.sharedMesh.name.Contains("vertices")))
                {
                    terreno = r.gameObject;
                    break;
                }
            }
        }

        if (terreno == null)
        {
            // Intentar instanciar el OBJ directamente
            var meshPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PATH_TERRENO_OBJ);
            if (meshPrefab != null)
            {
                terreno = (GameObject)PrefabUtility.InstantiatePrefab(meshPrefab);
                terreno.name = "GroundPointsMesh";
                Undo.RegisterCreatedObjectUndo(terreno, "Crear Terreno");
                Debug.Log("[AltsasuSetup] Terreno CloudCompare instanciado desde OBJ.");
            }
            else
            {
                Debug.LogWarning("[AltsasuSetup] No se encontró el terreno en la escena ni el OBJ. Añádelo manualmente.");
                return;
            }
        }

        // Marcar como Static
        GameObjectUtility.SetStaticEditorFlags(terreno,
            StaticEditorFlags.ContributeGI |
            StaticEditorFlags.OccludeeStatic |
            StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.BatchingStatic);

        // Añadir OptimizadorTerreno si no lo tiene
        var opt = terreno.GetComponent<OptimizadorTerreno>();
        if (opt == null)
        {
            opt = Undo.AddComponent<OptimizadorTerreno>(terreno);
        }

        // Asignar textura de CloudCompare
        var textura = AssetDatabase.LoadAssetAtPath<Texture2D>(PATH_TEXTURA_CC);
        if (textura != null)
        {
            opt.texturaCCOlor = textura;
            Debug.Log("[AltsasuSetup] Textura Alsasua_Color.png asignada al terreno.");
        }
        else
        {
            Debug.LogWarning("[AltsasuSetup] No se encontró Alsasua_Color.png en EscenarioSuelo/");
        }

        // Buscar chunks hijos
        var chunkRenderers = terreno.GetComponentsInChildren<MeshRenderer>(true);
        if (chunkRenderers.Length > 1)
        {
            opt.chunksTerreno = chunkRenderers;
            opt.usarChunks = true;
        }

        EditorUtility.SetDirty(terreno);
        Debug.Log($"[AltsasuSetup] Terreno configurado: {terreno.name} ({chunkRenderers.Length} chunks)");
    }

    // =========================================================================
    //  PASO 2 — SPAWN POINTS
    // =========================================================================

    static SpawnData CrearSpawnPoints()
    {
        var data = new SpawnData();

        // Contenedor padre
        var contenedor = new GameObject("--- SpawnPoints ---");
        Undo.RegisterCreatedObjectUndo(contenedor, "Spawn Points");

        // Spawn jugador — frente al ayuntamiento (centro)
        data.spawnJugador = CrearSpawnPoint("Spawn_Jugador", CENTRO_ALSASUA + new Vector3(0, 0, 5), contenedor.transform, Color.green);

        // Spawn policía — 4 puntos en las entradas del pueblo
        data.spawnPolicia = new Transform[4];
        data.spawnPolicia[0] = CrearSpawnPoint("Spawn_Policia_N",  CENTRO_ALSASUA + new Vector3(0,   0, 60),  contenedor.transform, Color.blue);
        data.spawnPolicia[1] = CrearSpawnPoint("Spawn_Policia_S",  CENTRO_ALSASUA + new Vector3(0,   0, -60), contenedor.transform, Color.blue);
        data.spawnPolicia[2] = CrearSpawnPoint("Spawn_Policia_E",  CENTRO_ALSASUA + new Vector3(60,  0, 0),   contenedor.transform, Color.blue);
        data.spawnPolicia[3] = CrearSpawnPoint("Spawn_Policia_O",  CENTRO_ALSASUA + new Vector3(-60, 0, 0),   contenedor.transform, Color.blue);

        // Spawn enemigos — 8 puntos dispersos
        data.spawnEnemigos = new Transform[8];
        float[] angulos = { 0, 45, 90, 135, 180, 225, 270, 315 };
        for (int i = 0; i < 8; i++)
        {
            float rad = angulos[i] * Mathf.Deg2Rad;
            float r = 30f;
            Vector3 pos = CENTRO_ALSASUA + new Vector3(Mathf.Sin(rad) * r, 0, Mathf.Cos(rad) * r);
            data.spawnEnemigos[i] = CrearSpawnPoint($"Spawn_Enemigo_{i + 1}", pos, contenedor.transform, Color.red);
        }

        // Puntos para árboles — corona exterior
        data.puntosArboles = new Transform[16];
        for (int i = 0; i < 16; i++)
        {
            float rad = (i / 16f) * Mathf.PI * 2f;
            float r = Random.Range(50f, 120f);
            Vector3 pos = CENTRO_ALSASUA + new Vector3(Mathf.Sin(rad) * r, 0, Mathf.Cos(rad) * r);
            data.puntosArboles[i] = CrearSpawnPoint($"Arbol_{i + 1}", pos, contenedor.transform, Color.green * 0.6f);
        }

        return data;
    }

    static Transform CrearSpawnPoint(string nombre, Vector3 pos, Transform padre, Color color)
    {
        var go = new GameObject(nombre);
        go.transform.position = pos;
        go.transform.SetParent(padre);
        Undo.RegisterCreatedObjectUndo(go, nombre);
        return go.transform;
    }

    // =========================================================================
    //  PASO 3 — GAME MANAGER
    // =========================================================================

    static void CrearGameManager(SpawnData spawns)
    {
        // Destruir GameManager anterior si existe
        var anterior = GameObject.Find("GameManager");
        if (anterior != null && anterior.GetComponent<GameManagerAltsasua>() != null)
        {
            Undo.DestroyObjectImmediate(anterior);
        }

        var gm = new GameObject("GameManager");
        Undo.RegisterCreatedObjectUndo(gm, "GameManager");

        var mgr = gm.AddComponent<GameManagerAltsasua>();

        // Asignar spawn del jugador
        mgr.puntoSpawnJugador = spawns.spawnJugador;

        // Asignar prefabs de policía
        mgr.prefabCochePolicia = LoadPrefab(PATH_INTERCEPTOR, "Interceptor");
        mgr.prefabHelicoptero  = LoadPrefab(PATH_HELICOPTERO,  "Helicopter");
        mgr.prefabEnemigo      = LoadPrefab(PATH_ENEMIGO,      "Z Walker");

        // Asignar spawn points
        mgr.puntosSpawnPolicia  = spawns.spawnPolicia;
        mgr.puntosSpawnEnemigos = spawns.spawnEnemigos;
        mgr.puntosArboles       = spawns.puntosArboles;

        // Asignar árboles
        mgr.prefabsArboles = new GameObject[]
        {
            LoadPrefab(PATH_TREE_1, "Tree10_1"),
            LoadPrefab(PATH_TREE_2, "Tree10_2"),
            LoadPrefab(PATH_TREE_3, "Tree10_3"),
            LoadPrefab(PATH_TREE_4, "Tree10_4"),
            LoadPrefab(PATH_TREE_5, "Tree10_5"),
        };

        // Referencia al terreno
        var terreno = GameObject.Find("GroundPointsMesh");
        if (terreno != null) mgr.terrenoCloudCompare = terreno;

        EditorUtility.SetDirty(gm);
        Debug.Log("[AltsasuSetup] GameManager creado y configurado.");
    }

    // =========================================================================
    //  PASO 4 — HUD CANVAS
    // =========================================================================

    static void CrearHUD()
    {
        // Eliminar HUD anterior
        var anteriorHUD = GameObject.Find("HUD_Altsasu");
        if (anteriorHUD != null) Undo.DestroyObjectImmediate(anteriorHUD);

        // Canvas raíz
        var canvasGO = new GameObject("HUD_Altsasu");
        Undo.RegisterCreatedObjectUndo(canvasGO, "HUD Canvas");

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // ── Dinero (esquina superior izquierda) ───────────────────────────
        var textoDinero = CrearTextoHUD(canvasGO.transform, "Texto_Dinero", "$ 500",
            new Vector2(10, -10), new Vector2(200, 40),
            TextAnchor.UpperLeft, new Color(0.1f, 1f, 0.3f), 22);

        // ── Nivel de búsqueda / estrellas (esquina superior derecha) ──────
        var textoEstrellas = CrearTextoHUD(canvasGO.transform, "Texto_Busqueda", "☆☆☆☆☆",
            new Vector2(-210, -10), new Vector2(200, 40),
            TextAnchor.UpperRight, new Color(1f, 0.85f, 0f), 28);

        // ── Puntuación (debajo del dinero) ────────────────────────────────
        var textoPuntos = CrearTextoHUD(canvasGO.transform, "Texto_Puntuacion", "Score: 0",
            new Vector2(10, -55), new Vector2(200, 35),
            TextAnchor.UpperLeft, Color.white, 18);

        // ── Crosshair central ─────────────────────────────────────────────
        var crosshair = CrearTextoHUD(canvasGO.transform, "Crosshair", "+",
            new Vector2(-10, -20), new Vector2(20, 20),
            TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f), 24);
        // Centrar el crosshair en pantalla
        var crossRT = crosshair.GetComponent<RectTransform>();
        crossRT.anchorMin = new Vector2(0.5f, 0.5f);
        crossRT.anchorMax = new Vector2(0.5f, 0.5f);
        crossRT.anchoredPosition = Vector2.zero;
        crossRT.sizeDelta = new Vector2(20, 20);

        // ── Barra de vida (parte inferior) ───────────────────────────────
        var textoVida = CrearTextoHUD(canvasGO.transform, "Texto_Vida", "❤ 100",
            new Vector2(10, 15), new Vector2(150, 35),
            TextAnchor.LowerLeft, new Color(1f, 0.3f, 0.3f), 20);
        var rtVida = textoVida.GetComponent<RectTransform>();
        rtVida.anchorMin = new Vector2(0, 0);
        rtVida.anchorMax = new Vector2(0, 0);
        rtVida.anchoredPosition = new Vector2(10, 15);

        // ── Asignar referencias al GameManager ───────────────────────────
        var gmObj = GameObject.Find("GameManager");
        if (gmObj != null)
        {
            var mgr = gmObj.GetComponent<GameManagerAltsasua>();
            if (mgr != null)
            {
                mgr.textoDinero       = textoDinero.GetComponent<Text>();
                mgr.textoNivelBusqueda = textoEstrellas.GetComponent<Text>();
                mgr.textoPuntuacion   = textoPuntos.GetComponent<Text>();
                EditorUtility.SetDirty(gmObj);
                Debug.Log("[AltsasuSetup] Referencias HUD asignadas al GameManager.");
            }
        }

        EditorUtility.SetDirty(canvasGO);
        Debug.Log("[AltsasuSetup] HUD Canvas creado.");
    }

    static GameObject CrearTextoHUD(Transform padre, string nombre, string textoInicial,
        Vector2 posicion, Vector2 tamaño, TextAnchor alineacion, Color color, int fontSize)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = posicion;
        rt.sizeDelta = tamaño;

        var txt = go.AddComponent<Text>();
        txt.text = textoInicial;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = fontSize;
        txt.color = color;
        txt.alignment = alineacion;
        txt.fontStyle = FontStyle.Bold;

        // Sombra para legibilidad
        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.8f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);

        Undo.RegisterCreatedObjectUndo(go, nombre);
        return go;
    }

    // =========================================================================
    //  PASO 5 — ÁRBOLES
    // =========================================================================

    static void SembrarArboles()
    {
        // Buscar spawn points de árboles creados antes
        var contenedor = GameObject.Find("--- SpawnPoints ---");
        if (contenedor == null)
        {
            Debug.LogWarning("[AltsasuSetup] No hay spawn points. Ejecuta 'Montar Escena Completa' primero.");
            return;
        }

        GameObject[] prefabsArboles =
        {
            LoadPrefab(PATH_TREE_1, "Tree10_1"),
            LoadPrefab(PATH_TREE_2, "Tree10_2"),
            LoadPrefab(PATH_TREE_3, "Tree10_3"),
            LoadPrefab(PATH_TREE_4, "Tree10_4"),
            LoadPrefab(PATH_TREE_5, "Tree10_5"),
        };

        var vegContenedor = new GameObject("--- Vegetacion ---");
        Undo.RegisterCreatedObjectUndo(vegContenedor, "Vegetacion");

        int sembrados = 0;
        for (int i = 0; i < contenedor.transform.childCount; i++)
        {
            var hijo = contenedor.transform.GetChild(i);
            if (!hijo.name.StartsWith("Arbol_")) continue;

            var prefab = prefabsArboles[Random.Range(0, prefabsArboles.Length)];
            if (prefab == null) continue;

            var arbol = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            arbol.transform.position = hijo.position;
            arbol.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            arbol.transform.SetParent(vegContenedor.transform);
            GameObjectUtility.SetStaticEditorFlags(arbol,
                StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
            Undo.RegisterCreatedObjectUndo(arbol, "Arbol");
            sembrados++;
        }

        Debug.Log($"[AltsasuSetup] {sembrados} árboles sembrados.");
    }

    // =========================================================================
    //  UTILIDADES
    // =========================================================================

    static GameObject LoadPrefab(string path, string nombre)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            Debug.LogWarning($"[AltsasuSetup] Prefab no encontrado: {path} ({nombre})");
        return prefab;
    }

    class SpawnData
    {
        public Transform   spawnJugador;
        public Transform[] spawnPolicia;
        public Transform[] spawnEnemigos;
        public Transform[] puntosArboles;
    }
}
