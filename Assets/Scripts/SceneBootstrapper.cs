// Assets/Scripts/SceneBootstrapper.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SCENE BOOTSTRAPPER — Hace el juego jugable en Play aunque falten prefabs
//
//  Se ejecuta ANTES que cualquier otro script (DefaultExecutionOrder -200).
//  En Play, construye todo lo necesario si no está en escena:
//    - Terrain desde DEM raw (si no existe)
//    - Jugador capsule con controles GTA (si no hay prefab)
//    - Cámara tercera persona
//    - Luz solar
//    - GameManager conectado
//    - AltsasuCore
//
//  Spawn en Herriko Plaza — Unity (1918, y, 8570).
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.IO;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-200)]
public class SceneBootstrapper : MonoBehaviour
{
    // ── Parámetros públicos ────────────────────────────────────────────────
    [Header("Coordenadas reales de Alsasua")]
    public float centroX = GeoDataAlsasua.OX;
    public float centroZ = GeoDataAlsasua.OZ;

    [Header("Ajustes de terreno")]
    public bool generarTerrenoDesdeDEM = true;
    public bool usarPlanoCuadradoFallback = true; // si el DEM falla

    [Header("Jugador")]
    public GameObject prefabJugador; // asignar en Inspector si existe
    public bool crearJugadorSiNoHay = true;

    // ── Rutas ─────────────────────────────────────────────────────────────
    const string DEM_PATH      = "Assets/AlsasuaData/dem_unity_1025.raw";
    // Terreno cuadrado centrado en Herriko Plaza (1918, y, 8570).
    // 6 km × 6 km cubre todo el casco urbano y las montañas próximas
    // (Urbasa NW, Aralar SW, Aizkorri NE), con Alsasua en el centro exacto.
    const float  TER_W         = 6000f;
    const float  TER_L         = 6000f;
    const float  TER_H         = 900f;
    const int    DEM_RES       = 1025;
    // Posición origen del terreno para que (centroX, y, centroZ) quede en el centro
    const float  TER_OX        = GeoDataAlsasua.OX - TER_W * 0.5f;   // -1082
    const float  TER_OZ        = GeoDataAlsasua.OZ - TER_L * 0.5f;   //  5570

#pragma warning disable CS0414
    bool _listo;
#pragma warning restore CS0414

    // =========================================================================
    //  BOOTSTRAP
    // =========================================================================

    IEnumerator Start()
    {
        yield return null; // esperar un frame a que todo Awake termine

        Debug.Log("[Bootstrap] Comprobando escena…");

        // 1. Terrain — omitir si Cesium Georeference está activo (evita z-fighting)
        bool cesiumActivo = IsCesiumPresente();
        bool terrenoOK = false;
        if (!cesiumActivo)
        {
            terrenoOK = EnsureTerrain();
            if (terrenoOK) yield return null;
        }
        else
        {
            Debug.Log("[Bootstrap] Cesium detectado — terreno DEM omitido (Cesium provee el terreno).");
        }

        // 2. Sol
        EnsureSol();
        CrearVolumeMinimo();

        // 3. NavMesh — arrancar el horneado en paralelo mientras se carga el jugador.
        //    SistemaNavMesh.Start() ya espera al Terrain, así que solo necesitamos
        //    asegurarnos de que el componente existe en escena.
        EnsureNavMesh();

        // 4. Jugador
        yield return StartCoroutine(EnsureJugador());

        // 5. Cámara
        EnsureCamera();

        // 6. GameManager + Core
        EnsureGameManager();
        EnsureCore();

        // 7. Sistemas de gameplay
        EnsureSistemasBasicos();

        _listo = true;
        Debug.Log($"[Bootstrap] ✅ Escena lista. Jugador en ({centroX}, y, {centroZ}) = Herriko Plaza." +
                  $" NavMesh listo: {SistemaNavMesh.EstaListo}");
    }

    // =========================================================================
    //  TERRAIN
    // =========================================================================

    bool EnsureTerrain()
    {
        if (FindFirstObjectByType<Terrain>() != null)
        {
            Debug.Log("[Bootstrap] Terrain ya existe en escena.");
            return false;
        }

        if (generarTerrenoDesdeDEM)
        {
            string demAbs = Path.Combine(Application.dataPath.Replace("Assets", ""), DEM_PATH);
            if (File.Exists(demAbs))
            {
                if (CrearTerrenoDesdeDEM(demAbs)) return true;
            }
            else Debug.LogWarning($"[Bootstrap] DEM no encontrado: {DEM_PATH}");
        }

        if (usarPlanoCuadradoFallback) CrearPlanoFallback();
        return false;
    }

