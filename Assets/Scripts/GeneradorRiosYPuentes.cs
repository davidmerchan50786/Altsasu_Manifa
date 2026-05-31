// Assets/Scripts/GeneradorRiosYPuentes.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GENERADOR DE RÍOS Y PUENTES — AAA+++ v1.0
//
//  Pipeline:
//    1. Carga rios_ejes.geojson  → polilíneas de eje de cauce en UTM
//    2. Excava el terrain Unity con perfil transversal V-asimétrico:
//         Arakil: ancho=8m, prof=1.8m
//         Arroyos: ancho=2m, prof=0.5m
//    3. Valida zonas de agua contra lidar_agua.json (ajuste local)
//    4. Instancia plano de agua HDRP con:
//         · SSR activado, flow map dinámico desde pendiente cauce
//         · Color vasco #1a3520, foam en rápidos, caustics bajo agua
//    5. Carga lidar_puentes.json, instancia bridge_roads.fbx orientado
//       por la tangente del río en el punto de cruce
//
//  Coordenadas:
//    UnityX = (E − 567951) + 1918
//    UnityZ = (N − 4749902) + 8570
//    AltitudUnity = altitud_real − 511.33
//
//  Orden de ejecución: −50 (después del terreno −70/−68, antes de edificios)
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(-50)]
public class GeneradorRiosYPuentes : MonoBehaviour
{
    public static GeneradorRiosYPuentes Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────
    [Header("Parámetros Excavación")]
    [Tooltip("Ancho del cauce principal Arakil (m)")]
    public float anchoArakil   = 8f;
    [Tooltip("Profundidad de excavación Arakil (m)")]
    public float profArakil    = 1.8f;
    [Tooltip("Ancho de arroyos secundarios (m)")]
    public float anchoArroyo   = 2f;
    [Tooltip("Profundidad de excavación arroyos (m)")]
    public float profArroyo    = 0.5f;
    [Tooltip("Resolución del kernel de excavación (muestras por metro)")]
    public float resKernel     = 2f;

    [Header("Agua HDRP")]
    [Tooltip("Shader de agua HDRP (deja vacío = busca HDRP/Water o fallback)")]
    public Shader shaderAgua;
    [Tooltip("Offset Y del plano de agua sobre el fondo del cauce (m)")]
    public float offsetAguaY   = 0.15f;
    [Tooltip("Flow map — escala de velocidad del flujo")]
    [Range(0.01f, 2f)]
    public float flowSpeed     = 0.3f;
    [Tooltip("Intensidad de foam en pendientes >2%")]
    [Range(0f, 1f)]
    public float foamIntensity = 0.7f;

    [Header("Puentes")]
    [Tooltip("FBX de puente a instanciar (bridge_roads.fbx)")]
    public GameObject prefabPuente;
    [Tooltip("Escala aplicada al FBX de puente")]
    public Vector3 escalaPuente = Vector3.one;

    [Header("Debug")]
    public bool logVerboso = true;

    // ── Rutas ─────────────────────────────────────────────────────────────
    const string PATH_RIOS    = "Assets/AlsasuaData/rios_ejes.geojson";
    const string PATH_AGUA    = "Assets/AlsasuaData/lidar_agua.json";
    const string PATH_PUENTES = "Assets/AlsasuaData/lidar_puentes.json";
    const string PATH_PUENTE_FBX = "Assets/_ExtractedAssets/Props/Urban/bridge_roads.fbx";

    // ── Constantes geográficas — fuente única: GeoDataAlsasua ────────────
    const float E_ORIG   = (float)GeoDataAlsasua.UTM_E_ORIGIN;
    const float N_ORIG   = (float)GeoDataAlsasua.UTM_N_ORIGIN;
    const float UNITY_OX = GeoDataAlsasua.UNITY_OX;
    const float UNITY_OZ = GeoDataAlsasua.UNITY_OZ;
    const float Z_MIN    = GeoDataAlsasua.Z_MIN;

    // ── Color base del agua (verde vasco) ─────────────────────────────────
    static readonly Color COLOR_AGUA = new(0x1a / 255f, 0x35 / 255f, 0x20 / 255f, 0.82f);

    // ── Datos internos ────────────────────────────────────────────────────
    List<RioCauce> _cauces     = new();
    List<PuenteDato> _puentes  = new();
    List<LidarAguaPunto> _puntosLidarAgua = new();
    Transform _parentAgua;
    Transform _parentPuentes;

    // ════════════════════════════════════════════════════════════════════════
    //  ESTRUCTURAS DE DATOS
    // ════════════════════════════════════════════════════════════════════════

    class RioCauce
    {
        public string nombre;
        public bool   esArakil;          // true = cauce principal Arakil
        public Vector3[] puntosUnity;   // posiciones XZ en Unity (Y = altura terreno)
        public float  ancho;
        public float  prof;
        public List<GameObject> planosAgua = new();
    }

