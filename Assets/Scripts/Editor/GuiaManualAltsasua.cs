// Assets/Scripts/Editor/GuiaManualAltsasua.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GUÍA DE SETUP MANUAL — Panel lateral con instrucciones paso a paso
//  Tools → Alsasua → 📋 Guía Manual Paso a Paso
//
//  Panel anclable que muestra cada paso manual con instrucciones exactas,
//  verifica automáticamente los que puede y marca los completados.
//
//  PASOS:
//    0  Importar texturas AAA y HDRIs
//    1  Ejecutar SETUP MAESTRO
//    2  Convertir materiales → HDRP
//    3  Limpiar Missing Scripts
//    4  Instalar TextMeshPro
//    5  Hornear Occlusion Culling
//    6  Verificar prefab del coche
//    7  Ajustar WheelColliders del Interceptor
//    8  Configurar Humanoid Avatar (Guardia Civil)
//    9  Asignar Guardia Civil como prefab enemigo
//   10  Configurar Animator del jugador
//   11  Verificar NavMesh y terreno
//   12  Verificar edificios y calles en escena
//   13  Probar el bucle jugable completo
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Linq;

public class GuiaManualAltsasua : EditorWindow
{
    // ── Persistencia de checkboxes ─────────────────────────────────────────
    private static bool[] _hecho = new bool[14];

    private Vector2 _scroll;
    private int     _pasoExpandido = 0;
    private static GUIStyle _estiloTitulo, _estiloPaso, _estiloCmd,
                             _estiloOK, _estiloWarn, _estiloBody,
                             _estiloPasoHeader;

    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/📋 Guia Manual", priority = 50)]
    public static void Abrir()
    {
        var w = GetWindow<GuiaManualAltsasua>("📋 Guía Manual");
        w.minSize = new Vector2(380, 600);
        w.VerificarAutomatico();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  VERIFICACIÓN AUTOMÁTICA
    // ─────────────────────────────────────────────────────────────────────────

    void VerificarAutomatico()
    {
        // 0: Textures_AAA importadas
        _hecho[0] = AssetDatabase.FindAssets("t:Texture2D", new[]{ "Assets/Textures_AAA" }).Length > 100;

        // 1: Setup Maestro ejecutado (escena principal existe)
        _hecho[1] = File.Exists(Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "Assets/#Scenes/Alsasua_Main.unity")));

        // 2: Materiales HDRP (ningún shader Standard en carpetas principales)
        _hecho[2] = VerificarMaterialesHDRP();

        // 3: Missing Scripts limpiados (no se puede verificar automáticamente)
        // _hecho[3] queda como marcado manualmente

        // 4: TextMeshPro instalado
        _hecho[4] = System.Type.GetType("TMPro.TextMeshPro, Unity.TextMeshPro") != null ||
                    AssetDatabase.IsValidFolder("Assets/TextMesh Pro");

        // 5: Occlusion culling bakeado
        _hecho[5] = AssetDatabase.FindAssets("OcclusionCullingData").Length > 0;

        // 6: Interceptor prefab existe
        _hecho[6] = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Coches/Interceptor_Jugador.prefab") != null;

        // 7: WheelColliders (verificar que no están en (0,0,0))
        _hecho[7] = VerificarWheelColliders();

        // 8: Humanoid Avatar en Guardia Civil
        _hecho[8] = VerificarHumanoidGC();

        // 9: Guardia Civil asignado como prefabEnemigo
        _hecho[9] = VerificarPrefabEnemigo();

        // 10: Animator del jugador configurado
        _hecho[10] = VerificarAnimatorJugador();

        // 11: NavMesh horneado (solo verificable en PlayMode)
        _hecho[11] = UnityEngine.AI.NavMesh.CalculateTriangulation().vertices.Length > 0;

        // 12: Edificios en Zone_01
        _hecho[12] = VerificarEdificiosZona();

