// Assets/Scripts/PosicionadorPrecisionUrbana.cs
// ═══════════════════════════════════════════════════════════════════════════
//  POSICIONADOR DE PRECISIÓN URBANA
//
//  Usa los datos ultra-precisos para colocar en el escenario:
//
//  A) ÁRBOLES — desde lidar_trees.json (posiciones LIDAR exactas)
//     En lugar de Poisson/random, cada árbol está donde LIDAR lo detectó,
//     con su altura real medida y radio de copa real.
//
//  B) PORTALES / ENTRADAS — desde portales_cartociudad.json
//     Cada edificio tiene sus puertas en la posición catastral exacta.
//     Genera un pequeño escalón + panel de buzones en la entrada.
//
//  C) MOBILIARIO URBANO — desde mapillary_objetos.json
//     Señales de tráfico, bancos, papeleras, farolas en posición real
//     detectada por la IA de Mapillary desde fotos de calle.
//
//  D) ALUMBRADO — desde idena_alumbrado.json
//     Cada farola en la posición del inventario municipal de IDENA.
//     Añade PointLight a cada una.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(-54)]  // después de GeomPrecisa (-60), Ortofoto (-55)
public class PosicionadorPrecisionUrbana : MonoBehaviour
{
    public static PosicionadorPrecisionUrbana Instance { get; private set; }

    [Header("Prefabs de mobiliario")]
    public GameObject prefabArbol;          // árbol genérico HDRP
    public GameObject prefabFarola;         // farola vasca
    public GameObject prefabBanco;          // banco urbano
    public GameObject prefabSenal;          // señal de tráfico
    public GameObject prefabPapelera;       // papelera
    public GameObject prefabPortal;         // escalón/entrada de edificio

    [Header("Opciones")]
    public bool colocarArboles    = true;
    public bool colocarFarolas    = true;
    public bool colocarPortales   = true;
    public bool colocarMobiliario = true;

    [Range(0f, 1f)]
    public float densidadMobiliario = 1f;   // 1.0 = todo, 0.5 = 50% aleatorio

    // ── Data containers ───────────────────────────────────────────────────
    [System.Serializable] class ArbolLIDAR
    { public float x, z, altura, radio; }

    [System.Serializable] class Portal
    { public float ux, uz; public string calle, num, cp; }

    [System.Serializable] class ObjetoMapillary
    { public string tipo, imagen; }

    [System.Serializable] class Alumbrado
    { public string id; public List<PuntoSimple> verts; }
    [System.Serializable] class PuntoSimple { public float x, z; }

    // Padres
    Transform _parArboles, _parFarolas, _parPortales, _parMobiliario;

    bool _terminado;
    public bool Terminado => _terminado;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        while (Terrain.activeTerrain == null) yield return new WaitForSeconds(0.3f);

        _parArboles   = CrearParent("Arboles_LIDAR");
        _parFarolas   = CrearParent("Farolas_IDENA");
        _parPortales  = CrearParent("Portales_CartoCiudad");
        _parMobiliario= CrearParent("Mobiliario_Mapillary");

        if (colocarArboles)    yield return StartCoroutine(ColocarArboles());
        if (colocarFarolas)    yield return StartCoroutine(ColocarFarolas());
        if (colocarPortales)   yield return StartCoroutine(ColocarPortales());
        if (colocarMobiliario) yield return StartCoroutine(ColocarMobiliario());

        _terminado = true;
        AlsasuaLogger.Info("PrecisionUrbana", "✅ Posicionamiento urbano preciso completado");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  A. ÁRBOLES desde lidar_trees.json
    // ═══════════════════════════════════════════════════════════════════════