    bool CrearTerrenoDesdeDEM(string demPath)
    {
        try
        {
            var td = new TerrainData();
            td.heightmapResolution = DEM_RES;
            td.size = new Vector3(TER_W, TER_H, TER_L);

            byte[] raw = File.ReadAllBytes(demPath);
            var heights = new float[DEM_RES, DEM_RES];
            for (int r = 0; r < DEM_RES; r++)
                for (int c = 0; c < DEM_RES; c++)
                {
                    int idx = (r * DEM_RES + c) * 2;
                    heights[r, c] = BitConverter.ToUInt16(raw, idx) / 65535f;
                }
            td.SetHeights(0, 0, heights);

            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain_Alsasua_Runtime";
            // Centrar el terreno en Herriko Plaza: (centroX - TER_W/2, 0, centroZ - TER_L/2)
            go.transform.position = new Vector3(TER_OX, 0f, TER_OZ);
            go.layer = 8;

            // HDRP/TerrainLit requiere terrain layers — las creamos en runtime
            var terrainComp = go.GetComponent<Terrain>();
            AplicarCapaBaseTerreno(terrainComp);

            Debug.Log($"[Bootstrap] ✓ Terrain 6×6 km centrado en Herriko Plaza ({TER_OX:F0},{TER_OZ:F0})→({TER_OX+TER_W:F0},{TER_OZ+TER_L:F0})");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Bootstrap] Error cargando DEM: {e.Message}");
            return false;
        }
    }

    void CrearPlanoFallback()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
        go.name = "Terrain_Fallback";
        go.transform.position = new Vector3(centroX, 240f, centroZ);
        go.transform.localScale = new Vector3(500f, 1f, 500f);
        go.layer = 8;
        var mat = go.GetComponent<MeshRenderer>().material;
        mat.color = new Color(0.38f, 0.42f, 0.30f);
        Debug.Log("[Bootstrap] ⚠ Plano fallback creado. Ejecuta 'Territorio Real → GENERAR TODO' en Editor.");
    }

    // =========================================================================
    //  VOLUME HDRP MÍNIMO — exposición correcta, sin blur agresivo
    // =========================================================================

    void CrearVolumeMinimo()
    {
        // Destruir volumes existentes que puedan estar causando problemas
        foreach (var v in FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None))
            if (v.isGlobal) Destroy(v.gameObject);

        var go  = new GameObject("Volume_Bootstrap");
        var vol = go.AddComponent<UnityEngine.Rendering.Volume>();
        vol.isGlobal = true;
        vol.priority = 100f; // máxima prioridad — anula cualquier otro

        var perfil = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
        vol.profile = perfil;

        // ── Exposición FIJA (evita la oscuridad/sobreexposición de Automatic) ──
        var expo = perfil.Add<UnityEngine.Rendering.HighDefinition.Exposure>(true);
        expo.mode.overrideState         = true;
        expo.mode.value                 = UnityEngine.Rendering.HighDefinition.ExposureMode.Fixed;
        expo.fixedExposure.overrideState= true;
        expo.fixedExposure.value        = 3f; // EV100 bajo = escena muy brillante (exterior día)

        // ── Cielo degradado simple ──
        var ve = perfil.Add<UnityEngine.Rendering.HighDefinition.VisualEnvironment>(true);
        // skyType.value debe ser el ID registrado de GradientSky, no GetHashCode().
        // Obtenemos el ID desde el atributo SkyUniqueID que HDRP pone en la clase.
        ve.skyType.overrideState        = true;
        ve.skyType.value                = ObtenerSkyTypeId<UnityEngine.Rendering.HighDefinition.GradientSky>();
        ve.skyAmbientMode.overrideState = true;
        ve.skyAmbientMode.value         = UnityEngine.Rendering.HighDefinition.SkyAmbientMode.Dynamic;

        var sky = perfil.Add<UnityEngine.Rendering.HighDefinition.GradientSky>(true);
        sky.top.overrideState    = true; sky.top.value    = new Color(0.4f, 0.6f, 1.0f);   // azul cielo vasco
        sky.middle.overrideState = true; sky.middle.value = new Color(0.7f, 0.82f, 1.0f);  // horizonte claro
        sky.bottom.overrideState = true; sky.bottom.value = new Color(0.5f, 0.65f, 0.35f); // reflejo verde prado

        // ── Tonemapping neutro ──
        var tm = perfil.Add<UnityEngine.Rendering.HighDefinition.Tonemapping>(true);
        tm.mode.overrideState = true;
        tm.mode.value         = UnityEngine.Rendering.HighDefinition.TonemappingMode.Neutral;

        // ── Sin niebla densa ──
        var fog = perfil.Add<UnityEngine.Rendering.HighDefinition.Fog>(true);
        fog.enabled.overrideState      = true; fog.enabled.value      = true;
        fog.meanFreePath.overrideState = true; fog.meanFreePath.value = 2000f; // niebla muy suave
        fog.baseHeight.overrideState   = true; fog.baseHeight.value   = 0f;
        fog.maximumHeight.overrideState= true; fog.maximumHeight.value= 500f;

        Debug.Log("[Bootstrap] ✓ Volume HDRP mínimo creado — escena visible.");
    }

    // =========================================================================
    //  NAVMESH
    // =========================================================================

    void EnsureNavMesh()
    {
        if (FindFirstObjectByType<SistemaNavMesh>() != null) return;

        var go = new GameObject("NavMeshManager");
        var nm = go.AddComponent<SistemaNavMesh>();

        // Pasar las coordenadas del centro urbano al NavMesh
        // centroX/centroZ son las coordenadas de Herriko Plaza en el sistema de terreno
        // El campo centroInicial de SistemaNavMesh se fija con los valores por defecto (1918, 240, 8570)
        // Si quieres sobreescribirlos en runtime:
        // Usar reflexión para asignar el campo privado si es necesario,
        // o hacerlo público en SistemaNavMesh (actualmente SerializeField — accesible desde Inspector)

        Debug.Log("[Bootstrap] NavMeshManager creado — horneado iniciará cuando el Terrain esté listo.");
    }

    // =========================================================================
    //  SOL
    // =========================================================================

    void EnsureSol()
    {
        if (FindFirstObjectByType<Light>() != null) return;

        var go = new GameObject("Sun_Bootstrap");
        var luz = go.AddComponent<Light>();
        luz.type      = LightType.Directional;
        luz.color     = new Color(1f, 0.96f, 0.88f);
        luz.intensity = 5f;   // intensidad alta para iluminar bien en HDRP
        luz.shadows   = LightShadows.Soft;
        go.transform.rotation = Quaternion.Euler(55f, -30f, 0f); // mediodía, ilumina bien el terreno
        // Iluminación ambiental directa — garantiza que el terreno recibe luz
        RenderSettings.ambientMode      = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor    = new Color(0.6f, 0.75f, 1.0f);   // cielo azul
        RenderSettings.ambientEquatorColor= new Color(0.55f, 0.65f, 0.45f); // horizonte verde
        RenderSettings.ambientGroundColor = new Color(0.25f, 0.32f, 0.18f); // suelo oscuro
        RenderSettings.ambientIntensity   = 1.5f;

        // HDRP requiere HDAdditionalLightData en luces direccionales
        var hdLightType = System.Type.GetType(
            "UnityEngine.Rendering.HighDefinition.HDAdditionalLightData, " +
            "Unity.RenderPipelines.HighDefinition.Runtime");
        if (hdLightType != null && go.GetComponent(hdLightType) == null)
            go.AddComponent(hdLightType);

        Debug.Log("[Bootstrap] ✓ Sol creado (HDRP).");
    }

    // =========================================================================
    //  JUGADOR
    // =========================================================================

    IEnumerator EnsureJugador()
    {
        if (GameObject.FindGameObjectWithTag("Player") != null)
        {
            Debug.Log("[Bootstrap] Jugador ya existe en escena.");
            yield break;
        }

        if (!crearJugadorSiNoHay) yield break;

        // Esperar hasta que el TerrainCollider esté en física (máx 3s)
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        // Buscar la altura del TERRAIN exclusivamente (no edificios) usando SampleHeight.
        // Esto evita que el jugador spawnee sobre un tejado si hay un edificio en Herriko Plaza.
        float alturaTerreno = 240f;
        if (Terrain.activeTerrain != null)
            alturaTerreno = Terrain.activeTerrain.SampleHeight(new Vector3(centroX, 0, centroZ));

        // Verificar que el punto de spawn no esté dentro de un collider (ej: dentro de un edificio).
        // Si lo está, buscar la cota más alta sobre la columna (raycast desde 1500m hacia abajo
        // ignorando triggers) y spawnear ENCIMA del último collider.
        Vector3 puntoSpawn = new Vector3(centroX, alturaTerreno + 1f, centroZ);
        Collider[] enColision = Physics.OverlapSphere(puntoSpawn, 0.5f, ~0, QueryTriggerInteraction.Ignore);
        bool dentroDeAlgo = false;
        foreach (var c in enColision)
            if (c.GetComponent<Terrain>() == null) { dentroDeAlgo = true; break; }

        if (dentroDeAlgo)
        {
            // Spawnear encima del techo más alto en esa columna
            var rayDesdeArriba = new Ray(new Vector3(centroX, 1500f, centroZ), Vector3.down);
            if (Physics.Raycast(rayDesdeArriba, out RaycastHit techoHit, 2000f,
                                ~0, QueryTriggerInteraction.Ignore))
            {
                alturaTerreno = techoHit.point.y;
                Debug.Log($"[Bootstrap] ⚠ Spawn dentro de collider — recolocando sobre techo Y={alturaTerreno:F1}");
            }
        }

        Vector3 pos = new Vector3(centroX, alturaTerreno + 2f, centroZ);

        GameObject jugador = null;

        // Intentar usar prefab si existe
        if (prefabJugador != null)
        {
            jugador = Instantiate(prefabJugador, pos, Quaternion.identity);
        }
        else
        {
            // Crear jugador mínimo funcional con controles GTA
            jugador = CrearJugadorMinimo(pos);
        }

        jugador.name = "Jugador";
        jugador.tag  = "Player";
        Debug.Log($"[Bootstrap] ✓ Jugador spawneado en {pos} ({305 + alturaTerreno - 240:+0;-0}m snm approx).");
    }

    GameObject CrearJugadorMinimo(Vector3 pos)
    {
        var root = new GameObject("Jugador");
        root.layer = 9;

        // Forma visual: cápsula
        var viz = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        viz.name = "Visual";
        viz.transform.SetParent(root.transform);
        viz.transform.localPosition = new Vector3(0, 1f, 0);
        viz.transform.localScale    = new Vector3(0.5f, 0.9f, 0.5f);
        Destroy(viz.GetComponent<CapsuleCollider>()); // usar sólo el del root

        // Material jugador — color azul oscuro visible en HDRP
        var mr  = viz.GetComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        mat.SetColor("_BaseColor",  new Color(0.1f, 0.2f, 0.7f));
        mat.SetColor("_EmissiveColor", Color.black);
        if (mat.HasProperty("_Metallic"))    mat.SetFloat("_Metallic",    0f);
        if (mat.HasProperty("_Smoothness"))  mat.SetFloat("_Smoothness",  0.3f);
        mr.material = mat;

        // Física
        var col = root.AddComponent<CapsuleCollider>();
        col.height = 1.8f; col.radius = 0.3f; col.center = new Vector3(0, 0.9f, 0);

        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 80f; rb.linearDamping = 6f; rb.angularDamping = 10f;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.isKinematic = true; // kinematic hasta que el terrain tenga física

        // Controlador de movimiento GTA básico
        root.AddComponent<ControladorMovimientoGTA>();

        root.transform.position = pos;

        // Activar física tras 1 segundo (terrain collider ya estará listo)
        StartCoroutine(ActivarFisicaJugador(rb));
        return root;
    }

    static void AplicarCapaBaseTerreno(Terrain terreno)
    {
        // Unlit muestra la textura sin depender de iluminación
        var shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Texture") ?? Shader.Find("Standard");
        var mat    = new Material(shader);

        // Intentar cargar la ortofoto PNOA real
        Texture2D ortofoto = null;
        string[] rutas = {
            Path.Combine(Application.dataPath, "AlsasuaData/ortofoto_alsasua_REAL.png"),
            Path.Combine(Application.dataPath, "AlsasuaData/Textures/ortofoto_alsasua_REAL.png"),
        };
        foreach (var ruta in rutas)
        {
            if (File.Exists(ruta))
            {
                byte[] bytes = File.ReadAllBytes(ruta);
                ortofoto = new Texture2D(2, 2, TextureFormat.RGB24, false);
                ortofoto.LoadImage(bytes);
                ortofoto.Apply(false, true); // compress to VRAM
                Debug.Log($"[Bootstrap] ✓ Ortofoto PNOA cargada ({bytes.Length / 1024 / 1024}MB)");
                break;
            }
        }

        if (ortofoto != null)
        {
            mat.mainTexture      = ortofoto;
            mat.mainTextureScale = Vector2.one;
            mat.SetTexture("_UnlitColorMap", ortofoto);
            mat.SetTexture("_BaseColorMap",  ortofoto);
            mat.SetTexture("_MainTex",       ortofoto);
            mat.SetColor("_UnlitColor", Color.white);
            mat.SetColor("_BaseColor",  Color.white);
            mat.SetColor("_Color",      Color.white);
        }
        else
        {
            Color v = new Color(0.45f, 0.62f, 0.28f);
            mat.SetColor("_UnlitColor", v);
            mat.SetColor("_BaseColor",  v);
            mat.SetColor("_Color",      v);
            Debug.LogWarning("[Bootstrap] Ortofoto no encontrada — usando color verde base.");
        }

        terreno.materialTemplate = mat;
    }

    IEnumerator ActivarFisicaJugador(Rigidbody rb)
    {
        yield return new WaitForSeconds(1f);
        if (rb != null) rb.isKinematic = false;
    }

    // =========================================================================
    //  CÁMARA
    // =========================================================================

    void EnsureCamera()
    {
        Camera camExistente = Camera.main;

        if (camExistente == null)
        {
            var camGO = new GameObject("GTA_Camera");
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
            cam.fieldOfView   = 65f;
            cam.nearClipPlane = 0.15f;
            cam.farClipPlane  = 2000f;
            camExistente = cam;
        }

        // HDRP requiere HDAdditionalCameraData para renderizar
        AnadirHDRPCameraData(camExistente.gameObject);

        // CameraFollow — si no hay jugador aún, se conectará cuando llegue
        if (camExistente.GetComponent<CameraFollowGTA>() == null)
            camExistente.gameObject.AddComponent<CameraFollowGTA>();

        var follow = camExistente.GetComponent<CameraFollowGTA>();
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            follow.objetivo = player.transform;
        }
        else
        {
            // Esperar a que AltsasuCore emita el jugador
            AltsasuCore.OnJugadorSpawned += t =>
            {
                if (follow != null) follow.objetivo = t;
            };
        }

        Debug.Log("[Bootstrap] ✓ Cámara configurada (HDRP).");
    }

    // Obtiene el ID de tipo de cielo registrado por HDRP para cualquier SkySettings subclass.
    // HDRP usa un atributo [SkyUniqueID(n)] en cada clase de cielo; GetHashCode() es incorrecto.
    static int ObtenerSkyTypeId<T>() where T : UnityEngine.Rendering.HighDefinition.SkySettings
    {
        // Buscar el atributo SkyUniqueID en el tipo T
        var skyUIDType = System.Type.GetType(
            "UnityEngine.Rendering.HighDefinition.SkyUniqueID, " +
            "Unity.RenderPipelines.HighDefinition.Runtime");
        if (skyUIDType != null)
        {
            var attr = System.Attribute.GetCustomAttribute(typeof(T), skyUIDType);
            if (attr != null)
            {
                // El atributo tiene un campo 'uniqueID' o puede ser el primer constructor arg
                var field = skyUIDType.GetField("uniqueID")
                         ?? skyUIDType.GetField("m_UniqueID",
                                System.Reflection.BindingFlags.NonPublic |
                                System.Reflection.BindingFlags.Instance);
                if (field != null)
                    return (int)field.GetValue(attr);
            }
        }
        // Fallback: GradientSky HDRP 16/17 tiene el ID = 189733825 (constante pública)
        return 189733825;
    }

    static void AnadirHDRPCameraData(GameObject camGO)
    {
        // HDAdditionalCameraData es el componente que HDRP necesita para renderizar.
        // Lo añadimos via reflexión para no romper si HDRP no está instalado.
        var hdType = System.Type.GetType(
            "UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData, " +
            "Unity.RenderPipelines.HighDefinition.Runtime");
        if (hdType == null) return;

        var hdComp = camGO.GetComponent(hdType);
        if (hdComp == null)
            hdComp = camGO.AddComponent(hdType);

        // Forzar fondo = Sky (evita pantalla negra en HDRP cuando no hay skybox asignado)
        // clearColorMode: 0=None, 1=Color, 2=Sky (valor enum HDAdditionalCameraData.ClearColorMode)
        try
        {
            var clearMode = hdType.GetProperty("clearColorMode");
            if (clearMode != null)
            {
                // ClearColorMode.Sky = 2
                var enumType = clearMode.PropertyType;
                var skyVal   = System.Enum.ToObject(enumType, 2);
                clearMode.SetValue(hdComp, skyVal);
            }

            // volumeLayerMask: capas de volumes activos (incluir default=1)
            var volMask = hdType.GetProperty("volumeLayerMask");
            if (volMask != null)
                volMask.SetValue(hdComp, ~0);   // todas las capas

            // antialiasing en modo FXAA (sin artefactos, barato)
            var aaMode = hdType.GetProperty("antialiasing");
            if (aaMode != null)
            {
                var enumType = aaMode.PropertyType;
                if (System.Enum.IsDefined(enumType, 1))   // FXAA=1
                    aaMode.SetValue(hdComp, System.Enum.ToObject(enumType, 1));
            }
        }
        catch { /* reflexión sin garantías — silenciar */ }
    }

    // =========================================================================
    //  GAMEMANAGER + CORE
    // =========================================================================

    void EnsureGameManager()
    {
        if (GameManagerAltsasua.Instance != null) return;

        var go = new GameObject("GameManager");
        go.AddComponent<GameManagerAltsasua>();
        Debug.Log("[Bootstrap] ✓ GameManager creado en runtime.");
    }

    void EnsureCore()
    {
        if (AltsasuCore.I != null) return;
        var gmGO = GameManagerAltsasua.Instance?.gameObject ?? new GameObject("AltsasuCore");
        gmGO.AddComponent<AltsasuCore>();
    }

    void EnsureSistemasBasicos()
    {
        var gmGO = GameManagerAltsasua.Instance?.gameObject;
        if (gmGO == null) return;

        // ── Gameplay básico ───────────────────────────────────────────────
        if (FindFirstObjectByType<SistemaApoyoPopular>() == null)
            gmGO.AddComponent<SistemaApoyoPopular>();
        if (FindFirstObjectByType<SistemaDestruccion>() == null)
            gmGO.AddComponent<SistemaDestruccion>();
        if (FindFirstObjectByType<SistemaClima>() == null)
            gmGO.AddComponent<SistemaClima>();
        if (FindFirstObjectByType<HUDCanvas>() == null)
            gmGO.AddComponent<HUDCanvas>();
        if (FindFirstObjectByType<JuegoManifestacion>() == null)
            gmGO.AddComponent<JuegoManifestacion>();
        if (FindFirstObjectByType<HUDManifestacion>() == null)
            gmGO.AddComponent<HUDManifestacion>();

        // ── Sistemas de generación del mundo ─────────────────────────────
        // Estos sistemas tienen DefaultExecutionOrder específico y DEBEN
        // existir como MonoBehaviours para que Unity respete su orden de ejecución.
        // Se crean en un GO dedicado para no contaminar GameManager.
        EnsureGeneradores();
    }

    void EnsureGeneradores()
    {
        // Un solo GO "Generadores" agrupa todos los sistemas de construcción del mundo
        var genGO = GameObject.Find("Generadores");
        if (genGO == null) genGO = new GameObject("Generadores");

        void Add<T>() where T : MonoBehaviour
        {
            if (FindFirstObjectByType<T>() == null)
            {
                genGO.AddComponent<T>();
                Debug.Log($"[Bootstrap] [+] {typeof(T).Name}");
            }
        }

        // Orden de adición = orden de ejecución lógico (Unity respeta DefaultExecutionOrder)
        Add<ConfiguradorAssetsAAA>();          // -98  primero — provee prefabs a todo
        Add<GestorMaterialesAlsasua>();        // -85  materiales PBR
        Add<SistemaSueloAAA>();                // -65  terrain layers + calles
        Add<GeneradorTerrenoUltraPreciso>();   // -68  heightmap LIDAR
        Add<FusionadorEdificiosUltra>();       // -62  carga nube de puntos edificios
        Add<GeneradorGeometriaPrecisa>();      // -60  genera meshes OSM con LIDAR
        Add<AplicadorOrtofoto>();              // -55  textura aérea PNOA
        Add<PosicionadorPrecisionUrbana>();    // -54  árboles LIDAR
    }

    // =========================================================================
    //  UTILIDADES
    // =========================================================================

    static bool IsCesiumPresente()
    {
        // Comprobar si hay un CesiumGeoreference en escena (sin depender del assembly de Cesium)
        foreach (var go in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (go != null && go.GetType().FullName != null &&
                go.GetType().FullName.Contains("CesiumGeoreference"))
                return true;
        return false;
    }

    static float ObtenerAltura(float x, float z)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain != null) return terrain.SampleHeight(new Vector3(x, 0, z));

        // Raycast
        if (Physics.Raycast(new Vector3(x, 1000f, z), Vector3.down, out var hit, 2000f))
            return hit.point.y;

        return 240f; // altura media de Alsasua en Unity coords
    }

}