    [Serializable] class GeoJSONFeatureCollection { public List<GeoJSONFeature> features; }
    [Serializable] class GeoJSONFeature           { public GeoJSONProperties properties; public GeoJSONGeometry geometry; }
    [Serializable] class GeoJSONProperties        { public string name, nombre, waterway, @ref; }
    [Serializable] class GeoJSONGeometry          { public string type; public List<float[]> coordinates; public List<List<float[]>> coordinates2; }

    [Serializable] class LidarAguaPunto { public float x, z, altitud; }
    [Serializable] class LidarPuenteDato
    {
        public float x, z, altitud;
        public float angulo;        // ángulo en grados (0=norte) si disponible
        public string nombre;
    }
    class PuenteDato
    {
        public Vector3 posUnity;
        public float   angulo;
        public string  nombre;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        // Esperar a que el terreno esté listo
        float t = 0f;
        while (Terrain.activeTerrain == null && t < 15f)
            { t += 0.25f; yield return new WaitForSeconds(0.25f); }

        if (Terrain.activeTerrain == null)
        { AlsasuaLogger.Warn("Rios","Sin terreno activo — GeneradorRiosYPuentes inactivo"); yield break; }

        _parentAgua    = new GameObject("Agua_Rios").transform;
        _parentPuentes = new GameObject("Puentes").transform;
        _parentAgua.SetParent(transform, false);
        _parentPuentes.SetParent(transform, false);

        // Cargar geodatos en background
        yield return StartCoroutine(CargarGeoJSON());
        yield return StartCoroutine(CargarLidarAguaYPuentes());

        if (_cauces.Count == 0)
        { AlsasuaLogger.Warn("Rios","Sin cauces cargados"); yield break; }

        // Excavar terrain
        yield return StartCoroutine(ExcavarTerrain());

        // Crear planos de agua (cauces GeoJSON)
        yield return StartCoroutine(CrearPlanosAgua());

        // Colocar láminas de agua en puntos LIDAR clase 9 (charcos, acequias, etc.)
        yield return StartCoroutine(ColocarAguaLidar());

        // Colocar puentes
        yield return StartCoroutine(ColocarPuentes());

        AlsasuaLogger.Info("Rios",
            $"✅ {_cauces.Count} cauces excavados, {_puentes.Count} puentes colocados");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CARGA GEOJSON — rios_ejes.geojson
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator CargarGeoJSON()
    {
        string fullPath = FullPath(PATH_RIOS);
        if (!File.Exists(fullPath))
        { AlsasuaLogger.Warn("Rios",$"rios_ejes.geojson no encontrado: {fullPath}"); yield break; }

        // Cache terrain data on main thread BEFORE entering Task.Run (Unity API is not thread-safe)
        var terrainPos  = Terrain.activeTerrain.transform.position;
        var terrainSize = Terrain.activeTerrain.terrainData.size;
        int hRes        = Terrain.activeTerrain.terrainData.heightmapResolution;
        float[,] heightsCopy = Terrain.activeTerrain.terrainData.GetHeights(0, 0, hRes, hRes);

        var task = Task.Run(() => ParsearGeoJSON(fullPath, terrainPos, terrainSize, hRes, heightsCopy));
        yield return EsperarTask(task, "GeoJSON rios");
        if (task.IsCompletedSuccessfully) _cauces = task.Result;

        AlsasuaLogger.Info("Rios",
            $"{_cauces.Count} cauces cargados ({_cauces.FindAll(c=>c.esArakil).Count} Arakil)");
    }

    static List<RioCauce> ParsearGeoJSON(string path, Vector3 terrainPos, Vector3 terrainSize, int hRes, float[,] heightsCopy)
    {
        var result = new List<RioCauce>();
        var ci     = System.Globalization.CultureInfo.InvariantCulture;
        // NOTE: All terrain data is passed in as plain values — no Unity API access in this method
        try
        {
            string json = File.ReadAllText(path);
            // Parser manual ligero por compatibilidad
            int searchPos = 0;
            int featIdx   = 0;
            while (true)
            {
                // Buscar bloque "coordinates"
                int coordIdx = json.IndexOf("\"coordinates\"", searchPos);
                if (coordIdx < 0) break;

                // Extraer nombre/waterway del feature (buscar hacia atrás)
                string nombre    = "";
                bool   esArakil  = false;
                int    propStart = json.LastIndexOf("\"properties\"", coordIdx);
                if (propStart >= 0)
                {
                    int propEnd = json.IndexOf("\"geometry\"", propStart);
                    if (propEnd < 0) propEnd = coordIdx;
                    string propBlock = json.Substring(propStart, propEnd - propStart);
                    nombre   = ExtraerStringProp(propBlock, "name");
                    if (string.IsNullOrEmpty(nombre)) nombre = ExtraerStringProp(propBlock, "nombre");
                    string waterway = ExtraerStringProp(propBlock, "waterway");
                    esArakil = nombre.ToLower().Contains("arakil")
                             || waterway.ToLower().Contains("river")
                             || waterway.ToLower().Contains("stream");
                }

                // Extraer puntos de coordenadas
                int bracketStart = json.IndexOf('[', coordIdx + 13);
                if (bracketStart < 0) { searchPos = coordIdx + 14; continue; }

                var pts2D = ExtraerPuntosUTM(json, bracketStart);

                if (pts2D != null && pts2D.Length >= 2)
                {
                    // Convertir UTM → Unity XZ (Y se asignará en hilo principal)
                    var pts3D = new Vector3[pts2D.Length];
                    for (int i = 0; i < pts2D.Length; i++)
                        pts3D[i] = new Vector3(pts2D[i].x, 0f, pts2D[i].y);

                    bool grande = esArakil || nombre.ToLower().Contains("arakil")
                               || (pts2D.Length > 50); // heurística: río largo = Arakil
                    result.Add(new RioCauce
                    {
                        nombre      = string.IsNullOrEmpty(nombre) ? $"Cauce_{featIdx}" : nombre,
                        esArakil    = grande,
                        puntosUnity = pts3D,
                        ancho       = grande ? 8f : 2f,
                        prof        = grande ? 1.8f : 0.5f,
                    });
                    featIdx++;
                }

                searchPos = bracketStart + 1;
            }
        }
        catch (Exception e) { Debug.LogWarning($"[Rios] ParsearGeoJSON: {e.Message}"); }
        return result;
    }