        // 13: Bucle jugable — solo marcado manualmente por el usuario
    }

    bool VerificarMaterialesHDRP()
    {
        var mats = AssetDatabase.FindAssets("t:Material", new[]{
            "Assets/Police Car & Helicopter", "Assets/Hot Rod", "Assets/BarrierPack" });
        foreach (var g in mats.Take(5))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
            if (m?.shader?.name?.Contains("Standard") == true) return false;
        }
        return true;
    }

    bool VerificarWheelColliders()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Coches/Interceptor_Jugador.prefab");
        if (prefab == null) return false;
        var wcs = prefab.GetComponentsInChildren<UnityEngine.WheelCollider>();
        if (wcs.Length < 4) return false;
        // Verificar que no todos están en (0,0,0)
        return wcs.Any(wc => wc.transform.localPosition != Vector3.zero);
    }

    bool VerificarHumanoidGC()
    {
        var guids = AssetDatabase.FindAssets("GuardiaCivil_Officer", new[]{
            "Assets/_ExtractedAssets/Personajes" });
        if (guids.Length == 0) return false;
        var path  = AssetDatabase.GUIDToAssetPath(guids[0]);
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        return importer?.animationType == ModelImporterAnimationType.Human;
    }

    bool VerificarPrefabEnemigo()
    {
        var gm = Object.FindFirstObjectByType<GameManagerAltsasua>();
        if (gm == null) return false;
        var f = typeof(GameManagerAltsasua).GetField("prefabEnemigo",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        return f?.GetValue(gm) != null;
    }

    bool VerificarEdificiosZona()
    {
        var zona = GameObject.Find("Zone_01_HerrikoPlaza_Centro");
        if (zona == null) return false;
        bool activa = zona.activeSelf; zona.SetActive(true);
        int n = zona.transform.childCount;
        zona.SetActive(activa);
        return n > 20;
    }

    bool VerificarAnimatorJugador()
    {
        // Buscar el animator controller del jugador
        var guids = AssetDatabase.FindAssets("t:AnimatorController JugadorAnimator",
            new[]{ "Assets" });
        if (guids.Length > 0) return true;
        // Alternativa: buscar cualquier AnimatorController que mencione jugador/player
        guids = AssetDatabase.FindAssets("t:AnimatorController Jugador");
        return guids.Length > 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GUI
    // ─────────────────────────────────────────────────────────────────────────

    void OnGUI()
    {
        InicializarEstilos();

        // Cabecera
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("📋  GUÍA DE SETUP MANUAL", _estiloTitulo);
        EditorGUILayout.LabelField("Sigue cada paso en orden. Los ✅ se verifican automáticamente.",
            EditorStyles.miniLabel);

        // Botón refrescar verificaciones
        if (GUILayout.Button("🔄 Verificar estado actual", GUILayout.Height(24)))
        {
            VerificarAutomatico();
            Repaint();
        }

        // Progreso
        int completados = _hecho.Count(h => h);
        EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(GUILayout.Height(16)),
            (float)completados / _hecho.Length,
            $"{completados}/{_hecho.Length} pasos completados");

        EditorGUILayout.Space(6);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        Paso(0,  "Importar texturas AAA y HDRIs",              DibujarPaso_0);
        Paso(1,  "Ejecutar ★ SETUP MAESTRO COMPLETO ★",       DibujarPaso_1);
        Paso(2,  "Convertir materiales → HDRP",               DibujarPaso_2);
        Paso(3,  "Limpiar Missing Scripts",                    DibujarPaso_3);
        Paso(4,  "Instalar TextMeshPro Essentials",            DibujarPaso_4);
        Paso(5,  "Hornear Occlusion Culling",                  DibujarPaso_5);
        Paso(6,  "Verificar prefab del coche",                 DibujarPaso_6);
        Paso(7,  "Ajustar WheelColliders del Interceptor",     DibujarPaso_7);
        Paso(8,  "Configurar Humanoid Avatar (Guardia Civil)", DibujarPaso_8);
        Paso(9,  "Asignar Guardia Civil como prefab enemigo",  DibujarPaso_9);
        Paso(10, "Configurar Animator del jugador",            DibujarPaso_10);
        Paso(11, "Verificar NavMesh y terreno",                DibujarPaso_11);
        Paso(12, "Verificar edificios y calles en escena",     DibujarPaso_12);
        Paso(13, "Probar el bucle jugable completo",           DibujarPaso_13);

        EditorGUILayout.EndScrollView();
    }

    void Paso(int idx, string titulo, System.Action contenido)
    {
        bool ok = idx < _hecho.Length && _hecho[idx];
        string icono = ok ? "✅" : (idx == _pasoExpandido ? "▶" : "○");
        Color colorFondo = ok ? new Color(0.1f,0.4f,0.1f,0.3f)
                              : idx == _pasoExpandido ? new Color(0.2f,0.4f,0.8f,0.2f)
                              : new Color(0.2f,0.2f,0.2f,0.1f);

        var rect = EditorGUILayout.BeginVertical();
        EditorGUI.DrawRect(rect, colorFondo);

        EditorGUILayout.BeginHorizontal();
        bool expandir = GUILayout.Button($"{icono}  Paso {idx+1}: {titulo}",
            _estiloPasoHeader, GUILayout.ExpandWidth(true));
        if (!ok)
        {
            if (GUILayout.Button("✓ Marcar hecho", GUILayout.Width(110), GUILayout.Height(20)))
            {
                _hecho[idx] = true;
                if (idx + 1 < _hecho.Length) _pasoExpandido = idx + 1;
            }
        }
        EditorGUILayout.EndHorizontal();

        if (expandir)
            _pasoExpandido = (_pasoExpandido == idx) ? -1 : idx;

        if (_pasoExpandido == idx && !ok)
        {
            EditorGUILayout.Space(4);
            contenido();
            EditorGUILayout.Space(6);
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CONTENIDO DE CADA PASO
    // ─────────────────────────────────────────────────────────────────────────

    void DibujarPaso_0()
    {
        Titulo("¿Por qué?");
        Texto("Unity no reconoce las 542 texturas PNG y 11 HDRIs descargados hasta que las importa. Sin importar, los edificios seguirán grises aunque los archivos estén en disco.");

        Titulo("Cómo hacerlo:");
        Cmd("1. En Unity, mira la barra inferior derecha");
        Cmd("2. Si hay una rueda girando → Unity ya está importando (espera)");
        Cmd("3. Si no gira → pulsa Ctrl+R o ve a Assets → Refresh");

        Titulo("Verificación automática:");
        var texCount = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Textures_AAA" }).Length;
        Texto($"Texturas importadas en Textures_AAA: {texCount}");
        Texto("✅ Se activa automáticamente cuando hay más de 100 texturas importadas.");

        if (GUILayout.Button("Abrir carpeta Textures_AAA en Project", GUILayout.Height(28)))
        {
            var obj = AssetDatabase.LoadAssetAtPath<Object>("Assets/Textures_AAA");
            if (obj != null) { Selection.activeObject = obj; EditorGUIUtility.PingObject(obj); }
            else EditorUtility.RevealInFinder(
                Path.GetFullPath(Path.Combine(Application.dataPath, "Textures_AAA")));
        }
    }

    void DibujarPaso_1()
    {
        Titulo("¿Qué hace el Setup Maestro?");
        Texto("Ejecuta 35 pasos automáticos: crea la escena, genera edificios, calles, prefabs de coche, animator, tráfico, civiles, waypoints de policía, configura audio/physics/HDRP y aplica texturas PBR.");

        Titulo("Cómo ejecutarlo:");
        Cmd("Tools  →  Alsasua  →  ★ SETUP MAESTRO COMPLETO ★");
        Texto("Pulsa 'Ejecutar todo' en el diálogo de confirmación.");

        Advertencia("Unity puede parecer congelado durante 5-15 minutos.\nNo lo cierres. La barra de progreso avanzará.");

        Titulo("Verificación:");
        Texto("En la Consola verás: '★ SETUP MAESTRO COMPLETADO ★'");
        Texto("La escena Assets/#Scenes/Alsasua_Main.unity debe existir.");

        bool existe = File.Exists(Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "Assets/#Scenes/Alsasua_Main.unity")));
        EditorGUILayout.LabelField(existe ? "✅ Escena detectada" : "❌ Escena no existe aún",
            existe ? _estiloOK : _estiloWarn);

        if (GUILayout.Button("Ejecutar Setup Maestro ahora", GUILayout.Height(32)))
            SETUP_MAESTRO.EjecutarTodo();
    }

    void DibujarPaso_2()
    {
        Titulo("¿Por qué?");
        Texto("Los paquetes importados (Police Car, Hot Rod, BarrierPack, etc.) usan el shader Standard de Unity. En HDRP se ven de color magenta/rosa hasta convertirlos.");

        Titulo("Pasos exactos:");
        Cmd("1. Menú superior:  Edit");
        Cmd("2. Submenú:  Rendering");
        Cmd("3. Submenú:  Materials");
        Cmd("4. Click en:  'Convert All Built-in Materials to HDRP'");
        Cmd("5. En el diálogo → pulsa 'Proceed'");
        Cmd("6. Espera la barra de progreso (1-3 min)");

        Advertencia("Si no ves ese submenú, instala el paquete HDRP primero:\nWindow → Package Manager → busca 'High Definition RP' → Install");

        Titulo("Verificación:");
        Texto("Los coches (Police Car, Hot Rod) deberían verse con sus colores reales, no magenta.");

        if (GUILayout.Button("Abrir menú Edit", GUILayout.Height(28)))
            EditorApplication.ExecuteMenuItem("Edit/Rendering/Materials/Convert All Built-in Materials to HDRP");
    }

    void DibujarPaso_3()
    {
        Titulo("¿Por qué?");
        Texto("Al importar muchos paquetes de terceros, algunos GameObjects quedan con referencias a scripts que ya no existen ('Missing Script'). Esto genera errores en consola y puede impedir que el Setup Maestro termine.");

        Titulo("Pasos automáticos:");
        if (GUILayout.Button("🧹 Limpiar Missing Scripts ahora", GUILayout.Height(32)))
            EditorApplication.ExecuteMenuItem("Tools/Alsasua/Debug/🧹 Limpiar Missing Scripts");

        Titulo("O manualmente:");
        Cmd("Tools → Alsasua → 🧹 Limpiar Missing Scripts");
        Texto("El script recorre TODOS los GameObjects de la escena activa y elimina las referencias nulas. Verás en Consola: '[LimpiarMissing] Eliminados X missing scripts'.");

        Advertencia("Hazlo ANTES de darle Play la primera vez. Los missing scripts dan\n" +
                    "NullReferenceException que ocultan los errores reales.");
    }

    void DibujarPaso_4()
    {
        Titulo("¿Por qué?");
        Texto("El HUD del juego (estrellas de wanted, velocímetro, texto de misiones) usa TextMeshPro. Sin instalarlo algunos elementos de UI pueden dar errores.");

        Titulo("Pasos exactos:");
        Cmd("1. Menú:  Window");
        Cmd("2. Submenú:  Package Manager");
        Cmd("3. En la lista izquierda → 'Unity Registry'");
        Cmd("4. Busca: 'TextMeshPro'");
        Cmd("5. Selecciónalo → pulsa 'Install'");
        Cmd("6. Cuando termine → pulsa 'Import TMP Essentials'");
        Cmd("   (sale una ventana automáticamente después de instalar)");

        Titulo("Verificación automática:");
        bool instalado = System.Type.GetType("TMPro.TextMeshPro, Unity.TextMeshPro") != null
                      || AssetDatabase.IsValidFolder("Assets/TextMesh Pro");
        EditorGUILayout.LabelField(
            instalado ? "✅ TextMeshPro detectado" : "❌ TextMeshPro no encontrado",
            instalado ? _estiloOK : _estiloWarn);

        if (GUILayout.Button("Abrir Package Manager", GUILayout.Height(28)))
            EditorApplication.ExecuteMenuItem("Window/Package Manager");
    }

    void DibujarPaso_5()
    {
        Titulo("¿Por qué?");
        Texto("Con 548 edificios en pantalla, Unity renderiza TODOS siempre, incluso los que están detrás de otros edificios. Sin Occlusion Culling la zona de Herriko Plaza puede ir a 10-15 FPS.");

        Titulo("Pasos exactos:");
        Cmd("1. Menú:  Window");
        Cmd("2. Submenú:  Rendering");
        Cmd("3. Click en:  'Occlusion Culling'");
        Cmd("4. En el panel que aparece → pestaña 'Bake'");
        Cmd("5. Pulsa el botón azul 'Bake'");
        Cmd("6. Espera 2-5 minutos");

        Advertencia("La escena Alsasua_Main.unity debe estar abierta.\nLos edificios deben estar marcados como 'Static' (el Setup Maestro lo hace).");

        Titulo("Verificación:");
        Texto("Cuando termina aparece: 'Bake completed' en la barra de estado.");
        Texto("En Scene View activa 'Overdraw' para ver qué se cullea.");

        bool horneado = AssetDatabase.FindAssets("OcclusionCullingData").Length > 0;
        EditorGUILayout.LabelField(
            horneado ? "✅ OcclusionCullingData detectado" : "❌ No horneado aún",
            horneado ? _estiloOK : _estiloWarn);

        if (GUILayout.Button("Abrir Occlusion Culling", GUILayout.Height(28)))
            EditorApplication.ExecuteMenuItem("Window/Rendering/Occlusion Culling");
    }

    void DibujarPaso_6()
    {
        Titulo("Verificar que el prefab se creó:");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Coches/Interceptor_Jugador.prefab");

        if (prefab != null)
        {
            EditorGUILayout.LabelField("✅ Interceptor_Jugador.prefab existe", _estiloOK);
            Texto("Puedes continuar al paso 7 (ajustar WheelColliders).");
            if (GUILayout.Button("Seleccionar prefab en Project", GUILayout.Height(28)))
            { Selection.activeObject = prefab; EditorGUIUtility.PingObject(prefab); }
        }
        else
        {
            EditorGUILayout.LabelField("❌ Prefab no encontrado", _estiloWarn);
            Texto("El Setup Maestro debe haberlo creado. Si no existe:");
            Cmd("Tools → Alsasua → 🚔 Crear Prefabs de Coche");
            if (GUILayout.Button("Crear prefabs de coche ahora", GUILayout.Height(28)))
                WizardCochePrefab.CrearPrefabs();
        }
    }

    void DibujarPaso_8()
    {
        Titulo("¿Por qué?");
        Texto("Los modelos Meshy AI del Guardia Civil tienen rig 'biped' pero Unity necesita que lo configures como 'Humanoid' para poder usar animaciones. Sin esto el personaje es una estatua en T-pose.");

        Titulo("Pasos exactos:");
        Cmd("1. Project → navega hasta:");
        Cmd("   Assets/_ExtractedAssets/Personajes/MeshyAI/GuardiaCivil_Officer_01/");
        Cmd("2. Selecciona el archivo .fbx (el modelo)");
        Cmd("3. En el Inspector → pestaña 'Rig'");
        Cmd("4. Animation Type → cambia a 'Humanoid'");
        Cmd("5. Avatar Definition → 'Create From This Model'");
        Cmd("6. Pulsa 'Apply'");
        Cmd("7. Pulsa 'Configure...' (botón que aparece tras Apply)");
        Cmd("8. Verifica que los huesos principales estén asignados:");
        Cmd("   - Hips, Spine, Chest, Head, UpperArm, Forearm, Hand");
        Cmd("   - UpperLeg, LowerLeg, Foot");
        Cmd("   Los círculos verdes = asignado, rojo = falta");
        Cmd("9. Pulsa 'Done'");

        Advertencia("Si hay círculos rojos en huesos críticos (Hips, Spine, Head),\n" +
                    "el rig no funcionará. En ese caso usa los civiles procedurales\n" +
                    "que el sistema ya genera automáticamente.");

        Titulo("Verificación:");
        bool ok = VerificarHumanoidGC();
        EditorGUILayout.LabelField(
            ok ? "✅ Avatar Humanoid detectado en GC" : "❌ Sin Humanoid configurado aún",
            ok ? _estiloOK : _estiloWarn);

        if (GUILayout.Button("Ir a carpeta Guardia Civil en Project", GUILayout.Height(28)))
        {
            var guids = AssetDatabase.FindAssets("GuardiaCivil_Officer",
                new[]{ "Assets/_ExtractedAssets/Personajes" });
            if (guids.Length > 0)
            {
                var obj = AssetDatabase.LoadAssetAtPath<Object>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }
        }
    }

    void DibujarPaso_9()
    {
        Titulo("¿Qué hace esto?");
        Texto("El GameManager spawnea policías cuando el nivel wanted sube. Necesita saber qué prefab usar. Sin esto, no aparecen policías en persecución.");

        Titulo("Pasos exactos:");
        Cmd("1. En la Hierarchy → busca 'GameManager'");
        Cmd("2. Selecciónalo → Inspector");
        Cmd("3. Busca el campo 'Prefab Enemigo'");
        Cmd("4. Arrastra desde Project hasta ese campo:");
        Cmd("   Assets/_ExtractedAssets/Personajes/MeshyAI/GuardiaCivil_Officer_01/[el FBX]");
        Cmd("   O bien: Assets/LowPolySoldiers_demo/models/Soldier_demo.FBX");
        Cmd("   (si el GC no tiene Humanoid aún usa el soldado low poly)");
        Cmd("5. También asigna 'Prefab Coche Policia':");
        Cmd("   Assets/Prefabs/Coches/Interceptor_Jugador.prefab");

        Titulo("Verificación:");
        bool ok = VerificarPrefabEnemigo();
        EditorGUILayout.LabelField(
            ok ? "✅ prefabEnemigo asignado" : "❌ prefabEnemigo = null",
            ok ? _estiloOK : _estiloWarn);

        if (GUILayout.Button("Seleccionar GameManager en Hierarchy", GUILayout.Height(28)))
        {
            var gm = Object.FindFirstObjectByType<GameManagerAltsasua>();
            if (gm != null) { Selection.activeObject = gm; }
            else EditorUtility.DisplayDialog("No encontrado",
                "GameManager no está en la escena activa.\nEjecuta primero el Setup Maestro.", "OK");
        }
    }

    void DibujarPaso_10()
    {
        Titulo("¿Por qué?");
        Texto("El jugador necesita un Animator Controller con los estados: Idle, Walk, Run, Jump, Drive. Sin él el personaje se mueve pero no tiene animaciones.");

        Titulo("El Setup Maestro lo crea automáticamente:");
        bool okAnim = VerificarAnimatorJugador();
        EditorGUILayout.LabelField(
            okAnim ? "✅ AnimatorController 'JugadorAnimator' detectado"
                   : "❌ AnimatorController no encontrado",
            okAnim ? _estiloOK : _estiloWarn);

        if (!okAnim)
        {
            Titulo("Crea el Animator Controller manualmente:");
            Cmd("1. En Project → click derecho en Assets/Animators");
            Cmd("2. Create → Animator Controller → nombrar 'JugadorAnimator'");
            Cmd("3. Doble click para abrir el Animator");
            Cmd("4. Crear estados: Idle, Walk, Run, Jump, Drive");
            Cmd("5. En la Hierarchy selecciona el jugador");
            Cmd("6. Inspector → Animator → Controller → arrastra JugadorAnimator");
            if (GUILayout.Button("Configurar Animator automáticamente", GUILayout.Height(32)))
                EditorApplication.ExecuteMenuItem("Tools/Alsasua/Assets/🎭 Animator Jugador");
        }
        else
        {
            Titulo("Verificar estados del Animator:");
            Cmd("1. Selecciona el prefab del jugador en Project");
            Cmd("2. Inspector → Animator → doble click en el Controller");
            Cmd("3. Verifica que hay estados: Idle, Walk, Run");
            Cmd("4. Las transiciones deben usar los parámetros: Speed, IsJumping, InVehicle");
        }

        Titulo("Parámetros requeridos:");
        Cmd("  float  Speed      — velocidad de movimiento (0-10)");
        Cmd("  bool   IsJumping  — true mientras salta");
        Cmd("  bool   InVehicle  — true cuando conduce");
    }

    void DibujarPaso_11()
    {
        Titulo("El NavMesh se hornea en runtime:");
        Texto("A diferencia del Occlusion Culling, el NavMesh de Alsasua se hornea automáticamente al darle Play (SistemaNavMesh.cs lo hace en ~1-2 segundos).");

        Titulo("Qué verificar:");
        Cmd("1. Dale Play");
        Cmd("2. Espera 2-3 segundos");
        Cmd("3. En la Consola debería aparecer:");
        Cmd("   '[NavMesh] ✅ NavMesh listo en X.XXs. Zona: 350m...'");
        Cmd("4. Si los policías empiezan a patrullar → NavMesh OK");

        Titulo("Si el NavMesh falla:");
        Cmd("• Verificar que el terreno DEM existe en:");
        Cmd("  Assets/AlsasuaData/dem_unity_1025.raw  (debe ser ~2MB)");
        Cmd("• Si no existe → el SceneBootstrapper crea un plano fallback");
        Cmd("  Los policías patrullarán en ese plano (cota Y=0)");

        bool ok = _hecho[11];
        EditorGUILayout.LabelField(
            ok ? "✅ NavMesh activo (verificado en Play)" : "⏳ Verificar en Play Mode",
            ok ? _estiloOK : EditorStyles.boldLabel);

        if (GUILayout.Button("Verificar DEM existe", GUILayout.Height(28)))
        {
            string dem = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "Assets/AlsasuaData/dem_unity_1025.raw"));
            bool existe = File.Exists(dem);
            EditorUtility.DisplayDialog("DEM",
                existe ? $"✅ DEM encontrado\n{new FileInfo(dem).Length/1024/1024} MB"
                       : "❌ DEM no encontrado\nEl terreno usará plano fallback",
                "OK");
        }
    }

    void DibujarPaso_12()
    {
        Titulo("Verificar contenido de Zone_01:");
        bool ok = VerificarEdificiosZona();
        var zona = GameObject.Find("Zone_01_HerrikoPlaza_Centro");

        if (zona != null)
        {
            bool activa = zona.activeSelf; zona.SetActive(true);
            int n = zona.transform.childCount;
            zona.SetActive(activa);
            EditorGUILayout.LabelField(
                ok ? $"✅ Zone_01 tiene {n} hijos (edificios, calles, civiles)"
                   : $"⚠ Zone_01 tiene solo {n} hijos — puede faltar contenido",
                ok ? _estiloOK : _estiloWarn);
        }
        else EditorGUILayout.LabelField("❌ Zone_01 no encontrada en escena", _estiloWarn);

        Cmd("Para ver la zona: Tools → Alsasua → 👁 Zonas → 01 · Herriko Plaza");
        Cmd("Debería activarse la zona y la cámara del Scene View moverse a Herriko Plaza");

        Titulo("Si la zona está vacía:");
        Cmd("Tools → Alsasua → 🏠 Generar Edificios Zone_01 (AAA)");
        Cmd("Tools → Alsasua → 🛣 Generar Carreteras Zone_01");
        Cmd("Tools → Alsasua → 🎨 Aplicar Texturas AAA");

        if (GUILayout.Button("Ir a Zone_01 en Scene View", GUILayout.Height(28)))
            EditorApplication.ExecuteMenuItem("Tools/Alsasua/👁 Zonas/01 · Herriko Plaza");
    }

    void DibujarPaso_13()
    {
        Titulo("El bucle jugable mínimo:");
        Texto("Esto no se puede verificar automáticamente — tienes que probarlo tú.");

        Titulo("Checklist en Play Mode:");
        Cmd("☐  1. Pulsa Play");
        Cmd("☐  2. El jugador aparece en Herriko Plaza (edificios alrededor)");
        Cmd("☐  3. WASD mueve al jugador con animación");
        Cmd("☐  4. El ratón mueve la cámara");
        Cmd("☐  5. Aparece el HUD (vida, dinero, estrellas)");
        Cmd("☐  6. En la Consola: '[NavMesh] ✅ NavMesh listo'");
        Cmd("☐  7. Aparece el texto: 'Roba el coche de la Policía Foral'");
        Cmd("☐  8. Acércate al Interceptor azul (a 20m al este de la plaza)");
        Cmd("☐  9. Aparece el indicador 'E para entrar'");
        Cmd("☐ 10. Pulsas E → entras en el coche");
        Cmd("☐ 11. WASD conduce el coche");
        Cmd("☐ 12. Aparece 1 estrella de wanted");
        Cmd("☐ 13. Un policía empieza a patrullar hacia ti");
        Cmd("☐ 14. Ctrl+W abre el panel de clima");
        Cmd("☐ 15. G activa modo pintar graffiti / click para pintar en una pared");

        Advertencia("Si algo falla en este checklist:\n" +
                    "Busca el error en la Consola (ventana roja) y dímelo.\n" +
                    "Cada error tiene nombre de script y número de línea.");

        if (GUILayout.Button("Iniciar Play Mode", GUILayout.Height(36)))
            EditorApplication.isPlaying = true;
    }

    void DibujarPaso_7()
    {
        Titulo("¿Qué son los WheelColliders?");
        Texto("Son los 4 puntos de físicas de las ruedas. El wizard los colocó en posiciones calculadas automáticamente, pero pueden no coincidir exactamente con el modelo 3D del Interceptor. Si están mal, el coche flota, se hunde en el suelo o no conduce.");

        Titulo("Pasos exactos:");
        Cmd("1. En el panel Project → busca: Assets/Prefabs/Coches/Interceptor_Jugador");
        Cmd("2. Doble click en el prefab → se abre el Prefab Editor");
        Cmd("3. En la Hierarchy del prefab verás 4 hijos: WC_FL, WC_FR, WC_RL, WC_RR");
        Cmd("   FL = Front Left, FR = Front Right, RL = Rear Left, RR = Rear Right");
        Cmd("4. Selecciona WC_FL");
        Cmd("5. En el Inspector → Transform → Position");
        Cmd("   Ajusta Y para que la esfera verde coincida con el centro de la rueda");
        Cmd("   Ajusta X/Z para que esté centrado en la rueda");
        Cmd("6. Repite para WC_FR, WC_RL, WC_RR");

        Titulo("Valores típicos para un sedan (ajusta según el modelo):");
        Cmd("  WC_FL: X = -0.77,  Y = 0.33,  Z =  1.45");
        Cmd("  WC_FR: X =  0.77,  Y = 0.33,  Z =  1.45");
        Cmd("  WC_RL: X = -0.77,  Y = 0.33,  Z = -1.45");
        Cmd("  WC_RR: X =  0.77,  Y = 0.33,  Z = -1.45");

        Advertencia("Para verificar: dale Play con el coche en escena.\n" +
                    "Si el coche flota → baja el Y de los WheelColliders.\n" +
                    "Si se hunde → sube el Y.\n" +
                    "Si vira solo → ajusta X.");

        Titulo("Verificación:");
        bool ok = VerificarWheelColliders();
        EditorGUILayout.LabelField(
            ok ? "✅ WheelColliders no están en (0,0,0)" : "⚠ WheelColliders pueden necesitar ajuste",
            ok ? _estiloOK : _estiloWarn);

        if (GUILayout.Button("Abrir prefab Interceptor_Jugador", GUILayout.Height(28)))
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Coches/Interceptor_Jugador.prefab");
            if (p != null) AssetDatabase.OpenAsset(p);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HELPERS DE UI
    // ─────────────────────────────────────────────────────────────────────────

    void Titulo(string t)
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(t, _estiloPaso);
    }

    void Texto(string t)
    {
        EditorGUILayout.LabelField(t, _estiloBody);
    }

    void Cmd(string t)
    {
        EditorGUILayout.LabelField(t, _estiloCmd);
    }

    void Advertencia(string t)
    {
        EditorGUILayout.Space(2);
        var rect = EditorGUILayout.GetControlRect(GUILayout.Height(
            EditorStyles.helpBox.CalcHeight(new GUIContent(t), position.width - 20)));
        EditorGUI.DrawRect(rect, new Color(0.8f,0.6f,0f,0.15f));
        EditorGUI.LabelField(rect, "⚠  " + t, EditorStyles.helpBox);
    }

    void InicializarEstilos()
    {
        if (_estiloTitulo != null) return;

        _estiloTitulo = new GUIStyle(EditorStyles.boldLabel)
            { fontSize = 14, alignment = TextAnchor.MiddleCenter };

        _estiloPasoHeader = new GUIStyle(EditorStyles.boldLabel)
            { fontSize = 12, alignment = TextAnchor.MiddleLeft,
              padding = new RectOffset(6, 0, 4, 4) };

        _estiloPaso = new GUIStyle(EditorStyles.boldLabel)
            { fontSize = 11, normal = { textColor = new Color(0.9f,0.85f,0.5f) } };

        _estiloCmd = new GUIStyle(EditorStyles.label)
            { fontSize = 11,
              normal   = { textColor = new Color(0.7f, 0.95f, 0.7f) },
              padding  = new RectOffset(16, 4, 1, 1),
              fontStyle = FontStyle.Normal };

        _estiloBody = new GUIStyle(EditorStyles.wordWrappedLabel)
            { fontSize = 11, padding = new RectOffset(4, 4, 2, 2) };

        _estiloOK   = new GUIStyle(EditorStyles.label)
            { normal = { textColor = new Color(0.3f, 1f, 0.3f) }, fontStyle = FontStyle.Bold };

        _estiloWarn = new GUIStyle(EditorStyles.label)
            { normal = { textColor = new Color(1f, 0.7f, 0.2f) }, fontStyle = FontStyle.Bold };
    }

    void OnInspectorUpdate() => Repaint();
}

// Helper local — usa el MenuItem registrado directamente (sin reflexión frágil)
internal static class ZonaHelper
{
    public static void MostrarZona01()
        => EditorApplication.ExecuteMenuItem("Tools/Alsasua/👁 Zonas/01 · Herriko Plaza");
}