// ─────────────────────────────────────────────────────────────────────────────
//  CONTROLADOR DE MOVIMIENTO GTA BÁSICO
//  (para cuando no hay PlayerMotor/ThirdPersonCharacter importado aún)
// ─────────────────────────────────────────────────────────────────────────────

public class ControladorMovimientoGTA : MonoBehaviour
{
    [Header("Movimiento")]
    public float velocidad      = 5f;
    public float velocidadCorrer= 10f;
    public float fuerzaSalto    = 6f;
    public float sensibilidadRaton = 2f;

    Rigidbody _rb;
    bool      _enSuelo;
    float     _rotY;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        // Confirmar que no es kinematic al spawnear
        if (_rb != null) _rb.isKinematic = false;
    }

    void Update()
    {
        // Salto con SPACE — usar la API nueva si está disponible
        if (LeerKeyDown(KeyCode.Space) && _enSuelo)
            _rb?.AddForce(Vector3.up * fuerzaSalto, ForceMode.Impulse);

        // Pausa con Escape
        if (LeerKeyDown(KeyCode.Escape))
        {
            var gm = GameManagerAltsasua.Instance;
            if (gm != null) gm.TogglePausa();
        }
    }

    // ── Helpers compatibles con New Input System ──────────────────────────

    static bool LeerKeyDown(KeyCode k)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return false;
        return k switch {
            KeyCode.W      => kb.wKey.wasPressedThisFrame,
            KeyCode.A      => kb.aKey.wasPressedThisFrame,
            KeyCode.S      => kb.sKey.wasPressedThisFrame,
            KeyCode.D      => kb.dKey.wasPressedThisFrame,
            KeyCode.Space  => kb.spaceKey.wasPressedThisFrame,
            KeyCode.Escape => kb.escapeKey.wasPressedThisFrame,
            KeyCode.LeftShift => kb.leftShiftKey.wasPressedThisFrame,
            _ => false,
        };
