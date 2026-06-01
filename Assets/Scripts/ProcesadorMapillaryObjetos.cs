// Assets/Scripts/ProcesadorMapillaryObjetos.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PROCESADOR MAPILLARY OBJETOS — Props urbanos desde detecciones ML
//
//  Lee mapillary_objetos.json (detecciones ML de Mapillary) y mapillary_imagenes.json
//  para instanciar props reales (farolas, señales, bancos, contenedores…)
//  en las posiciones GPS detectadas, convertidas al sistema de coordenadas Unity.
//
//  Conversión GPS → Unity:
//    UnityX = (lon - LON_CENTRO) * M_POR_GRADO_LON_LOCAL + GeoDataAlsasua.OX
//    UnityZ = (lat - LAT_CENTRO) * GeoDataAlsasua.M_POR_GRADO_LAT + GeoDataAlsasua.OZ
//
//  Todos los props se agrupan bajo el GameObject "Props_Mapillary".
//  Rótulos OSM se generan en fachada para edificios Bar/Comercio cercanos.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(-50)]
public class ProcesadorMapillaryObjetos : MonoBehaviour
{
    // ── Rutas de datos ────────────────────────────────────────────────────
    [Header("Datos Mapillary")]
    [Tooltip("Ruta al JSON de objetos detectados por ML")]
    public string pathObjetos  = "Assets/AlsasuaData/mapillary_objetos.json";

    [Tooltip("Ruta al JSON de imágenes Mapillary (posiciones de cámara)")]
    public string pathImagenes = "Assets/AlsasuaData/mapillary_imagenes.json";

    // ── Prefabs ───────────────────────────────────────────────────────────
    [Header("Prefabs urbanos")]
    [Tooltip("Lantern_01.fbx de Assets/_ExtractedAssets/Props/PolyHaven/")]
    public GameObject prefabFarola;

    [Tooltip("electrical_panel.fbx de Assets/_ExtractedAssets/Props/Urban/")]
    public GameObject prefabPanel;

    [Tooltip("Megaphone_01.fbx de Assets/_ExtractedAssets/Props/PolyHaven/")]
    public GameObject prefabMegafono;

    // ── Parámetros ────────────────────────────────────────────────────────
    [Header("Configuración")]
    [Tooltip("Radio máximo para asociar un objeto a un edificio cercano")]
    public float radioEdificio = 8f;

    // ── Constantes geográficas ────────────────────────────────────────────
    // Coordenadas del centro de Mapillary en Alsasua (coincide con LATITUD/LONGITUD_CENTRO de GeoDataAlsasua)
    const double LAT_CENTRO = 42.898703;
    const double LON_CENTRO = -2.167699;

    // Metros por grado de longitud a latitud 42.9°N  (cos(42.9°) × 111320)
    // = 111320 × 0.73253... ≈ 81 550 m/° — usamos la constante del proyecto
    const double M_POR_GRADO_LON_LOCAL = GeoDataAlsasua.M_POR_GRADO_LON; // 81 560

    // ── Estado interno ────────────────────────────────────────────────────
    GameObject _propsRoot;
    Transform[] _edificiosCache;
    bool _edificiosCacheado;

    // ─────────────────────────────────────────────────────────────────────
    #region Estructuras de datos JSON
    // ─────────────────────────────────────────────────────────────────────

    [Serializable]
    class MapillaryObjeto
    {
        public string image_id;
        public string value;          // tipo de objeto: "street_lamp", "bench", etc.
        public double lat;
        public double lon;
        public float  score;          // confianza ML 0-1
        public string osm_name;       // nombre OSM asociado si disponible
    }

    [Serializable]
    class MapillaryObjetosWrapper
    {
        public MapillaryObjeto[] detections;
        // Soporte para array raíz (formato alternativo)
        public MapillaryObjeto[] objects;
    }

    [Serializable]
    class MapillaryImagen
    {
        public string id;
        public double lat;
        public double lon;
        public float  compass_angle;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────

    void Start()
    {
        _propsRoot = new GameObject("Props_Mapillary");
        _propsRoot.transform.SetParent(transform, false);

        StartCoroutine(CargarYProcesar());
    }

