#if UNITY_EDITOR
// Assets/Scripts/Editor/CrearJugadorHumano.cs
// ═══════════════════════════════════════════════════════════════════════════
//  CREAR JUGADOR HUMANO REAL — Lucia con animaciones AAA
//
//  Reemplaza la cápsula azul con un modelo humano real usando:
//   • Modelo: Lucia.FBX (o Civil_1 si Lucia falla)
//   • Rig: Humanoid (compatible con Mixamo + Locomotion Pack)
//   • Animator: Z Controller.controller (idle/walk/run/jump/crouch)
//   • Movimiento: ThirdPersonCharacter de Unity Standard Assets
//
//  MENÚ: Altsasu GTA → ★ Jugador Humano Realista (Lucia + animaciones)
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class CrearJugadorHumano
{
    static readonly string[] CANDIDATOS_MODELO = {
        "Assets/Models/Characters/Lucia/LuciaModel.FBX",
        "Assets/Models/Characters/Civiles/Civil_1/Meshy_AI_Casual_Confidence_0421161928_texture.fbx",
        "Assets/Models/Characters/Civiles/Civil_2/Meshy_AI_Casual_Summer_Street__0421162005_texture.fbx",
    };

    const string CONTROLLER_PATH = "Assets/#Xtra/Standard Assets/Characters/ThirdPersonCharacter/Animator/Z Controller.controller";
    const string CONTROLLER_FALLBACK = "Assets/#Xtra/Locomotion Setup/Locomotion/Locomotion.controller";

    // MENÚ-LEGACY: [MenuItem("Altsasu GTA/★ Jugador Humano Realista (Lucia + animaciones)", false, 10)]
    public static void CrearJugador()
    {
        // 1. Encontrar el terrain para sacar altura
        var terrain = Object.FindFirstObjectByType<Terrain>();
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("Sin terrain",
                "Crea primero el terrain (Altsasu GTA → Territorio Real → ★ Crear Terrain).", "OK");
            return;
        }

        // 2. Encontrar modelo humano
        GameObject modeloPrefab = null;
        string modeloPath = null;
        foreach (var p in CANDIDATOS_MODELO)
        {
            modeloPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (modeloPrefab != null) { modeloPath = p; break; }
        }
        if (modeloPrefab == null)
        {
            EditorUtility.DisplayDialog("Sin modelo",
                "No se encuentra ningún modelo humano en Assets/Models/Characters/.\n\n" +
                "Esperaba: LuciaModel.FBX o Civil_1/Civil_2.fbx", "OK");
            return;
        }

        // 3. Configurar como Humanoid si no lo está
        ConfigurarHumanoid(modeloPath);

        // 4. Borrar jugador antiguo (cápsula azul)
        var antiguos = GameObject.FindGameObjectsWithTag("Player");
        foreach (var a in antiguos) Undo.DestroyObjectImmediate(a);

        // 5. Instanciar el modelo
        var jugador = (GameObject)PrefabUtility.InstantiatePrefab(modeloPrefab);
        Undo.RegisterCreatedObjectUndo(jugador, "Crear Jugador Humano");
        jugador.name = "Jugador_" + System.IO.Path.GetFileNameWithoutExtension(modeloPath);
        jugador.tag  = "Player";

        // 6. Posicionar en Herriko Plaza
        float alturaSuelo = terrain.SampleHeight(new Vector3(1918f, 0, 8570f));
        jugador.transform.position = new Vector3(1918f, alturaSuelo + 0.1f, 8570f);
        jugador.transform.rotation = Quaternion.identity;

        // Auto-escalar si el modelo viene en escala incorrecta
        var bounds = CalcularBounds(jugador);
        if (bounds.size.y > 0.01f && bounds.size.y < 1.2f)
        {
            float factor = 1.75f / bounds.size.y;
            jugador.transform.localScale *= factor;
            Debug.Log($"[Jugador] Modelo reescalado x{factor:F2} para llegar a 1.75m de altura.");
        }
        else if (bounds.size.y > 3f)
        {
            float factor = 1.75f / bounds.size.y;
            jugador.transform.localScale *= factor;
            Debug.Log($"[Jugador] Modelo reescalado x{factor:F2} (era demasiado grande).");
        }

        // 7. Animator (usar GetOrAdd Unity-safe — `??` no detecta destroyed-but-not-null)
        var anim = GetOrAdd<Animator>(jugador);
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CONTROLLER_PATH)
                      ?? AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CONTROLLER_FALLBACK);
        if (controller != null) anim.runtimeAnimatorController = controller;
        anim.applyRootMotion = false;

        // 8. Rigidbody + CapsuleCollider
        var rb = GetOrAdd<Rigidbody>(jugador);
        if (rb == null)
        {
            Debug.LogError("[Jugador] No se pudo añadir Rigidbody al modelo. ¿Tiene componente conflictivo?");
            return;
        }
        rb.mass = 80f;
        rb.linearDamping = 0f;
        rb.angularDamping = 10f;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.isKinematic = false;

        var col = GetOrAdd<CapsuleCollider>(jugador);
        if (col != null)
        {
            col.height = 1.75f;
            col.radius = 0.32f;
            col.center = new Vector3(0, 0.875f, 0);
        }

        // 9. Eliminar controlador antiguo si existe
        var antigCtrl = jugador.GetComponent<ControladorMovimientoGTA>();
        if (antigCtrl != null) Object.DestroyImmediate(antigCtrl);

        // 10. Añadir ThirdPersonCharacter + ThirdPersonUserControl (Standard Assets)
        var tpc = AñadirSiExiste(jugador, "UnityStandardAssets.Characters.ThirdPerson.ThirdPersonCharacter");
        var tpu = AñadirSiExiste(jugador, "UnityStandardAssets.Characters.ThirdPerson.ThirdPersonUserControl");
        if (tpc == null || tpu == null)
        {
            // Fallback: usar nuestro propio controlador
            jugador.AddComponent<ControladorMovimientoGTA>();
            Debug.Log("[Jugador] ThirdPerson scripts no encontrados — usando ControladorMovimientoGTA.");
        }

        // 11. Health
        AñadirSiExiste(jugador, "Health");

        // 12. SpawnProtection
        if (jugador.GetComponent<SpawnProtection>() == null)
            jugador.AddComponent<SpawnProtection>();

        // 13. Configurar cámara — debe haber MainCamera con CameraFollowGTA
        var cam = Camera.main;
        if (cam != null)
        {
            var follow = cam.GetComponent<CameraFollowGTA>() ?? cam.gameObject.AddComponent<CameraFollowGTA>();
            follow.objetivo = jugador.transform;
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Selection.activeGameObject = jugador;
        EditorGUIUtility.PingObject(jugador);

        EditorUtility.DisplayDialog("✅ Jugador humano creado",
            $"Modelo: {System.IO.Path.GetFileName(modeloPath)}\n" +
            $"Posición: Herriko Plaza ({1918f:0}, {alturaSuelo + 0.1f:0.0}, {8570f:0})\n" +
            $"Animator: {(controller != null ? controller.name : "ninguno")}\n" +
            $"Movimiento: {(tpc != null ? "ThirdPersonCharacter (AAA)" : "ControladorMovimientoGTA")}\n\n" +
            "Pulsa ▶ Play.", "OK");
    }

    // =========================================================================
    //  CONFIGURAR HUMANOID RIG
    // =========================================================================

    static void ConfigurarHumanoid(string fbxPath)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null) return;

        bool cambiado = false;
        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            cambiado = true;
        }
        if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            cambiado = true;
        }
        if (cambiado)
        {
            importer.SaveAndReimport();
            Debug.Log($"[Jugador] {fbxPath} configurado como Humanoid.");
        }
    }

    // =========================================================================
    //  HELPERS
    // =========================================================================

    static Component AñadirSiExiste(GameObject go, string nombreClase)
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var t in asm.GetTypes())
                {
                    if (t.FullName == nombreClase || t.Name == nombreClase)
                    {
                        if (typeof(Component).IsAssignableFrom(t))
                        {
                            var existing = go.GetComponent(t);
                            if (existing != null) return existing;
                            return go.AddComponent(t);
                        }
                    }
                }
            }
            catch { }
        }
        return null;
    }

    // GetOrAdd Unity-safe: el operador `??` no detecta objetos Unity "destroyed-but-not-null".
    // Esta versión usa la comparación Unity (== null) que sí lo hace.
    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var existing = go.GetComponent<T>();
        if (existing != null) return existing;
        return go.AddComponent<T>();
    }

    static Bounds CalcularBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }
}
#endif