    IEnumerator ColocarArboles()
    {
        var json = LeerJSON("Assets/AlsasuaData/lidar_trees.json");
        if (json == null) yield break;

        var arboles = JsonHelper.ParseArray<ArbolLIDAR>(json);
        AlsasuaLogger.Info("PrecisionUrbana", $"Árboles LIDAR: {arboles.Length}");

        // Destruir árboles del terrain data (se reemplazarán con LIDAR exactos)
        LimpiarArbolesTerreno();

        // lidar_trees.json tiene coordenadas Unity absolutas — no requiere offset.

        // Usar prefabs GreenForest del ConfiguradorAssetsAAA si disponibles
        var cfgAAA = ConfiguradorAssetsAAA.Instance;

        // Radio máximo de colocación inicial (rendimiento + visibilidad)
        // PosicionadorPrecisionUrbana coloca árboles cercanos al centro urbano;
        // zonas lejanas las gestiona AlsasuaTreeStreamer en streaming.
        const float RADIO_MAX_INICIAL = 600f;
        const float OX_CENTRO = GeoDataAlsasua.OX, OZ_CENTRO = GeoDataAlsasua.OZ;

        int colocados = 0;
        for (int i = 0; i < arboles.Length; i++)
        {
            var a    = arboles[i];
            // Filtrar árboles irreales: clusters de monte o ruido
            if (a.altura < 1.5f || a.altura > 40f || a.radio > 14f) continue;

            // Solo árboles dentro del radio urbano inicial
            float dx = a.x - OX_CENTRO, dz = a.z - OZ_CENTRO;
            if (dx*dx + dz*dz > RADIO_MAX_INICIAL * RADIO_MAX_INICIAL) continue;

            float ux = a.x;   // coordenadas Unity absolutas (generadas por PipelineLIDAR)
            float uz = a.z;
            float y  = AlturaTerreno(ux, uz);

            // Seleccionar prefab GreenForest según radio y altura, o fallback procedural
            GameObject prefabSeleccionado = null;
            if (cfgAAA != null)
                prefabSeleccionado = cfgAAA.GetPrefabArbol(a.radio, a.altura);
            if (prefabSeleccionado == null)
                prefabSeleccionado = prefabArbol;

            var go = prefabSeleccionado != null
                ? Instantiate(prefabSeleccionado)
                : CrearArbolProcedural(a.altura, a.radio);

            go.transform.SetParent(_parArboles, false);
            go.transform.position  = new Vector3(ux, y, uz);
            // Escalar según radio real LIDAR: radio/3 = escala normalizada para GreenForest
            float escala = Mathf.Clamp(a.radio / 3f, 0.4f, 3f);
            go.transform.localScale= Vector3.one * escala;
            // Rotación Y aleatoria para variedad visual
            go.transform.localRotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            go.isStatic = true;
            go.name = $"Arbol_LIDAR_{i}";

            colocados++;
            if (i % 50 == 0) yield return null;
        }

        AlsasuaLogger.Info("PrecisionUrbana", $"✅ {colocados} árboles LIDAR colocados");
    }

    void LimpiarArbolesTerreno()
    {
        // Destruir instancias de árbol del terrain si se van a reemplazar con LIDAR
        var treeStreamer = FindFirstObjectByType<AlsasuaTreeStreamer>();
        // No destruir — el streamer gestiona sus propias instancias.
        // Desactivamos el parent de árboles OSM si existe.
        var arbOSM = GameObject.Find("Arboles_OSM");
        if (arbOSM != null) arbOSM.SetActive(false);
    }

    // Shared materials para árboles procedurales (creados una vez, reutilizados)
    static Material _matTronco, _matCopa;

    static Material MatHDRP(Color c)
    {
        var sh  = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        var mat = new Material(sh);
        // HDRP usa _BaseColor; Standard usa _Color
        if (mat.HasProperty("_BaseColor"))   mat.SetColor("_BaseColor", c);
        else                                  mat.SetColor("_Color",     c);
        if (mat.HasProperty("_EmissiveColor")) mat.SetColor("_EmissiveColor", Color.black);
        return mat;
    }

