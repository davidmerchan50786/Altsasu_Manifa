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
using UnityEngine.Rendering.HighDefinition;

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

        // 1. Terrain — delegado en ServicioTerreno (única fuente de verdad).
        //    El servicio valida terrenos existentes (rechaza demos de asset
        //    packs), genera el DEM en hilo de fondo y publica TerrenoListoEvent.
        //    Aquí solo se espera a que el suelo esté resuelto.
        yield return EsperarServicioTerreno();

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

    // La creación del terreno vive en ServicioTerreno (Systems/ServicioTerreno.cs):
    // cadena de proveedores [existente validado → DEM en hilo de fondo → plano
    // de emergencia], desactivación de terrenos ajenos y TerrenoListoEvent.
    // Aquí solo queda la espera con timeout explícito.
    IEnumerator EsperarServicioTerreno()
    {
        if (ServicioTerreno.Instance == null)
            new GameObject("ServicioTerreno").AddComponent<ServicioTerreno>();

        float t = 0f;
        while (ServicioTerreno.Instance != null &&
               !ServicioTerreno.Instance.EstaListo &&
               ServicioTerreno.Instance.Estado != EstadoTerreno.Fallido &&
               t < 30f)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        Debug.Log($"[Bootstrap] Terreno: estado={ServicioTerreno.Instance?.Estado} " +
                  $"fuente={ServicioTerreno.Instance?.Fuente} (espera {t:F1}s).");
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

        // ── Exposición AUTOMÁTICA con límites ──────────────────────────────────
        // FIX (jun 2026): EV3 fijo + sol de 80.000 lux = ~12 pasos sobreexpuesto
        // (pantalla blanca quemada). Con el sol HDRP en lux, un exterior de día
        // pide EV ~14-15. Automatic con límites 11-15 cubre día y atardecer sin
        // quemar ni oscurecer.
        var expo = perfil.Add<UnityEngine.Rendering.HighDefinition.Exposure>(true);
        expo.mode.overrideState     = true;
        expo.mode.value             = UnityEngine.Rendering.HighDefinition.ExposureMode.Automatic;
        expo.limitMin.overrideState = true; expo.limitMin.value = 11f;
        expo.limitMax.overrideState = true; expo.limitMax.value = 15f;

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
        luz.shadows   = LightShadows.Soft;
        go.transform.rotation = Quaternion.Euler(55f, -30f, 0f); // mediodía, ilumina bien el terreno
        // Iluminación ambiental directa — garantiza que el terreno recibe luz
        RenderSettings.ambientMode      = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor    = new Color(0.6f, 0.75f, 1.0f);   // cielo azul
        RenderSettings.ambientEquatorColor= new Color(0.55f, 0.65f, 0.45f); // horizonte verde
        RenderSettings.ambientGroundColor = new Color(0.25f, 0.32f, 0.18f); // suelo oscuro
        RenderSettings.ambientIntensity   = 1.5f;

        // HDRP requiere HDAdditionalLightData en luces direccionales
        var hdSol = go.GetComponent<HDAdditionalLightData>() ?? go.AddComponent<HDAdditionalLightData>();
        hdSol.SetIntensity(80000f, UnityEngine.Rendering.LightUnit.Lux);

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

        // Esperar a que el SUELO esté listo (mosaico V2 o terreno único). Sin
        // esto el jugador spawneaba con el terreno aún generándose, en el aire
        // (Y viejo 242) y caía atravesando hasta el vacío (Y≈-2144 en playtest).
        float tEsp = 0f;
        while (tEsp < 20f)
        {
            var svcT = ServiceLocator.Get<ITerrainService>();
            if (svcT != null && svcT.EstaListo) break;
            if (svcT == null && Terrain.activeTerrain != null) break; // escena legacy
            tEsp += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }
        // Margen para que los TerrainCollider del anillo 0 entren en física
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        // Altura del suelo TILE-AWARE: Terrain.activeTerrain.SampleHeight no vale
        // con 48 tiles (devuelve uno arbitrario). Fallback: cota real de la plaza
        // en el datum local (≈20.6), nunca el 240 del esquema viejo.
        float alturaTerreno = GeoDataAlsasua.COTA_PLAZA - GeoDataAlsasua.Z_MIN;
        float yMundo = TerrenoGlobal.AlturaMundo(centroX, centroZ);
        if (yMundo > -500f) alturaTerreno = yMundo;

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
        Debug.Log($"[Bootstrap] ✓ Jugador spawneado en {pos} (cota real {alturaTerreno + GeoDataAlsasua.Z_MIN:0}m snm).");
    }

    GameObject CrearJugadorMinimo(Vector3 pos)
    {
        // FIX (jun 2026): antes se creaba una cápsula azul con Rigidbody +
        // ControladorMovimientoGTA, así que NUNCA se usaban los assets reales.
        // Ahora se usa el controlador principal del proyecto:
        //   · ControladorJugador — cámara TP, armas, animaciones (RequireComponent
        //     añade su CharacterController, que ya hace de collider: sin Rigidbody
        //     ni CapsuleCollider duplicados que peleen con él).
        //   · ConfiguradorPersonajeAAA — asigna el modelo real (Resources/Prefabs/
        //     Personajes/PlayerArmature, rigged HDRP) y materiales PBR. Si no
        //     encuentra FBX, ControladorJugador cae al humanoide procedural.
        var root = new GameObject("Jugador");
        root.layer = 9;
        root.transform.position = pos;

        root.AddComponent<ControladorJugador>();
        root.AddComponent<ConfiguradorPersonajeAAA>();
        return root;
    }

    public static void AplicarCapaBaseTerreno(Terrain terreno) // usado por ServicioTerreno
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
            cam.farClipPlane  = 16000f; // anillo de montes de fondo a 5-9 km (SistemaMontesFondo)
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

            // antialiasing en modo TAA (sustituye al VolumeComponent TAA que
            // no existe en HDRP 17; mejor calidad que FXAA en mundo abierto)
            var aaMode = hdType.GetProperty("antialiasing");
            if (aaMode != null)
            {
                var enumType = aaMode.PropertyType;
                if (System.Enum.IsDefined(enumType, 2))   // TemporalAntialiasing=2
                    aaMode.SetValue(hdComp, System.Enum.ToObject(enumType, 2));
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

        // AudioManager — sin él no hay sonido (sintético + clips reales TMM).
        if (FindFirstObjectByType<AudioManager>() == null)
            gmGO.AddComponent<AudioManager>();

        // GestorStreamingTexturas — presupuesto de Mipmap Streaming (acota VRAM) +
        // anti-hitch al paneo rápido + caché CPU de SVT. Sin instanciar, el script
        // no corre: este es su punto de arranque (capa Systems → permitido).
        if (FindFirstObjectByType<GestorStreamingTexturas>() == null)
            gmGO.AddComponent<GestorStreamingTexturas>();

        // SistemaManifestacion — mecánica CENTRAL. JuegoManifestacion espera a su
        // Instance (timeout 10s) pero NO lo crea: debe existir antes que él.
        if (FindFirstObjectByType<SistemaManifestacion>() == null)
            gmGO.AddComponent<SistemaManifestacion>();
        // SistemaMisiones — misiones/objetivos del juego.
        if (FindFirstObjectByType<SistemaMisiones>() == null)
            gmGO.AddComponent<SistemaMisiones>();

        if (FindFirstObjectByType<JuegoManifestacion>() == null)
            gmGO.AddComponent<JuegoManifestacion>();
        if (FindFirstObjectByType<HUDManifestacion>() == null)
            gmGO.AddComponent<HUDManifestacion>();

        // ConductorMundo — resuelve duplicados (vegetación/río/mobiliario) de forma
        // segura (component.enabled=false, reversible). Debe correr en Play.
        if (FindFirstObjectByType<ConductorMundo>() == null)
            gmGO.AddComponent<ConductorMundo>();

        // ── Sistemas de generación del mundo ─────────────────────────────
        // Estos sistemas tienen DefaultExecutionOrder específico y DEBEN
        // existir como MonoBehaviours para que Unity respete su orden de ejecución.
        // Se crean en un GO dedicado para no contaminar GameManager.
        EnsureGeneradores();

        // ── Mundo vivo EXTRA (migrado): tren, túneles, charcos, humo, viento ─
        EnsureMundoVivo();

        // ── Sistemas de ASSETS + población del mundo (antes nunca se instanciaban) ─
        EnsureSistemasAssets();
    }

    // Sistemas que dan vida real al mundo usando los prefabs de Resources/:
    // civiles, tráfico, fauna, mobiliario urbano, animaciones de NPCs, rocas HD,
    // fuego real y el director de intensidad. NINGUNO estaba en escena en Play,
    // así que aunque los prefabs y scripts existían, no se ejecutaban. Aquí se
    // instancian en el orden correcto: primero los "targets" que SistemaAssets
    // auto-rellena en su Awake, luego SistemaAssets (carga Resources/ + auto-asigna),
    // y por último los consumidores que usan SistemaAssets.Instance en su Start.
    void EnsureSistemasAssets()
    {
        var go = GameObject.Find("SistemasAssets") ?? new GameObject("SistemasAssets");

        void Add<T>() where T : MonoBehaviour
        {
            if (FindFirstObjectByType<T>() == null)
            {
                go.AddComponent<T>();
                Debug.Log($"[Bootstrap] [+ assets] {typeof(T).Name}");
            }
        }

        // 1) Targets que SistemaAssets auto-rellena por reflexión en su Awake.
        //    Deben existir ANTES de añadir SistemaAssets (Awake corre al instante
        //    al hacer AddComponent en runtime). SistemaDestruccion y
        //    ConfiguradorAssetsAAA ya existen (creados arriba / en EnsureGeneradores).
        Add<SistemaArmasExtendido>();          // Molotov + lapas con fuego real

        // 2) Cargador central — carga Resources/ y auto-asigna prefabs a:
        //    SistemaDestruccion (fuego), ConfiguradorAssetsAAA (explosiones),
        //    SistemaArmasExtendido (molotov), PoliciaForalIA (modelos GC),
        //    AlsasuaTreeStreamer (árboles vascos), SistemaManifestacion (barricadas).
        Add<SistemaAssets>();

        // 3) Director de intensidad ("clima de seguridad") — alimenta la barra de
        //    tensión del HUD y a los consumidores de eventos.
        Add<DirectorMundo>();

        // 4) Consumidores — usan SistemaAssets.Instance en Start (orden por
        //    DefaultExecutionOrder, todos > 0, así que corren tras SistemaAssets).
        Add<SistemaAnimacionesRuntime>();      // 150 · saca a civiles/GC del T-pose
        Add<SistemaTrafico>();                 // 150 · coches reales (pool de prefabs)
        Add<SistemaSpawnCiviles>();            // 160 · civiles reales en las calles
        Add<SistemaFauna>();                   // 170 · perros/ciervos/lobo por bioma
        Add<SistemaReaccionNPCs>();            // 180 · reacción a disparos/redadas
        Add<MobiliarioUrbano>();               // street furniture + props de zona (sistema unificado)
        Add<SistemaRocasHD>();                 // 190 · rocas HD cerca del jugador
        Add<SistemaVidaNocturna>();            // farolas se encienden de noche

        // 5) Interiores caminables — entra a bar/comisaría/tienda. Usa los 27
        //    muebles reales de Resources/MueblesCiudad/ (Mesa, Silla, Sofá, Armario…).
        //    Se auto-detecta sobre "Edificios_AAA"; degrada limpio si no existe.
        Add<InterioresExplorables>();

        // 6) Montes de fondo — anillo de cumbres en el horizonte (sierras vascas),
        //    sube el far clip y relaja la niebla. Antes el horizonte quedaba vacío
        //    a 2 km. Decorativo, sin coste de gameplay.
        Add<SistemaMontesFondo>();

        // 7) Edificios fotogramétricos — receptor del pipeline de fotogrametría.
        //    Coloca mallas foto-reales de Resources/Fotogrametria/ en su GPS real.
        //    Inerte si la carpeta está vacía; es la vía a realismo Street View.
        Add<SistemaEdificiosFotogrametria>();

        // 8) Sistemas integrados en la auditoría 2026-06 (antes huérfanos: existían
        //    pero nada los instanciaba). Todos self-contained y defensivos.
        Add<SistemaDirectorConsumos>();        // puente DirectorMundo.OnEvento → sistemas
        Add<PropsDestruccionManifestacion>();  // escombros/barricadas/fuego según intensidad
        Add<SistemaRotulosZona>();             // nombre de barrio al entrar (estilo GTA)
        Add<SistemaShaderGlobals>();           // _GlobalWetness/_GlobalNightLevel → PBR mojado
        Add<TuningFisica>();                   // fricción Pacejka, anti-roll, suspensión
        Add<SistemaOcclusion>();               // GPU occlusion culling según quality tier
        Add<SistemaAguaRio>();                 // HDRP WaterSurface del Burunda (no-op sin ALSASUA_WATER)
        Add<SistemaNeblina>();                 // niebla volumétrica local del cauce del Arakil
        Add<SistemaClimaEfectos>();            // clima → humo fábricas + velocidad tren
        Add<SistemaAPVScenarios>();            // GI día/noche (APV) — se desactiva si no hay APV
        Add<AplicadorTexturasReales>();        // fachadas PBR desde fotos reales (no-op sin reporte)
        Add<HUDSistemas>();                    // F3: overlay de debug + toasts del director
        // FIX (jun 2026): SistemaChunks vive en Alsasua.Systems y AltsasuCore
        // (Runtime) no puede crearlo — solo lo creaba el menú de editor, así que
        // el check F1 "SistemaChunks" fallaba siempre en escenas auto-generadas.
        // Es defensivo: sin chunks configurados queda inerte.
        Add<SistemaChunks>();                  // streaming de secciones del mundo
    }

    // Sistemas de "mundo vivo" migrados desde la otra rama. Arrancan solos en
    // Play, en un GO dedicado. Son self-contained y defensivos (no rompen si
    // falta algo). Colocan su geometría según la altura del terreno, así que
    // lucen correctos con el terreno LIDAR local generado (no con Cesium activo).
    void EnsureMundoVivo()
    {
        var go = GameObject.Find("MundoVivoExtra") ?? new GameObject("MundoVivoExtra");

        void AddVivo<T>() where T : MonoBehaviour
        {
            if (FindFirstObjectByType<T>() == null)
            {
                go.AddComponent<T>();
                Debug.Log($"[Bootstrap] [+ mundo vivo] {typeof(T).Name}");
            }
        }

        AddVivo<SistemaVientoVegetacion>();   // viento → WindZone + vegetación
        AddVivo<SistemaCharcos>();            // charcos/suelo mojado con lluvia
        AddVivo<SistemaHumoFabricas>();       // humo en el Polígono Isasia
        AddVivo<SistemaTren>();               // tren llega/para/sale de la estación
        AddVivo<SistemaTuneles>();            // túneles de la autovía N-1
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
        Add<GeneradorTejadosAAA>();            // -61  kit modular de tejados (lo usa GeometriaPrecisa, ruta 2)
        Add<GeneradorGeometriaPrecisa>();      // -60  genera meshes OSM con LIDAR
        Add<AplicadorOrtofoto>();              // -55  textura aérea PNOA
        Add<PosicionadorPrecisionUrbana>();    // -54  árboles LIDAR
        // FIX (jun 2026): nadie instanciaba AlsasuaTreeStreamer — el check F1
        // "AlsasuaTreeStreamer" fallaba siempre. Es self-contained: espera al
        // Terrain (30s) y SistemaAssets le auto-asigna los prefabs de árbol.
        Add<AlsasuaTreeStreamer>();            //      streaming 20k árboles OSM/LIDAR
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