#else
        return Input.GetKeyDown(k);
#endif
    }

    static bool LeerKey(KeyCode k)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return false;
        return k switch {
            KeyCode.W => kb.wKey.isPressed,
            KeyCode.A => kb.aKey.isPressed,
            KeyCode.S => kb.sKey.isPressed,
            KeyCode.D => kb.dKey.isPressed,
            KeyCode.LeftShift => kb.leftShiftKey.isPressed,
            _ => false,
        };
#else
        return Input.GetKey(k);
#endif
    }

    static float LeerEjeHorizontal()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return 0f;
        float v = 0f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  v -= 1f;
        return v;
#else
        return Input.GetAxis("Horizontal");
#endif
    }

    static float LeerEjeVertical()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return 0f;
        float v = 0f;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed)   v += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v -= 1f;
        return v;
#else
        return Input.GetAxis("Vertical");
#endif
    }

    void FixedUpdate()
    {
        if (_rb == null || _rb.isKinematic) return;

        // Detectar suelo
        _enSuelo = Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, 0.25f);

        // Movimiento WASD compatible con ambos input systems
        float h = LeerEjeHorizontal();
        float v = LeerEjeVertical();
        bool  corriendo = LeerKey(KeyCode.LeftShift);

        Vector3 dir = (transform.forward * v + transform.right * h);
        if (dir.sqrMagnitude > 1f) dir = dir.normalized;

        float spd = corriendo ? velocidadCorrer : velocidad;
        Vector3 vel = dir * spd;
        vel.y = _rb.linearVelocity.y;
        _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity, vel, Time.fixedDeltaTime * 10f);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  CÁMARA FOLLOW GTA BÁSICA