    static string ExtraerStringProp(string block, string key)
    {
        string search = $"\"{key}\"";
        int k = block.IndexOf(search);
        if (k < 0) return "";
        int colon = block.IndexOf(':', k + search.Length);
        if (colon < 0) return "";
        int q1 = block.IndexOf('"', colon + 1);
        if (q1 < 0) return "";
        int q2 = block.IndexOf('"', q1 + 1);
        if (q2 < 0) return "";
        return block.Substring(q1 + 1, q2 - q1 - 1);
    }

    static Vector2[] ExtraerPuntosUTM(string json, int startBracket)
    {
        var pts  = new List<Vector2>();
        var ci   = System.Globalization.CultureInfo.InvariantCulture;
        int depth = 0;
        int i = startBracket;

        while (i < json.Length)
        {
            char c = json[i];
            if (c == '[') { depth++; i++; continue; }
            if (c == ']') { depth--; if (depth <= 0) break; i++; continue; }

            if (c == '-' || char.IsDigit(c))
            {
                int end = i;
                while (end < json.Length && (json[end]=='-'||json[end]=='.'||char.IsDigit(json[end]))) end++;
                if (!float.TryParse(json.AsSpan(i, end-i), System.Globalization.NumberStyles.Float, ci, out float e_utm))
                { i++; continue; }
                i = end;
                while (i < json.Length && (json[i]==','||json[i]==' ')) i++;

                end = i;
                while (end < json.Length && (json[end]=='-'||json[end]=='.'||char.IsDigit(json[end]))) end++;
                if (!float.TryParse(json.AsSpan(i, end-i), System.Globalization.NumberStyles.Float, ci, out float n_utm))
                { i++; continue; }
                i = end;

                float ux = (e_utm - E_ORIG) + UNITY_OX;
                float uz = (n_utm - N_ORIG) + UNITY_OZ;
                pts.Add(new Vector2(ux, uz));

                while (i < json.Length && json[i]!='['&&json[i]!=']'&&json[i]!='-'&&!char.IsDigit(json[i])) i++;
            }
            else i++;
        }

        return pts.Count >= 2 ? pts.ToArray() : null;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CARGA LIDAR AGUA Y PUENTES
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator CargarLidarAguaYPuentes()
    {
        string pathAgua    = FullPath(PATH_AGUA);
        string pathPuentes = FullPath(PATH_PUENTES);

        Task<List<LidarAguaPunto>> taskAgua = null;
        Task<List<LidarPuenteDato>> taskPuentes = null;

        if (File.Exists(pathAgua))
            taskAgua = Task.Run(() => {
                var arr = JsonHelper.ParseArray<LidarAguaPunto>(File.ReadAllText(pathAgua));
                return arr != null ? new List<LidarAguaPunto>(arr) : new List<LidarAguaPunto>();
            });

        if (File.Exists(pathPuentes))
            taskPuentes = Task.Run(() => {
                var arr = JsonHelper.ParseArray<LidarPuenteDato>(File.ReadAllText(pathPuentes));
                return arr != null ? new List<LidarPuenteDato>(arr) : new List<LidarPuenteDato>();
            });

        while ((taskAgua != null && !taskAgua.IsCompleted)
            || (taskPuentes != null && !taskPuentes.IsCompleted))
            yield return null;

        // Guardar puntos LIDAR de agua para uso posterior (colocación de láminas pequeñas)
        if (taskAgua?.IsCompletedSuccessfully == true)
        {
            _puntosLidarAgua = taskAgua.Result;
            AlsasuaLogger.Info("Rios",$"LIDAR agua: {_puntosLidarAgua.Count} puntos clase 9 cargados");
        }

        if (taskPuentes?.IsCompletedSuccessfully == true)
        {
            foreach (var p in taskPuentes.Result)
            {
                float ux = (p.x - E_ORIG) + UNITY_OX;  // p.x puede ser UTM E o ya Unity
                float uz = (p.z - N_ORIG) + UNITY_OZ;

                // Si coordenadas parecen ya Unity (rango plausible del mapa)
                if (p.x > 0 && p.x < 5000 && p.z > 0 && p.z < 15000)
                { ux = p.x; uz = p.z; }

                float uy = p.altitud > 0 ? p.altitud - Z_MIN : GeoDataAlsasua.AlturaTerreno(ux, uz);
                _puentes.Add(new PuenteDato
                {
                    posUnity = new Vector3(ux, uy, uz),
                    angulo   = p.angulo,
                    nombre   = p.nombre ?? "Puente",
                });
            }
            AlsasuaLogger.Info("Rios",$"{_puentes.Count} puentes cargados de lidar_puentes.json");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  EXCAVACIÓN DEL TERRAIN
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator ExcavarTerrain()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) yield break;

        var td     = terrain.terrainData;
        int hRes   = td.heightmapResolution;
        float terW = td.size.x;
        float terH = td.size.z;
        float terY = td.size.y;
        Vector3 terPos = terrain.transform.position;

        // Obtener heightmap completo una sola vez
        float[,] heights = td.GetHeights(0, 0, hRes, hRes);
        bool modificado  = false;

        AlsasuaLogger.Info("Rios",$"Excavando {_cauces.Count} cauces en heightmap {hRes}²...");

        // Elevar alturas reales de los puntos del cauce con SampleHeight
        foreach (var cauce in _cauces)
        {
            for (int i = 0; i < cauce.puntosUnity.Length; i++)
            {
                var p = cauce.puntosUnity[i];
                float y = terrain.SampleHeight(new Vector3(p.x, 0, p.z))
                        + terPos.y;
                cauce.puntosUnity[i] = new Vector3(p.x, y, p.z);
            }
        }

        int lote = 0;
        foreach (var cauce in _cauces)
        {
            float  ancho   = cauce.ancho;
            float  prof    = cauce.prof;
            float  radio   = ancho * 0.5f;
            float  radioSq = radio * radio;

            // Para cada segmento del cauce
            for (int seg = 0; seg < cauce.puntosUnity.Length - 1; seg++)
            {
                Vector3 a = cauce.puntosUnity[seg];
                Vector3 b = cauce.puntosUnity[seg + 1];
                float segLen = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                if (segLen < 0.1f) continue;

                // Bounding box del segmento expandido por el radio
                float minX = Mathf.Min(a.x, b.x) - radio - 2f;
                float maxX = Mathf.Max(a.x, b.x) + radio + 2f;
                float minZ = Mathf.Min(a.z, b.z) - radio - 2f;
                float maxZ = Mathf.Max(a.z, b.z) + radio + 2f;

                // Convertir a coordenadas de heightmap
                int hx0 = Mathf.Max(0, Mathf.FloorToInt((minX - terPos.x) / terW * (hRes-1)));
                int hx1 = Mathf.Min(hRes-1, Mathf.CeilToInt((maxX - terPos.x) / terW * (hRes-1)));
                int hz0 = Mathf.Max(0, Mathf.FloorToInt((minZ - terPos.z) / terH * (hRes-1)));
                int hz1 = Mathf.Min(hRes-1, Mathf.CeilToInt((maxZ - terPos.z) / terH * (hRes-1)));

                // Dirección y normal del segmento
                Vector2 dir = new Vector2(b.x - a.x, b.z - a.z).normalized;
                Vector2 nor = new Vector2(-dir.y, dir.x); // perpendicular

                float altBase = Mathf.Min(a.y, b.y); // altura mínima del segmento

                for (int hz = hz0; hz <= hz1; hz++)
                for (int hx = hx0; hx <= hx1; hx++)
                {
                    float wx = terPos.x + (float)hx / (hRes-1) * terW;
                    float wz = terPos.z + (float)hz / (hRes-1) * terH;

                    // Distancia firmada desde el eje del cauce
                    Vector2 pLocal = new Vector2(wx - a.x, wz - a.z);
                    float   tSeg   = Mathf.Clamp01(Vector2.Dot(pLocal, dir) / segLen);
                    Vector2 closest = new Vector2(a.x + dir.x*tSeg*segLen, a.z + dir.y*tSeg*segLen);
                    float   dist    = Vector2.Distance(new Vector2(wx, wz), closest);

                    if (dist > radio + 2f) continue;

                    // Perfil transversal V-asimétrico
                    // d < radio*0.5 → fondo plano suave
                    // radio*0.5 < d < radio → talud
                    // radio < d < radio+2 → suavizado exterior
                    float depthFactor = PerfilVAsimetrico(dist, radio, nor, pLocal);
                    if (depthFactor <= 0f) continue;

                    float excavacion = prof * depthFactor / terY;
                    float altOrig    = heights[hz, hx];
                    float altObjeto  = (altBase - terPos.y) / terY - excavacion;
                    float altNueva   = Mathf.Min(altOrig, altObjeto);

                    if (altNueva < altOrig - 0.0001f)
                    {
                        heights[hz, hx] = Mathf.Clamp01(altNueva);
                        modificado = true;
                    }
                }
            }

            if (++lote % 3 == 0) yield return null;
        }

        if (modificado)
        {
            td.SetHeights(0, 0, heights);
            terrain.Flush();
            AlsasuaLogger.Info("Rios","✅ Terrain excavado con perfiles de cauce");
        }
    }

    /// Perfil V-asimétrico: 1.0 en el centro, 0 en el borde externo + suavizado
    static float PerfilVAsimetrico(float dist, float radio, Vector2 nor, Vector2 pLocal)
    {
        if (dist > radio + 1.5f) return 0f;
        if (dist > radio) return Mathf.Lerp(0.3f, 0f, (dist - radio) / 1.5f);

        // Fondo plano en el núcleo (30% del radio)
        float fondoR = radio * 0.3f;
        if (dist <= fondoR) return 1f;

        // Talud suave desde fondoR → radio, con leve asimetría (izq vs der) simulando erosión
        float t = (dist - fondoR) / (radio - fondoR);
        // Asimetría: talud izquierdo (nor negativo) algo más suave
        float asim = Vector2.Dot(pLocal.normalized, nor) > 0 ? 1.0f : 0.85f;
        return Mathf.Lerp(1f, 0.2f, t * t) * asim;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  PLANOS DE AGUA HDRP
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator CrearPlanosAgua()
    {
        var terrain = Terrain.activeTerrain;

        // Detectar shader HDRP de agua
        Shader shAgua = shaderAgua
                     ?? Shader.Find("HDRP/Water")
                     ?? Shader.Find("HDRP/Lit")
                     ?? Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");

        foreach (var cauce in _cauces)
        {
            if (cauce.puntosUnity.Length < 2) continue;

            // Crear segmentos de plano de agua entre cada par de vértices
            for (int seg = 0; seg < cauce.puntosUnity.Length - 1; seg++)
            {
                Vector3 a = cauce.puntosUnity[seg];
                Vector3 b = cauce.puntosUnity[seg + 1];

                float   segLen = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                if (segLen < 0.5f) continue;

                // Centro del segmento
                Vector3 centro = (a + b) * 0.5f;
                // Altura del agua = fondo excavado + offsetAguaY
                float yFondo = terrain != null
                    ? terrain.SampleHeight(centro) + terrain.transform.position.y
                    : centro.y;
                centro.y = yFondo + offsetAguaY;

                // Orientación: apunta de a→b
                Vector3 dir = (b - a).normalized;
                dir.y = 0f;
                if (dir == Vector3.zero) continue;
                Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);

                // Plano de agua
                var go  = new GameObject($"Agua_{cauce.nombre}_seg{seg}");
                go.transform.SetParent(_parentAgua, false);
                go.transform.SetPositionAndRotation(centro, rot);
                go.isStatic = true;

                var mf  = go.AddComponent<MeshFilter>();
                mf.sharedMesh = CrearPlanoAgua(segLen, cauce.ancho * 0.9f);

                var mr  = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = CrearMaterialAgua(shAgua, cauce, seg);
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows    = true;

                cauce.planosAgua.Add(go);
            }

            yield return null;
        }

        AlsasuaLogger.Info("Rios",
            $"✅ Planos agua creados ({_cauces.Sum(c => c.planosAgua.Count)} segmentos)");
    }

    static Mesh CrearPlanoAgua(float largo, float ancho)
    {
        float hl = largo * 0.5f, ha = ancho * 0.5f;
        var m = new Mesh { name = "AguaPlano" };
        m.SetVertices(new[]
        {
            new Vector3(-ha, 0, -hl), new Vector3( ha, 0, -hl),
            new Vector3( ha, 0,  hl), new Vector3(-ha, 0,  hl),
        });
        m.SetTriangles(new[] { 0,2,1, 0,3,2 }, 0);
        m.SetUVs(0, new[]
        {
            new Vector2(0,0), new Vector2(1,0),
            new Vector2(1,1), new Vector2(0,1),
        });
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    Material CrearMaterialAgua(Shader sh, RioCauce cauce, int seg)
    {
        var mat = new Material(sh);
        mat.name = $"Mat_Agua_{cauce.nombre}";

        // Calcular pendiente del segmento para flow map y foam
        float pendiente = 0f;
        if (seg < cauce.puntosUnity.Length - 1)
        {
            Vector3 a = cauce.puntosUnity[seg], b = cauce.puntosUnity[seg + 1];
            float dH  = Mathf.Abs(b.y - a.y);
            float dXZ = Vector2.Distance(new Vector2(a.x,a.z), new Vector2(b.x,b.z));
            pendiente = dXZ > 0.1f ? dH / dXZ : 0f;
        }

        float foam = cauce.esArakil
            ? Mathf.Clamp01(pendiente / 0.03f) * foamIntensity
            : Mathf.Clamp01(pendiente / 0.05f) * foamIntensity * 0.5f;

        // HDRP Water surface
        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", COLOR_AGUA);
        }
        if (mat.HasProperty("_Color"))
        {
            mat.SetColor("_Color", COLOR_AGUA);
        }

        // HDRP Surface Type — transparente
        if (mat.HasProperty("_SurfaceType"))
            mat.SetFloat("_SurfaceType", 1f);   // 1 = Transparent
        if (mat.HasProperty("_BlendMode"))
            mat.SetFloat("_BlendMode", 0f);     // Alpha

        // Smoothness alta para SSR; metallic 0 (el agua no es metálica)
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", 0.95f);
        if (mat.HasProperty("_Metallic"))
            mat.SetFloat("_Metallic", 0.0f);

        // Emisión muy suave azul-verde (#0a1a0f @ 0.1) — simula scattering subsuperficial
        var emitCol = new Color(0x0a/255f, 0x1a/255f, 0x0f/255f) * 0.1f;
        if (mat.HasProperty("_EmissiveColor"))
        {
            mat.SetColor("_EmissiveColor", emitCol);
            mat.EnableKeyword("_EMISSION");
        }
        else if (mat.HasProperty("_EmissionColor"))
        {
            mat.SetColor("_EmissionColor", emitCol);
            mat.EnableKeyword("_EMISSION");
        }

        // Flow map via UV scroll
        if (mat.HasProperty("_BaseColorMap"))
        {
            // Crear flow map procedural 4×4
            var flowTex = CrearFlowMap(pendiente);
            mat.SetTexture("_BaseColorMap", flowTex);
        }

        // Foam (HDRP Water o Lit con keyword)
        if (foam > 0.05f)
        {
            mat.EnableKeyword("_FOAM");
            if (mat.HasProperty("_FoamIntensity"))
                mat.SetFloat("_FoamIntensity", foam);
        }

        // Caustics
        if (mat.HasProperty("_CausticsIntensity"))
            mat.SetFloat("_CausticsIntensity", 0.5f);

        // SSR hint (en HDRP Lit se activa con RefractionModel)
        if (mat.HasProperty("_RefractionModel"))
            mat.SetFloat("_RefractionModel", 1f);

        mat.renderQueue = (int)RenderQueue.Transparent;
        return mat;
    }

    static Texture2D CrearFlowMap(float pendiente)
    {
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode   = TextureWrapMode.Repeat;
        // Codificar dirección de flujo en RG (convenio flowmap: R=X, G=Y, B=0.5, A=1)
        float speed = Mathf.Clamp01(pendiente * 20f);
        var col = new Color(0.5f + speed * 0.4f, 0.5f, 0.5f, 1f);
        for (int i = 0; i < 16; i++) tex.SetPixel(i%4, i/4, col);
        tex.Apply();
        return tex;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  LÁMINAS DE AGUA LIDAR (clase 9) — charcos, acequias, balsas
    // ════════════════════════════════════════════════════════════════════════

    /// Agrupa los puntos LIDAR de agua (clase 9) en clusters por proximidad
    /// y coloca una pequeña lámina de agua quad en el centroide de cada cluster.
    IEnumerator ColocarAguaLidar()
    {
        if (_puntosLidarAgua.Count == 0) yield break;

        var terrain = Terrain.activeTerrain;

        Shader shAgua = shaderAgua
                     ?? Shader.Find("HDRP/Lit")
                     ?? Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");

        // Convertir coordenadas a Unity. Los puntos en lidar_agua.json pueden estar
        // en UTM absoluto (E~567xxx) o ya en Unity space — detectar por magnitud.
        var ptsUnity = new List<Vector3>(_puntosLidarAgua.Count);
        foreach (var p in _puntosLidarAgua)
        {
            float ux, uz;
            if (p.x > 100000f) // UTM absoluto
            {
                ux = (p.x - E_ORIG) + UNITY_OX;
                uz = (p.z - N_ORIG) + UNITY_OZ;
            }
            else // ya en Unity space
            {
                ux = p.x;
                uz = p.z;
            }
            float uy = p.altitud > Z_MIN ? p.altitud - Z_MIN
                     : (terrain != null ? terrain.SampleHeight(new Vector3(ux, 0, uz)) : 0f);
            ptsUnity.Add(new Vector3(ux, uy, uz));
        }

        // Clustering simple: radio 5m — agrupar puntos cercanos en un cluster
        const float CLUSTER_RADIO = 5f;
        const float CLUSTER_RADIO_SQ = CLUSTER_RADIO * CLUSTER_RADIO;
        var usados = new bool[ptsUnity.Count];
        var clusters = new List<(Vector3 centroide, float radio)>();

        for (int i = 0; i < ptsUnity.Count; i++)
        {
            if (usados[i]) continue;
            var centro = ptsUnity[i];
            int count = 1;
            float minX = centro.x, maxX = centro.x;
            float minZ = centro.z, maxZ = centro.z;
            float sumY = centro.y;

            for (int j = i + 1; j < ptsUnity.Count; j++)
            {
                if (usados[j]) continue;
                float dx = ptsUnity[j].x - centro.x;
                float dz = ptsUnity[j].z - centro.z;
                if (dx*dx + dz*dz < CLUSTER_RADIO_SQ)
                {
                    usados[j] = true;
                    count++;
                    sumY += ptsUnity[j].y;
                    if (ptsUnity[j].x < minX) minX = ptsUnity[j].x;
                    if (ptsUnity[j].x > maxX) maxX = ptsUnity[j].x;
                    if (ptsUnity[j].z < minZ) minZ = ptsUnity[j].z;
                    if (ptsUnity[j].z > maxZ) maxZ = ptsUnity[j].z;
                }
            }

            float cx = (minX + maxX) * 0.5f;
            float cz = (minZ + maxZ) * 0.5f;
            float cy  = sumY / count + offsetAguaY;
            float radioCluster = Mathf.Max(1f, Mathf.Max(maxX - minX, maxZ - minZ) * 0.5f + 0.5f);
            clusters.Add((new Vector3(cx, cy, cz), radioCluster));
        }

        // Filtrar clusters que ya están dentro de un cauce GeoJSON (evitar solapamiento)
        int colocados = 0;
        foreach (var (centroide, radio) in clusters)
        {
            if (EsZonaAgua(centroide.x, centroide.z)) continue; // ya cubierto por cauce GeoJSON

            var go = new GameObject($"AguaLidar_{colocados}");
            go.transform.SetParent(_parentAgua, false);
            go.transform.position = centroide;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = CrearPlanoAgua(radio * 2f, radio * 2f);

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CrearMaterialAguaLidar(shAgua);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = true;

            go.isStatic = true;
            colocados++;

            if (colocados % 20 == 0) yield return null;
        }

        AlsasuaLogger.Info("Rios",
            $"✅ LIDAR agua: {colocados} láminas de agua pequeñas colocadas ({clusters.Count} clusters de {_puntosLidarAgua.Count} puntos)");
    }

    /// Crea un material de agua mejorado para láminas LIDAR (charcos/balsas pequeñas).
    Material CrearMaterialAguaLidar(Shader sh)
    {
        var mat = new Material(sh) { name = "Mat_AguaLidar" };

        // Color base: azul verdoso oscuro, semi-transparente
        var colorAgua = new Color(0x1a/255f, 0x35/255f, 0x20/255f, 0.78f);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colorAgua);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     colorAgua);

        // Superficie transparente
        if (mat.HasProperty("_SurfaceType")) mat.SetFloat("_SurfaceType", 1f);
        if (mat.HasProperty("_BlendMode"))   mat.SetFloat("_BlendMode",   0f);

        // Alta reflectividad, no metálico
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.95f);
        if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic",   0.0f);

