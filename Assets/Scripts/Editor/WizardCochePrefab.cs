// Assets/Scripts/Editor/WizardCochePrefab.cs
// ═══════════════════════════════════════════════════════════════════════════
//  WIZARD DE CONFIGURACIÓN DE COCHE
//  Menú: Tools → Alsasua → 🚔 Crear Prefabs de Coche
//
//  Crea dos prefabs listos para jugar:
//    1. Interceptor_Jugador.prefab  — coche policial conducible (Gorka Pillar 1+2)
//    2. Trabant_Civil.prefab        — coche civil conducible
//
//  Qué hace:
//    · Añade Rigidbody con masa y centro de masa correctos
//    · Crea 4 GOs de WheelCollider posicionados automáticamente (bounding box)
//    · Añade ControladorVehiculoJugador con valores preconfigurados
//    · Crea Transform AsientoConductor
//    · Añade IndicadorEntradaVehiculo ("Pulsa E para entrar")
//    · Añade MeshCollider al cuerpo del coche
//    · Guarda el prefab en Assets/Prefabs/Coches/
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Reflection;

public static class WizardCochePrefab
{
    private const string DIR_PREFABS = "Assets/Prefabs/Coches";

    // ── Rutas de los modelos fuente ───────────────────────────────────────
    private const string RUTA_INTERCEPTOR =
        "Assets/Police Car & Helicopter/Prefabs/Interceptor.prefab";
    private const string RUTA_TRABANT =
        "Assets/_ExtractedAssets/Vehicles/Trabant_601/Trabant_601.fbx";

    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/🚔 Crear Prefabs de Coche", priority = 40)]
    public static void CrearPrefabs()
    {
        // Crear carpeta destino
        if (!AssetDatabase.IsValidFolder(DIR_PREFABS))
        {
            AssetDatabase.CreateFolder("Assets/Prefabs", "Coches");
        }

        int creados = 0;

        // ── 1. Interceptor (coche policial) ────────────────────────────────
        var interceptorSrc = AssetDatabase.LoadAssetAtPath<GameObject>(RUTA_INTERCEPTOR);
        if (interceptorSrc != null)
        {
            ConfigurarYGuardar(
                interceptorSrc,
                $"{DIR_PREFABS}/Interceptor_Jugador.prefab",
                new ConfigCoche
                {
                    masa              = 1400f,
                    parMaximo         = 320f,
                    parFreno          = 900f,
                    parFrenoMano      = 1600f,
                    velocidadMax      = 30f,   // ~108 km/h
                    cuatroRuedas      = false,
                    anguloMaxDir      = 32f,
                    radioRueda        = 0.33f,
                    anchoRueda        = 0.22f,
                    alturaCentroMasa  = -0.35f,
                    rigidezMuelle     = 40000f,
                    amortiguacion     = 5000f,
                    antiRoll          = 4000f,
                    // Dimensiones típicas Crown Victoria / sedan policial
                    semilargo         = 2.50f, // semi-longitud = distancia centro a eje (m)
                    semiAncho         = 0.80f, // semi-ancho = distancia centro a rueda (m)
                    alturaRueda       = 0.33f  // altura del centro de rueda sobre la base
                });
            creados++;
            Debug.Log("[WizardCoche] ✅ Interceptor_Jugador.prefab creado.");
        }
        else
        {
            Debug.LogWarning("[WizardCoche] Interceptor.prefab no encontrado. " +
                             "Importa primero el paquete 'Police Car & Helicopter'.");
        }

        // ── 2. Trabant (coche civil) ───────────────────────────────────────
        var trabantSrc = AssetDatabase.LoadAssetAtPath<GameObject>(RUTA_TRABANT);
        if (trabantSrc != null)
        {
            ConfigurarYGuardar(
                trabantSrc,
                $"{DIR_PREFABS}/Trabant_Civil.prefab",
                new ConfigCoche
                {
                    masa              = 620f,   // Trabant real: 620 kg
                    parMaximo         = 95f,    // Trabant 601: motor 2T, 26 CV
                    parFreno          = 400f,
                    parFrenoMano      = 800f,
                    velocidadMax      = 22f,    // ~80 km/h
                    cuatroRuedas      = false,
                    anguloMaxDir      = 38f,
                    radioRueda        = 0.28f,
                    anchoRueda        = 0.18f,
                    alturaCentroMasa  = -0.25f,
                    rigidezMuelle     = 22000f,
                    amortiguacion     = 3000f,
                    antiRoll          = 2000f,
                    semilargo         = 1.85f,
                    semiAncho         = 0.65f,
                    alturaRueda       = 0.28f
                });
            creados++;
            Debug.Log("[WizardCoche] ✅ Trabant_Civil.prefab creado.");
        }
        else
        {
            // Crear coche primitivo funcional si no hay modelo
            CrearCochePrimitivo($"{DIR_PREFABS}/CochePrimitivo.prefab");
            creados++;
            Debug.Log("[WizardCoche] ⚠ Trabant no encontrado — creado CochePrimitivo.prefab (caja azul).");
        }

        AssetDatabase.Refresh();

        if (creados > 0)
        {
            EditorUtility.DisplayDialog("Prefabs creados",
                $"✅ {creados} prefabs de coche en {DIR_PREFABS}/\n\n" +
                "SIGUIENTE PASO:\n" +
                "1. Arrastra Interceptor_Jugador.prefab a Zone_01_HerrikoPlaza\n" +
                "2. Posiciónalo en una calle de Herriko Plaza (~1918, y, 8570)\n" +
                "3. Dale Play → acércate al coche → pulsa E\n\n" +
                "Ajusta los WheelColliders en el Inspector si la suspensión\n" +
                "no coincide exactamente con el modelo visual.", "OK");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CONFIGURACIÓN PRINCIPAL
    // ─────────────────────────────────────────────────────────────────────────

    private struct ConfigCoche
    {
        public float masa, parMaximo, parFreno, parFrenoMano, velocidadMax;
        public bool  cuatroRuedas;
        public float anguloMaxDir, radioRueda, anchoRueda;
        public float alturaCentroMasa, rigidezMuelle, amortiguacion, antiRoll;
        public float semilargo, semiAncho, alturaRueda;
    }

    private static void ConfigurarYGuardar(GameObject src, string rutaPrefab, ConfigCoche cfg)
    {
        // Instanciar temporalmente en la escena
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        go.name = Path.GetFileNameWithoutExtension(rutaPrefab);

        // ── Calcular bounding box del modelo visual ───────────────────────
        // Usamos los valores de cfg.semilargo/semiAncho como referencia;
        // si el modelo es mucho mayor/menor, Unity lo verá en escena y se puede ajustar.
        Bounds bounds = CalcularBounds(go);
        // Escalar valores de rueda si el modelo es muy diferente del típico
        if (bounds.size.magnitude > 1f)
        {
            float escalaL = bounds.size.z > 0.5f ? bounds.size.z / 5f : 1f;
            float escalaA = bounds.size.x > 0.5f ? bounds.size.x / 2f : 1f;
            cfg.semilargo   = Mathf.Clamp(bounds.size.z * 0.48f, 1.5f, 3.5f);
            cfg.semiAncho   = Mathf.Clamp(bounds.size.x * 0.44f, 0.5f, 1.2f);
            cfg.alturaRueda = Mathf.Clamp(cfg.radioRueda, 0.22f, 0.50f);
        }

        // ── Rigidbody ─────────────────────────────────────────────────────
        var rb = go.GetComponent<Rigidbody>() ?? go.AddComponent<Rigidbody>();
        rb.mass             = cfg.masa;
        rb.linearDamping    = 0.05f;
        rb.angularDamping   = 0.08f;
        rb.interpolation    = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // ── Centro de masa bajo (estabilidad) ─────────────────────────────
        // Se configura en ControladorVehiculoJugador.Awake via campo alturaCentroMasa

        // ── 4 WheelColliders ──────────────────────────────────────────────
        float yRueda  = bounds.min.y + cfg.alturaRueda; // altura del eje en local space
        float zFront  =  cfg.semilargo;
        float zRear   = -cfg.semilargo;
        float xLeft   = -cfg.semiAncho;
        float xRight  =  cfg.semiAncho;

        var rDI = CrearWheelCollider(go, "WC_FL", new Vector3(xLeft,  yRueda, zFront), cfg);
        var rDD = CrearWheelCollider(go, "WC_FR", new Vector3(xRight, yRueda, zFront), cfg);
        var rTI = CrearWheelCollider(go, "WC_RL", new Vector3(xLeft,  yRueda, zRear),  cfg);
        var rTD = CrearWheelCollider(go, "WC_RR", new Vector3(xRight, yRueda, zRear),  cfg);

        // ── AsientoConductor ──────────────────────────────────────────────
        var asientoGO  = new GameObject("AsientoConductor");
        asientoGO.transform.SetParent(go.transform, false);
        // Posición aproximada del conductor: lado izquierdo (volante europeo), a 1/4 de la longitud
        asientoGO.transform.localPosition = new Vector3(
            xLeft * 0.5f,
            bounds.max.y * 0.55f,   // altura de la cabeza aprox.
            cfg.semilargo * 0.35f); // algo adelantado del centro

        // ── IndicadorEntradaVehiculo ("Pulsa E") ──────────────────────────
        var indicGO = new GameObject("IndicadorEntrada");
        indicGO.transform.SetParent(go.transform, false);
        indicGO.transform.localPosition = Vector3.up * (bounds.max.y + 0.3f);
        if (System.Type.GetType("IndicadorEntradaVehiculo") != null)
            indicGO.AddComponent(System.Type.GetType("IndicadorEntradaVehiculo"));
        else
            indicGO.AddComponent<IndicadorEntradaVehiculo>();

        // ── MeshCollider del cuerpo ───────────────────────────────────────
        // Usar BoxCollider en vez de MeshCollider — más barato y suficiente
        // para una caja de colisión de coche
        var boxCol = go.GetComponent<BoxCollider>() ?? go.AddComponent<BoxCollider>();
        boxCol.center = bounds.center - go.transform.position;
        boxCol.size   = new Vector3(
            bounds.size.x * 0.90f,
            bounds.size.y * 0.85f,
            bounds.size.z * 0.95f);

        // ── Audio de motor ────────────────────────────────────────────────
        var motorGO  = new GameObject("AudioMotor");
        motorGO.transform.SetParent(go.transform, false);
        var motorSrc = motorGO.AddComponent<AudioSource>();
        motorSrc.spatialBlend = 1f;   // 3D
        motorSrc.loop         = true;
        motorSrc.volume       = 0.5f;
        motorSrc.minDistance  = 2f;
        motorSrc.maxDistance  = 30f;
        motorSrc.rolloffMode  = AudioRolloffMode.Linear;
        var motorClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/#Tools/Engine_07.wav")
                     ?? AssetDatabase.LoadAssetAtPath<AudioClip>(
                            "Assets/#Xtra/Standard Assets/Vehicles/Car/Audio/AccelerationLow.wav");

        // ── ControladorVehiculoJugador ────────────────────────────────────
        var ctrl = go.AddComponent<ControladorVehiculoJugador>();
        if (motorClip != null)
        {
            SetField(ctrl, "motorAudio", motorSrc);
            SetField(ctrl, "clipMotor",  motorClip);
            motorSrc.clip = motorClip;
        }
        SetField(ctrl, "rDI", rDI); SetField(ctrl, "rDD", rDD);
        SetField(ctrl, "rTI", rTI); SetField(ctrl, "rTD", rTD);
        SetField(ctrl, "asientoConductor", asientoGO.transform);
        SetField(ctrl, "parMaximo",        cfg.parMaximo);
        SetField(ctrl, "parFreno",         cfg.parFreno);
        SetField(ctrl, "parFrenoMano",     cfg.parFrenoMano);
        SetField(ctrl, "velocidadMax",     cfg.velocidadMax);
        SetField(ctrl, "cuatroRuedas",     cfg.cuatroRuedas);
        SetField(ctrl, "anguloMaxDireccion", cfg.anguloMaxDir);
        SetField(ctrl, "alturaCentroMasa", cfg.alturaCentroMasa);
        SetField(ctrl, "rigidezMuelle",    cfg.rigidezMuelle);
        SetField(ctrl, "amortiguacion",    cfg.amortiguacion);
        SetField(ctrl, "antiRollForce",    cfg.antiRoll);
        SetField(ctrl, "velocidadTransCamara", 5f);

        // Tag "Vehicle" — asignar si existe, sino dejar Untagged
        go.tag = UnityEditorInternal.InternalEditorUtility.tags.Contains("Vehicle")
            ? "Vehicle"
            : "Untagged";

        // ── Guardar prefab ─────────────────────────────────────────────────
        PrefabUtility.SaveAsPrefabAssetAndConnect(go, rutaPrefab, InteractionMode.AutomatedAction);
        Object.DestroyImmediate(go);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  COCHE PRIMITIVO (fallback si no hay modelo)
    // ─────────────────────────────────────────────────────────────────────────

    private static void CrearCochePrimitivo(string rutaPrefab)
    {
        var go = new GameObject("CochePrimitivo");

        // Cuerpo visible
        var cuerpo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cuerpo.transform.SetParent(go.transform, false);
        cuerpo.transform.localPosition = new Vector3(0, 0.6f, 0);
        cuerpo.transform.localScale    = new Vector3(1.8f, 1.2f, 4.2f);
        var mat = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.15f, 0.3f, 0.8f));
        else mat.color = new Color(0.15f, 0.3f, 0.8f);
        cuerpo.GetComponent<MeshRenderer>().sharedMaterial = mat;
        Object.DestroyImmediate(cuerpo.GetComponent<BoxCollider>());

        // Configurar como coche con parámetros genéricos
        ConfigurarYGuardar(go, rutaPrefab, new ConfigCoche
        {
            masa=1200f, parMaximo=250f, parFreno=700f, parFrenoMano=1200f,
            velocidadMax=25f, cuatroRuedas=false, anguloMaxDir=35f,
            radioRueda=0.35f, anchoRueda=0.2f, alturaCentroMasa=-0.3f,
            rigidezMuelle=35000f, amortiguacion=4500f, antiRoll=3500f,
            semilargo=1.80f, semiAncho=0.75f, alturaRueda=0.35f
        });

        Object.DestroyImmediate(go);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    private static WheelCollider CrearWheelCollider(GameObject padre, string nombre,
                                                     Vector3 posLocal, ConfigCoche cfg)
    {
        var wcGO = new GameObject(nombre);
        wcGO.transform.SetParent(padre.transform, false);
        wcGO.transform.localPosition = posLocal;

        var wc = wcGO.AddComponent<WheelCollider>();
        wc.radius = cfg.radioRueda;
        wc.mass   = 20f;
        wc.wheelDampingRate = 0.25f;

        wc.suspensionDistance = 0.15f;
        var spring = wc.suspensionSpring;
        spring.spring   = cfg.rigidezMuelle;
        spring.damper   = cfg.amortiguacion;
        spring.targetPosition = 0.5f;
        wc.suspensionSpring = spring;

        // Fricción mínima — el Pacejka del ControladorVehiculoJugador la sobreescribe
        var fwd = wc.forwardFriction;  fwd.stiffness  = 0.1f;  wc.forwardFriction  = fwd;
        var lat = wc.sidewaysFriction; lat.stiffness  = 0.1f;  wc.sidewaysFriction = lat;

        return wc;
    }

    private static Bounds CalcularBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(Vector3.up * 0.7f, new Vector3(1.8f, 1.4f, 4.2f));

        // Acumular bounds en espacio mundo y convertir al espacio local del GO
        var worldBounds = renderers[0].bounds;
        foreach (var r in renderers) worldBounds.Encapsulate(r.bounds);

        // Centrar relativo al pivot del GO (que puede estar en el suelo)
        Vector3 localCenter = go.transform.InverseTransformPoint(worldBounds.center);
        // El tamaño no depende de la rotación del GO (bounds ya es axis-aligned)
        return new Bounds(localCenter, worldBounds.size);
    }

    private static void SetField(object target, string field, object value)
    {
        var f = target.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null) f.SetValue(target, value);
        else Debug.LogWarning($"[WizardCoche] Campo '{field}' no encontrado en {target.GetType().Name}");
    }
}