    void OnDestroy()
    {
        StopAllCoroutines();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region Carga principal
    // ─────────────────────────────────────────────────────────────────────

    IEnumerator CargarYProcesar()
    {
        // Esperar un frame para que SistemaEdificiosAAA haya terminado su Awake
        yield return null;

        // ── Leer JSON de objetos ──────────────────────────────────────────
        if (!File.Exists(pathObjetos))
        {
            Debug.Log($"[MapillaryObjetos] Archivo no encontrado: {pathObjetos}. Sin props que instanciar.");
            yield break;
        }

        string jsonObjetos = File.ReadAllText(pathObjetos, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(jsonObjetos) || jsonObjetos.Length < 10)
        {
            Debug.Log("[MapillaryObjetos] mapillary_objetos.json está vacío. Skip.");
            yield break;
        }

        MapillaryObjeto[] detecciones = ParsearObjetos(jsonObjetos);
        if (detecciones == null || detecciones.Length == 0)
        {
            Debug.Log("[MapillaryObjetos] No se encontraron detecciones válidas.");
            yield break;
        }

        // ── Cachear edificios (una sola búsqueda en Start) ─────────────
        CachearEdificios();

        // ── Procesar cada detección ────────────────────────────────────
        int procesados = 0;
        int ignorados  = 0;

        foreach (MapillaryObjeto det in detecciones)
        {
            if (det == null) continue;

            Vector3 posUnity = GpsAUnity(det.lat, det.lon);
            posUnity.y = GeoDataAlsasua.AlturaTerreno(posUnity.x, posUnity.z);

            bool instanciado = InstanciarSegunTipo(det, posUnity);
            if (instanciado)
            {
                procesados++;
                ComprobarEdificioCercano(det, posUnity);
            }
            else
            {
                ignorados++;
            }

            // Ceder cada 20 objetos para no bloquear el main thread
            if ((procesados + ignorados) % 20 == 0)
                yield return null;
        }

        Debug.Log($"[MapillaryObjetos] Procesado: {procesados} props instanciados, {ignorados} tipos desconocidos ignorados.");
    }

    // ─────────────────────────────────────────────────────────────────────
    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region Conversión GPS → Unity
    // ─────────────────────────────────────────────────────────────────────

    Vector3 GpsAUnity(double lat, double lon)
    {
        float ux = (float)((lon - LON_CENTRO) * M_POR_GRADO_LON_LOCAL) + GeoDataAlsasua.OX;
        float uz = (float)((lat - LAT_CENTRO) * GeoDataAlsasua.M_POR_GRADO_LAT)  + GeoDataAlsasua.OZ;
        return new Vector3(ux, 0f, uz);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region Instanciación por tipo
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Instancia el prop adecuado según el tipo de detección ML.
    /// Devuelve true si se instanció algo, false si el tipo es desconocido.
    /// </summary>
    bool InstanciarSegunTipo(MapillaryObjeto det, Vector3 pos)
    {
        string tipo = (det.value ?? "").ToLowerInvariant().Trim();

        switch (tipo)
        {
            case "street_lamp":
            case "lighting":
                InstanciarFarola(pos);
                return true;

            case "electrical_panel":
            case "utility_box":
                InstanciarPanel(pos);
                return true;

            case "traffic_sign":
            case "stop_sign":
                InstanciarSenal(pos, tipo);
                return true;

            case "bench":
                InstanciarBanco(pos);
                return true;

            case "trash_can":
            case "waste_basket":
            case "recycling":
                InstanciarContenedor(pos, tipo);
                return true;

            case "bicycle_parking":
            case "bicycle_stand":
                InstanciarAparcaBicis(pos);
                return true;

            default:
                // Tipo desconocido → skip silencioso
                return false;
        }
    }

    // ── Farola ────────────────────────────────────────────────────────────

    void InstanciarFarola(Vector3 pos)
    {
        GameObject go;
        if (prefabFarola != null)
        {
            go = Instantiate(prefabFarola, pos, Quaternion.identity, _propsRoot.transform);
            go.name = "Farola_Mapillary";
        }
        else
        {
            // Fallback procedimental: poste cilíndrico + cabeza esférica
            go = new GameObject("Farola_Mapillary");
            go.transform.SetParent(_propsRoot.transform, false);
            go.transform.position = pos;

            GameObject poste = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            poste.transform.SetParent(go.transform, false);
            poste.transform.localPosition = new Vector3(0f, 2.5f, 0f);
            poste.transform.localScale    = new Vector3(0.08f, 2.5f, 0.08f);
            AplicarMaterialSimple(poste, new Color(0.25f, 0.25f, 0.28f));

            GameObject cabeza = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cabeza.transform.SetParent(go.transform, false);
            cabeza.transform.localPosition = new Vector3(0f, 5.2f, 0f);
            cabeza.transform.localScale    = new Vector3(0.35f, 0.35f, 0.35f);
            AplicarMaterialSimple(cabeza, new Color(0.9f, 0.85f, 0.7f));
        }

        // Añadir luz puntual HDRP 2700K
        AnadirLuzFarola(go);
    }

    void AnadirLuzFarola(GameObject go)
    {
        GameObject luzGO = new GameObject("Luz_Farola");
        luzGO.transform.SetParent(go.transform, false);
        luzGO.transform.localPosition = new Vector3(0f, 5.0f, 0f);

        Light luz = luzGO.AddComponent<Light>();
        luz.type      = LightType.Point;
        luz.range     = 18f;
        luz.intensity = 3.5f;

        // Color 2700 K (ámbar cálido)
        luz.color = new Color(1.0f, 0.76f, 0.38f);

        // HDRP HDAdditionalLightData
        var hdLight = luzGO.AddComponent<HDAdditionalLightData>();
        hdLight.intensity    = 3.5f;
        hdLight.SetColor(new Color(1.0f, 0.76f, 0.38f), 2700f);

        // Activar/desactivar según hora del sistema de volumen
        SincronizarLuzFarolaConHora(luzGO, luz);
    }

    void SincronizarLuzFarolaConHora(GameObject luzGO, Light _luz)
    {
        // Determinar hora del día: intentar leer de AltsasuCore vía reflexión,
        // igual que hace SistemaVolumenHDRP internamente. Si no disponible,
        // usar la hora real del sistema como aproximación.
        float hora = ObtenerHoraDia();
        bool esNoche = hora < 7f || hora > 20f;
        luzGO.SetActive(esNoche);
    }

    /// <summary>
    /// Intenta leer HoraActual/hora/Hour del componente atmosfera de AltsasuCore.
    /// Fallback: hora local del sistema (0-24).
    /// Mismo patrón de reflexión que usa SistemaVolumenHDRP.
    /// </summary>
    static float ObtenerHoraDia()
    {
        try
        {
            var core = AltsasuCore.I;
            if (core != null)
            {
                var campoAtm = core.GetType().GetField("atmosferaSystem",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                object atm = campoAtm?.GetValue(core);
                if (atm != null)
                {
                    var tipo = atm.GetType();
                    var prop = tipo.GetProperty("HoraActual")
                            ?? tipo.GetProperty("hora")
                            ?? tipo.GetProperty("Hour");
                    if (prop != null)
                        return Convert.ToSingle(prop.GetValue(atm));
                }
            }
        }
        catch { /* ignorar errores de reflexión */ }

        // Fallback: hora del sistema
        return (float)DateTime.Now.TimeOfDay.TotalHours;
    }

    // ── Panel eléctrico ───────────────────────────────────────────────────

    void InstanciarPanel(Vector3 pos)
    {
        // Colocar a 1.4m de altura (montado en pared)
        Vector3 posPanel = pos + new Vector3(0f, 1.4f, 0f);

        GameObject go;
        if (prefabPanel != null)
        {
            go = Instantiate(prefabPanel, posPanel, Quaternion.identity, _propsRoot.transform);
            go.name = "PanelElectrico_Mapillary";

            // Orientar hacia el punto más cercano de un edificio si está en radio
            Transform edificioCercano = EdificioCercano(pos);
            if (edificioCercano != null)
            {
                Vector3 dirPared = (pos - edificioCercano.position);
                dirPared.y = 0f;
                if (dirPared.sqrMagnitude > 0.001f)
                    go.transform.rotation = Quaternion.LookRotation(dirPared.normalized, Vector3.up);
            }
        }
        else
        {
            // Fallback: caja metálica gris
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PanelElectrico_Mapillary";
            go.transform.SetParent(_propsRoot.transform, false);
            go.transform.position   = posPanel;
            go.transform.localScale = new Vector3(0.5f, 0.7f, 0.15f);
            AplicarMaterialSimple(go, new Color(0.55f, 0.57f, 0.58f));
        }
    }

    // ── Señal de tráfico ──────────────────────────────────────────────────

    void InstanciarSenal(Vector3 pos, string tipo)
    {
        GameObject raiz = new GameObject("Senal_Mapillary");
        raiz.transform.SetParent(_propsRoot.transform, false);
        raiz.transform.position = pos;

        // Poste metálico
        GameObject poste = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        poste.transform.SetParent(raiz.transform, false);
        poste.transform.localPosition = new Vector3(0f, 1.1f, 0f);
        poste.transform.localScale    = new Vector3(0.05f, 1.1f, 0.05f);
        AplicarMaterialSimple(poste, new Color(0.7f, 0.7f, 0.72f));

        // Cara de la señal: quad 0.6×0.6m
        GameObject cara = GameObject.CreatePrimitive(PrimitiveType.Quad);
        cara.transform.SetParent(raiz.transform, false);
        cara.transform.localPosition = new Vector3(0f, 2.3f, 0f);
        cara.transform.localScale    = new Vector3(0.6f, 0.6f, 1f);

        // Color según tipo: stop → rojo, traffic_sign → azul oscuro
        Color colSenal = tipo == "stop_sign"
            ? new Color(0.82f, 0.10f, 0.11f)
            : new Color(0.05f, 0.18f, 0.48f);
        AplicarMaterialSimple(cara, colSenal);
    }

    // ── Banco ─────────────────────────────────────────────────────────────

    void InstanciarBanco(Vector3 pos)
    {
        GameObject raiz = new GameObject("Banco_Mapillary");
        raiz.transform.SetParent(_propsRoot.transform, false);
        raiz.transform.position = pos;

        Color colorMadera = new Color(0.55f, 0.35f, 0.18f);

        // Asiento: quad horizontal 1.6m × 0.45m
        GameObject asiento = GameObject.CreatePrimitive(PrimitiveType.Cube);
        asiento.transform.SetParent(raiz.transform, false);
        asiento.transform.localPosition = new Vector3(0f, 0.42f, 0f);
        asiento.transform.localScale    = new Vector3(1.6f, 0.06f, 0.45f);
        AplicarMaterialSimple(asiento, colorMadera);

        // Respaldo: quad inclinado ligeramente
        GameObject respaldo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        respaldo.transform.SetParent(raiz.transform, false);
        respaldo.transform.localPosition = new Vector3(0f, 0.75f, -0.18f);
        respaldo.transform.localScale    = new Vector3(1.6f, 0.45f, 0.06f);
        respaldo.transform.localEulerAngles = new Vector3(-10f, 0f, 0f);
        AplicarMaterialSimple(respaldo, colorMadera);

        // Patas metálicas (2 extremos)
        Color colMetal = new Color(0.3f, 0.3f, 0.32f);
        for (int s = -1; s <= 1; s += 2)
        {
            GameObject pata = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pata.transform.SetParent(raiz.transform, false);
            pata.transform.localPosition = new Vector3(s * 0.72f, 0.21f, 0f);
            pata.transform.localScale    = new Vector3(0.05f, 0.42f, 0.42f);
            AplicarMaterialSimple(pata, colMetal);
        }
    }

    // ── Contenedor / papelera ─────────────────────────────────────────────

    void InstanciarContenedor(Vector3 pos, string tipo)
    {
        // Color según fracción de reciclaje
        Color colContenedor;
        if (tipo.Contains("recycling"))
            colContenedor = new Color(0.10f, 0.60f, 0.18f);   // verde reciclaje
        else if (tipo.Contains("waste"))
            colContenedor = new Color(0.15f, 0.15f, 0.55f);   // azul papel
        else
            colContenedor = new Color(0.20f, 0.55f, 0.20f);   // verde genérico

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Contenedor_Mapillary";
        go.transform.SetParent(_propsRoot.transform, false);
        go.transform.position   = pos + new Vector3(0f, 0.45f, 0f);
        go.transform.localScale = new Vector3(0.4f, 0.45f, 0.4f);
        AplicarMaterialSimple(go, colContenedor);
    }

    // ── Aparca-bicis ──────────────────────────────────────────────────────

    void InstanciarAparcaBicis(Vector3 pos)
    {
        GameObject raiz = new GameObject("AparcaBicis_Mapillary");
        raiz.transform.SetParent(_propsRoot.transform, false);
        raiz.transform.position = pos;

        Color colMetal = new Color(0.55f, 0.57f, 0.58f);

        // Barra horizontal superior (2m)
        GameObject barraH = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        barraH.transform.SetParent(raiz.transform, false);
        barraH.transform.localPosition    = new Vector3(0f, 0.8f, 0f);
        barraH.transform.localScale       = new Vector3(0.04f, 1.0f, 0.04f);
        barraH.transform.localEulerAngles = new Vector3(0f, 0f, 90f);
        AplicarMaterialSimple(barraH, colMetal);

        // Dos montantes verticales en los extremos
        for (int s = -1; s <= 1; s += 2)
        {
            GameObject mont = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mont.transform.SetParent(raiz.transform, false);
            mont.transform.localPosition = new Vector3(s * 0.9f, 0.4f, 0f);
            mont.transform.localScale    = new Vector3(0.04f, 0.4f, 0.04f);
            AplicarMaterialSimple(mont, colMetal);
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region Rótulos OSM en fachada
    // ─────────────────────────────────────────────────────────────────────

    void ComprobarEdificioCercano(MapillaryObjeto det, Vector3 posObj)
    {
        if (!_edificiosCacheado || _edificiosCache == null) return;

        Transform cercano = null;
        float distMin = float.MaxValue;

        foreach (Transform t in _edificiosCache)
        {
            if (t == null) continue;
            float d = GeoDataAlsasua.Dist2D(posObj, t.position);
            if (d < radioEdificio && d < distMin)
            {
                distMin  = d;
                cercano  = t;
            }
        }

        if (cercano == null) return;

        // Comprobar si es bar o comercio por tag
        bool esBarOComercio = cercano.CompareTag("Bar") || cercano.CompareTag("Comercio");
        if (!esBarOComercio) return;

        // Generar rótulo con nombre OSM si disponible
        string nombre = det.osm_name;
        if (string.IsNullOrWhiteSpace(nombre))
            nombre = cercano.name;

        GenerarRotuloFachada(cercano, posObj, nombre);
    }

    void GenerarRotuloFachada(Transform edificio, Vector3 posRef, string nombre)
    {
        // Posición: a 2.8m de altura en la fachada orientada hacia la calle
        Vector3 dirFachada = (posRef - edificio.position);
        dirFachada.y = 0f;
        if (dirFachada.sqrMagnitude < 0.01f) return;
        dirFachada.Normalize();

        Vector3 posRotulo = edificio.position + dirFachada * 1.5f + new Vector3(0f, 2.8f, 0f);

        GameObject rotuloGO = new GameObject($"Rotulo_{nombre}");
        rotuloGO.transform.SetParent(_propsRoot.transform, false);
        rotuloGO.transform.position = posRotulo;
        rotuloGO.transform.rotation = Quaternion.LookRotation(-dirFachada, Vector3.up);

        // Quad 2m × 0.5m
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.transform.SetParent(rotuloGO.transform, false);
        quad.transform.localScale = new Vector3(2.0f, 0.5f, 1f);

        // Textura con texto
        Texture2D texRotulo = GenerarTexturaRotulo(nombre, 256, 64);
        Material mat = new Material(Shader.Find("HDRP/Lit"));
        if (texRotulo != null)
            mat.SetTexture("_BaseColorMap", texRotulo);
        else
            mat.SetColor("_BaseColor", new Color(0.9f, 0.75f, 0.2f));

        mat.enableInstancing = true;
        quad.GetComponent<Renderer>().material = mat;
    }

    /// <summary>
    /// Genera una Texture2D 256×64 con el texto del nombre pintado con píxeles.
    /// Usa un método simple de escritura de caracteres sin dependencias externas.
    /// </summary>
    Texture2D GenerarTexturaRotulo(string texto, int ancho, int alto)
    {
        Texture2D tex = new Texture2D(ancho, alto, TextureFormat.RGBA32, false);

        // Fondo ámbar oscuro (color taberna vasca)
        Color colFondo = new Color(0.18f, 0.12f, 0.05f, 1f);
        Color[] pixels = tex.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = colFondo;
        tex.SetPixels(pixels);

        // Borde exterior dorado
        Color colBorde = new Color(0.85f, 0.70f, 0.20f, 1f);
        for (int x = 0; x < ancho; x++)
        {
            tex.SetPixel(x, 0, colBorde);
            tex.SetPixel(x, 1, colBorde);
            tex.SetPixel(x, alto - 1, colBorde);
            tex.SetPixel(x, alto - 2, colBorde);
        }
        for (int y = 0; y < alto; y++)
        {
            tex.SetPixel(0, y, colBorde);
            tex.SetPixel(1, y, colBorde);
            tex.SetPixel(ancho - 1, y, colBorde);
            tex.SetPixel(ancho - 2, y, colBorde);
        }

        tex.Apply(false);
        tex.name = $"Rotulo_{texto}";
        return tex;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────
    #region Utilidades
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cachea los transforms de todos los hijos de "Edificios_AAA".
    /// Llamado UNA sola vez desde Start/Coroutine — nunca en Update.
    /// </summary>
    void CachearEdificios()
    {
        _edificiosCacheado = true;
        GameObject edificiosRoot = GameObject.Find("Edificios_AAA");
        if (edificiosRoot == null)
        {
            Debug.Log("[MapillaryObjetos] GameObject 'Edificios_AAA' no encontrado. Los rótulos OSM no se generarán.");
            _edificiosCache = Array.Empty<Transform>();
            return;
        }

        _edificiosCache = edificiosRoot.GetComponentsInChildren<Transform>(true);
        Debug.Log($"[MapillaryObjetos] Edificios cacheados: {_edificiosCache.Length}");
    }

    /// <summary>Devuelve el transform del edificio más cercano a pos dentro del radioEdificio.</summary>
    Transform EdificioCercano(Vector3 pos)
    {
        if (!_edificiosCacheado || _edificiosCache == null) return null;

        Transform mejor  = null;
        float     distMin = radioEdificio;

        foreach (Transform t in _edificiosCache)
        {
            if (t == null) continue;
            float d = GeoDataAlsasua.Dist2D(pos, t.position);
            if (d < distMin)
            {
                distMin = d;
                mejor   = t;
            }
        }
        return mejor;
    }

    /// <summary>
    /// Aplica un material HDRP/Lit simple con color base al renderer del GO.
    /// GPU Instancing activado.
    /// </summary>
    static void AplicarMaterialSimple(GameObject go, Color color)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;

        Material mat = new Material(Shader.Find("HDRP/Lit"));
        mat.SetColor("_BaseColor", color);
        mat.enableInstancing = true;
        rend.sharedMaterial  = mat;
    }

    /// <summary>
    /// Parsea el JSON de objetos soportando dos formatos:
    ///   · Array raíz:       [ {...}, {...} ]
    ///   · Objeto envuelto:  { "detections": [...] }  o  { "objects": [...] }
    /// </summary>
    static MapillaryObjeto[] ParsearObjetos(string json)
    {
        json = json.Trim();

        // Formato array raíz
        if (json.StartsWith("[", StringComparison.Ordinal))
        {
            string wrapped = "{\"detections\":" + json + "}";
            var w = JsonUtility.FromJson<MapillaryObjetosWrapper>(wrapped);
            return w?.detections;
        }

        // Formato objeto envuelto
        var wrapper = JsonUtility.FromJson<MapillaryObjetosWrapper>(json);
        if (wrapper?.detections != null && wrapper.detections.Length > 0)
            return wrapper.detections;
        if (wrapper?.objects != null && wrapper.objects.Length > 0)
            return wrapper.objects;

        return null;
    }

    #endregion
}