        // Emisión muy suave azul-verde (#0a1a0f) a intensidad 0.1 — simula scattering subsuperficial
        if (mat.HasProperty("_EmissiveColor"))
        {
            var emitColor = new Color(0x0a/255f, 0x1a/255f, 0x0f/255f) * 0.1f;
            mat.SetColor("_EmissiveColor", emitColor);
            mat.EnableKeyword("_EMISSION");
        }
        else if (mat.HasProperty("_EmissionColor"))
        {
            var emitColor = new Color(0x0a/255f, 0x1a/255f, 0x0f/255f) * 0.1f;
            mat.SetColor("_EmissionColor", emitColor);
            mat.EnableKeyword("_EMISSION");
        }

        if (mat.HasProperty("_RefractionModel")) mat.SetFloat("_RefractionModel", 1f);
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.enableInstancing = true;
        return mat;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  COLOCAR PUENTES
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator ColocarPuentes()
    {
        // Si no hay datos LIDAR de puentes, detectar cruces de polilíneas (futuro)
        if (_puentes.Count == 0)
        {
            AlsasuaLogger.Info("Rios","Sin datos de puentes en lidar_puentes.json");
            yield break;
        }

        // Cargar prefab si no está asignado
        if (prefabPuente == null)
        {
#if UNITY_EDITOR
            string assetPath = PATH_PUENTE_FBX;
            prefabPuente = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefabPuente == null)
                AlsasuaLogger.Warn("Rios",$"bridge_roads.fbx no encontrado en {assetPath}");
#endif
        }