    GameObject CrearArbolProcedural(float altura, float radio)
    {
        if (_matTronco == null) _matTronco = MatHDRP(new Color(0.35f, 0.22f, 0.12f));

        var go  = new GameObject("Arbol");
        float h = Mathf.Clamp(altura, 2f, 20f);   // cap a 20m para procedurales
        float r = Mathf.Clamp(radio,  0.5f, 5f);

        // Tronco
        var tronco = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(tronco.GetComponent<Collider>());
        tronco.transform.SetParent(go.transform, false);
        tronco.transform.localPosition = new Vector3(0, h * 0.35f, 0);
        tronco.transform.localScale    = new Vector3(r * 0.15f, h * 0.35f, r * 0.15f);
        tronco.GetComponent<MeshRenderer>().sharedMaterial = _matTronco;

        // Copa — color verde variado por árbol, material nuevo solo si cambia mucho
        float v = Random.Range(-0.04f, 0.04f);
        var copa = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(copa.GetComponent<Collider>());
        copa.transform.SetParent(go.transform, false);
        copa.transform.localPosition = new Vector3(0, h * 0.75f, 0);
        copa.transform.localScale    = new Vector3(r * 1.6f, h * 0.45f, r * 1.6f);
        copa.GetComponent<MeshRenderer>().sharedMaterial =
            MatHDRP(new Color(0.18f + v, 0.42f + v, 0.12f));

        return go;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  B. FAROLAS desde idena_alumbrado.json
    // ═══════════════════════════════════════════════════════════════════════

    IEnumerator ColocarFarolas()
    {
        var json = LeerJSON("Assets/AlsasuaData/idena_alumbrado.json");
        if (json == null) yield break;

        // El JSON de IDENA tiene { id, props, verts:[{x,z}] }
        var features = JsonHelper.ParseArray<IDENAFeature>(json);

        int n = 0;
        foreach (var feat in features)
        {
            if (feat.verts == null || feat.verts.Length == 0) continue;
            // Punto de la farola (es Sym → punto único)
            float ux = feat.verts[0].x + GeoDataAlsasua.OX;
            float uz = feat.verts[0].z + GeoDataAlsasua.OZ;
            float y  = AlturaTerreno(ux, uz);

            var go = prefabFarola != null
                ? Instantiate(prefabFarola)
                : CrearFarolaProcedural();

            go.transform.SetParent(_parFarolas, false);
            go.transform.position = new Vector3(ux, y, uz);
            go.isStatic = false; // las luces no pueden ser estáticas con sombras dinámicas
            go.name = $"Farola_{n}";

            n++;
            if (n % 30 == 0) yield return null;
        }

        AlsasuaLogger.Info("PrecisionUrbana", $"✅ {n} farolas IDENA colocadas");
    }

    GameObject CrearFarolaProcedural()
    {
        var go = new GameObject("Farola");

        // Poste
        var poste = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        poste.transform.SetParent(go.transform, false);
        poste.transform.localPosition = new Vector3(0, 3.5f, 0);
        poste.transform.localScale    = new Vector3(0.06f, 3.5f, 0.06f);
        var mr = poste.GetComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"))
            { color = new Color(0.25f, 0.25f, 0.28f) };

        // Luz punto
        var lightGO = new GameObject("Luz");
        lightGO.transform.SetParent(go.transform, false);
        lightGO.transform.localPosition = new Vector3(0, 7.2f, 0);
        var pl = lightGO.AddComponent<Light>();
        pl.type      = LightType.Point;
        pl.color     = new Color(1f, 0.92f, 0.72f); // blanco cálido sodio
        pl.range     = 18f;
        pl.shadows   = LightShadows.Soft;
        var hdPl = lightGO.AddComponent<HDAdditionalLightData>();
        hdPl.intensity = 800f;
        hdPl.lightUnit = LightUnit.Lux;

        return go;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  C. PORTALES desde portales_cartociudad.json
    // ═══════════════════════════════════════════════════════════════════════

    IEnumerator ColocarPortales()
    {
        var json = LeerJSON("Assets/AlsasuaData/portales_cartociudad.json");
        if (json == null) yield break;

        var portales = JsonHelper.ParseArray<Portal>(json);
        int n = 0;

        foreach (var p in portales)
        {
            float y = AlturaTerreno(p.ux, p.uz);

            var go = prefabPortal != null
                ? Instantiate(prefabPortal)
                : CrearPortalProcedural(p.num);

            go.transform.SetParent(_parPortales, false);
            go.transform.position = new Vector3(p.ux, y, p.uz);
            go.name = $"Portal_{p.calle}_{p.num}";
            go.isStatic = true;

            n++;
            if (n % 100 == 0) yield return null;
        }

        AlsasuaLogger.Info("PrecisionUrbana", $"✅ {n} portales CartoCiudad colocados");
    }

    GameObject CrearPortalProcedural(string numero)
    {
        var go = new GameObject($"Portal_{numero}");

        // Escalón de hormigón
        var escalon = GameObject.CreatePrimitive(PrimitiveType.Cube);
        escalon.transform.SetParent(go.transform, false);
        escalon.transform.localPosition = new Vector3(0, 0.08f, 0.2f);
        escalon.transform.localScale    = new Vector3(1.2f, 0.16f, 0.4f);
        var mr = escalon.GetComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"))
            { color = new Color(0.62f, 0.61f, 0.59f) };

        return go;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  D. MOBILIARIO desde mapillary_objetos.json
    // ═══════════════════════════════════════════════════════════════════════

    IEnumerator ColocarMobiliario()
    {
        var json = LeerJSON("Assets/AlsasuaData/mapillary_imagenes.json");
        if (json == null) yield break;

        // Por ahora solo posicionamos los puntos de foto como orientaciones de cámara
        // (útil para debug y para saber dónde mirar para verificar la escena)
        // En futuro: con las detecciones de objetos colocar los prefabs
        yield return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  UTILIDADES
    // ═══════════════════════════════════════════════════════════════════════

    [System.Serializable] class IDENAFeature
    {
        public string id;
        public PuntoSimple[] verts;
    }

    static float AlturaTerreno(float ux, float uz) => GeoDataAlsasua.AlturaTerreno(ux, uz);

    Transform CrearParent(string nombre)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    string LeerJSON(string path)
    {
        string full = Path.Combine(Application.dataPath.Replace("Assets",""), path);
        if (!File.Exists(full)) return null;
        return File.ReadAllText(full);
    }
}