// ─────────────────────────────────────────────────────────────────────────────

public class CameraFollowGTA : MonoBehaviour
{
    public Transform objetivo;
    public float distancia  = 6f;
    public float altura     = 2.5f;
    public float suavidad   = 8f;
    public float sensibilidad = 3f;

    float _yaw, _pitch = 15f;
    bool  _inicializada;

    void Start()
    {
        if (objetivo == null) objetivo = GameObject.FindGameObjectWithTag("Player")?.transform;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
        SnapACamaraDetrasDelJugador();
    }

    // Posiciona la cámara INSTANTÁNEAMENTE detrás del jugador.
    // Sin esto, la cámara inicial en (0,1,-10) viaja en Lerp hasta (1918, ~244, 8570)
    // durante ~5 segundos, cruzando todo lo que haya entre medias — paredes, árboles, etc.
    void SnapACamaraDetrasDelJugador()
    {
        if (objetivo == null) return;
        _yaw = objetivo.eulerAngles.y;
        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0);
        Vector3 pos = objetivo.position + rot * new Vector3(0, 0, -distancia) + Vector3.up * altura;
        transform.position = pos;
        transform.LookAt(objetivo.position + Vector3.up * 1.2f);
        _inicializada = true;
    }

    void LateUpdate()
    {
        if (objetivo == null)
        {
            objetivo = GameObject.FindGameObjectWithTag("Player")?.transform;
            if (objetivo == null) return;
            SnapACamaraDetrasDelJugador();
        }

        // Si todavía no estamos inicializados (objetivo apareció después), snap ahora
        if (!_inicializada) SnapACamaraDetrasDelJugador();

        // Rotar cámara con ratón — compatible con ambos input systems
        Vector2 mouseDelta = LeerMouseDelta();
        _yaw   += mouseDelta.x * sensibilidad * 0.1f;
        _pitch -= mouseDelta.y * sensibilidad * 0.1f;
        _pitch  = Mathf.Clamp(_pitch, -10f, 60f);

        // Calcular posición deseada
        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0);
        Vector3 posDeseada = objetivo.position + rot * new Vector3(0, 0, -distancia) + Vector3.up * altura;

        // Spring arm: si hay pared entre jugador y posición deseada, acercar cámara
        // SphereCast con radio 0.3 evita que la cámara se "pegue" al ladrillo
        Vector3 desde = objetivo.position + Vector3.up * 1.2f;
        Vector3 hacia = posDeseada - desde;
        float dist = hacia.magnitude;
        if (dist > 0.01f && Physics.SphereCast(desde, 0.3f, hacia.normalized, out var hit,
            dist, ~(1 << 9), QueryTriggerInteraction.Ignore))
        {
            posDeseada = desde + hacia.normalized * Mathf.Max(hit.distance - 0.1f, 0.8f);
        }

        // Distancia entre actual y deseada — si > 50m, snap (estamos lejos, no interpolar)
        if (Vector3.Distance(transform.position, posDeseada) > 50f)
            transform.position = posDeseada;
        else
            transform.position = Vector3.Lerp(transform.position, posDeseada, Time.deltaTime * suavidad);

        transform.LookAt(objetivo.position + Vector3.up * 1.2f);

        // Rotar jugador con la cámara (en el plano horizontal)
        if (objetivo.TryGetComponent<ControladorMovimientoGTA>(out var ctrl))
            objetivo.rotation = Quaternion.Euler(0, _yaw, 0);
    }

    static Vector2 LeerMouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        var m = UnityEngine.InputSystem.Mouse.current;
        if (m == null) return Vector2.zero;
        return m.delta.ReadValue();
#else
        return new Vector2(Input.GetAxis("Mouse X") * 10f, Input.GetAxis("Mouse Y") * 10f);
#endif
    }
}