        foreach (var puente in _puentes)
        {
            // Alinear al cauce más cercano
            float angulo = puente.angulo;
            if (Mathf.Approximately(angulo, 0f))
            {
                // Calcular ángulo desde tangente del cauce más cercano
                angulo = AnguloPuenteDesdeRio(puente.posUnity);
            }

            Quaternion rot = Quaternion.Euler(0f, angulo, 0f);

            GameObject go;
            if (prefabPuente != null)
            {
                go = Instantiate(prefabPuente, puente.posUnity, rot, _parentPuentes);
                go.transform.localScale = escalaPuente;
            }
            else
            {
                // Placeholder: cubo blanco si no hay FBX
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetPositionAndRotation(puente.posUnity, rot);
                go.transform.localScale = new Vector3(escalaPuente.x * 8f, 1.5f, escalaPuente.z * 2f);
                go.transform.SetParent(_parentPuentes, true);
                AlsasuaLogger.Warn("Rios",$"Puente '{puente.nombre}' usando placeholder");
            }

            go.name = $"Puente_{puente.nombre}";
            go.isStatic = true;
            yield return null;
        }

        AlsasuaLogger.Info("Rios",$"✅ {_puentes.Count} puentes colocados");
    }

    float AnguloPuenteDesdeRio(Vector3 pos)
    {
        float minDist = float.MaxValue;
        Vector3 tangente = Vector3.forward;

        foreach (var cauce in _cauces)
        {
            for (int i = 0; i < cauce.puntosUnity.Length - 1; i++)
            {
                Vector3 a = cauce.puntosUnity[i], b = cauce.puntosUnity[i+1];
                Vector2 closest = PuntoMasCercanoEnSegmento(
                    new Vector2(pos.x, pos.z),
                    new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                float d = Vector2.Distance(new Vector2(pos.x, pos.z), closest);
                if (d < minDist)
                {
                    minDist  = d;
                    tangente = (b - a).normalized;
                }
            }
        }

        // El puente es perpendicular al río
        Vector3 perp = Quaternion.Euler(0, 90, 0) * tangente;
        return Mathf.Atan2(perp.x, perp.z) * Mathf.Rad2Deg;
    }

    static Vector2 PuntoMasCercanoEnSegmento(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 0.0001f) return a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        return a + ab * t;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  VALIDACIÓN EDITOR
    // ════════════════════════════════════════════════════════════════════════

    [ContextMenu("Regenerar Ríos y Puentes")]
    public void RegenerarDesdeEditor()
    {
        // Limpiar instancias anteriores
        foreach (Transform ch in _parentAgua)  Destroy(ch.gameObject);
        foreach (Transform ch in _parentPuentes) Destroy(ch.gameObject);
        _cauces.Clear(); _puentes.Clear();
        foreach (var c in _cauces) c.planosAgua.Clear();
        StartCoroutine(Start());
    }

    // ════════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>¿Es la posición Unity (x,z) zona de agua (cauce)?</summary>
    public bool EsZonaAgua(float ux, float uz)
    {
        foreach (var cauce in _cauces)
        {
            float radio = cauce.ancho * 0.5f;
            for (int i = 0; i < cauce.puntosUnity.Length - 1; i++)
            {
                Vector3 a = cauce.puntosUnity[i], b = cauce.puntosUnity[i+1];
                Vector2 c2 = PuntoMasCercanoEnSegmento(
                    new Vector2(ux, uz),
                    new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                if (Vector2.Distance(new Vector2(ux, uz), c2) < radio)
                    return true;
            }
        }
        return false;
    }

    /// <summary>Distancia al cauce más cercano (m), -1 si sin datos.</summary>
    public float DistanciaAlRio(float ux, float uz)
    {
        float min = float.MaxValue;
        foreach (var cauce in _cauces)
            for (int i = 0; i < cauce.puntosUnity.Length - 1; i++)
            {
                Vector3 a = cauce.puntosUnity[i], b = cauce.puntosUnity[i+1];
                Vector2 c2 = PuntoMasCercanoEnSegmento(
                    new Vector2(ux, uz),
                    new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                float d = Vector2.Distance(new Vector2(ux, uz), c2);
                if (d < min) min = d;
            }
        return min < float.MaxValue ? min : -1f;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════════════

    static string FullPath(string rel)
        => Path.Combine(Application.dataPath.Replace("Assets", ""), rel);

    static IEnumerator EsperarTask(Task task, string nombre)
    {
        while (!task.IsCompleted) yield return null;
        if (task.IsFaulted)
        {
            var ex = task.Exception?.InnerException ?? task.Exception;
            AlsasuaLogger.Error("Rios", $"Task '{nombre}': {ex?.GetType().Name} — {ex?.Message}");
        }
    }

    static IEnumerator EsperarTask<T>(Task<T> task, string nombre)
    {
        while (!task.IsCompleted) yield return null;
        if (task.IsFaulted)
        {
            var ex = task.Exception?.InnerException ?? task.Exception;
            AlsasuaLogger.Error("Rios", $"Task '{nombre}': {ex?.GetType().Name} — {ex?.Message}");
        }
    }
}
