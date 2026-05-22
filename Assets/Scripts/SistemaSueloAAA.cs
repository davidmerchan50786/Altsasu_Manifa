// Assets/Scripts/SistemaSueloAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE SUELO AAA — terreno + calles + aceras + todo perfecto
//
//  Aplica en runtime:
//    • Terrain layers PBR reales (hierba, roca, tierra, barro) por pendiente
//    • Calles mejoradas: asfalto PBR + marcas viales + bordillos + aceras
//    • Mobiliario urbano: bancos, papeleras, farolas, señales a lo largo calles
//    • Vegetación con los 12 árboles nativos de Navarra en FBX real
//    • Animales: caballos, ovejas, conejos, ciervos en zonas naturales
//    • Vehículos NPC parqueados y circulando
//    • Props decorativos de Poly Haven
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

[DefaultExecutionOrder(-65)] // después de SistemaTerreno (-70) para leer alphamap ya pintado
public class SistemaSueloAAA : MonoBehaviour
{
    public static SistemaSueloAAA Instance { get; private set; }

    // ── Texturas de suelo disponibles (Poly Haven) ────────────────────────
    static readonly string[] HIERBA  = { "aerial_grass_rock", "forest_ground_01", "brown_mud_leaves_01" };
    static readonly string[] ROCA    = { "aerial_rocks_01", "aerial_rocks_02", "aerial_ground_rock", "rock_ground_02" };
    static readonly string[] TIERRA  = { "brown_mud", "brown_mud_02", "aerial_mud_1", "dirt_road_01" };
    static readonly string[] ASFALTO = { "asphalt_01", "asphalt_02", "clean_asphalt", "aerial_asphalt_01" };
    static readonly string[] ACERA   = { "cobblestone_01", "cobblestone_pavement", "brick_pavement", "cobblestone_floor_001" };
    static readonly string[] PIEDRA  = { "cobblestone_large_01", "cobblestone_embedded_asphalt", "cobblestone_floor_02" };

    // ── Offsets OSM → Unity ───────────────────────────────────────────────
    const float OX = 1918f, OZ = 8570f;

    // ── Estado ────────────────────────────────────────────────────────────
    Material _matAsfalto, _matAcera, _matBordillo, _matMarcaVial;
    Material _matHierba, _matRoca, _matTierra;
    List<GameObject> _prefabsArboles   = new();
    List<GameObject> _prefabsAnimales  = new();
    List<GameObject> _prefabsProps     = new();
    List<GameObject> _prefabsVehiculos = new();

    Transform _parentCalles, _parentVegetacion, _parentProps, _parentAnimales;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        GeneradorMundoOSM.OnMundoGenerado += OnMundoListo;
    }

    void OnMundoListo()
    {
        StartCoroutine(ConstruirSuelo());
    }

    IEnumerator ConstruirSuelo()
    {
        yield return new WaitForSeconds(1.5f); // esperar edificios

        CargarMateriales();
        CargarPrefabs();
        yield return null;

        CrearParents();

        // Aplicar terreno
        yield return StartCoroutine(AplicarTerrain());
        yield return null;

        // Mejorar calles
        yield return StartCoroutine(MejorarCalles());
        yield return null;

        // Plantar vegetación
        yield return StartCoroutine(PlantarVegetacion());
        yield return null;

        // Colocar props
        yield return StartCoroutine(ColocarMobiliario());
        yield return null;

        // Animales
        yield return StartCoroutine(SoltarAnimales());
        yield return null;

        // Vehículos parqueados
        yield return StartCoroutine(VehiculosParqueados());

        AlsasuaLogger.Info("SueloAAA", "✅ Suelo, vegetación, props y animales completados");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  A. TERRAIN — layers PBR por pendiente y altura
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator AplicarTerrain()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) yield break;

        var td = terrain.terrainData;

        // Crear TerrainLayers con materiales PBR reales
        var layers = new List<TerrainLayer>();
        layers.Add(CrearTerrainLayer(HIERBA[0],  4f,  4f));  // base: hierba
        layers.Add(CrearTerrainLayer(ROCA[0],    8f,  8f));  // pendientes: roca
        layers.Add(CrearTerrainLayer(TIERRA[0],  3f,  3f));  // caminos: tierra
        layers.Add(CrearTerrainLayer(HIERBA[1],  5f,  5f));  // variación hierba

        td.terrainLayers = layers.ToArray();
        yield return null;

        // Pintar automáticamente por pendiente + fBm multifrecuencia
        int res   = td.alphamapResolution;

        // fBm: 6 octavas, variación a múltiples escalas (mejor que Perlin 1 octava)
        var alphaFBM = IntegradorMatematicas.GenerarAlphamapFBM(
            terrain, layers.Count,
            frecuenciaBase: 0.0018f, octavas: 6, persistencia: 0.45f, lacunaridad: 2.1f);

        var alpha = alphaFBM ?? new float[res, res, layers.Count];
        if (alphaFBM != null)
        {
            td.SetAlphamaps(0, 0, alpha);
            yield break; // fBm calculó el alphamap completo
        }

        // Fallback si fBm falló: Perlin de una octava
        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float nx = (float)x / res, nz = (float)z / res;
                float slope = td.GetSteepness(nx, nz);
                float height = td.GetInterpolatedHeight(nx, nz) / td.size.y;
                float noise  = Mathf.PerlinNoise(x * 0.05f, z * 0.05f);

                // Hierba en zonas planas bajas
                float wHierba = Mathf.Clamp01(1f - slope / 25f) * (1f - height * 0.3f);
                // Roca en pendientes y cumbres
                float wRoca   = Mathf.Clamp01((slope - 20f) / 20f) + height * 0.4f;
                // Tierra variación con noise
                float wTierra = noise * 0.2f * (1f - wRoca);
                // Hierba 2
                float wHierba2 = (1f - noise) * 0.15f * (1f - wRoca);

                float total = wHierba + wRoca + wTierra + wHierba2;
                if (total < 0.001f) total = 1f;

                alpha[z, x, 0] = wHierba  / total;
                alpha[z, x, 1] = wRoca    / total;
                alpha[z, x, 2] = wTierra  / total;
                alpha[z, x, 3] = wHierba2 / total;
            }
            if (z % 50 == 0) yield return null;
        }

        td.SetAlphamaps(0, 0, alpha);
        terrain.Flush();
        AlsasuaLogger.Info("SueloAAA", "Terrain layers PBR aplicados");
    }

    TerrainLayer CrearTerrainLayer(string texName, float tileSizeX, float tileSizeZ)
    {
        var layer = new TerrainLayer { tileSize = new Vector2(tileSizeX, tileSizeZ) };

#if UNITY_EDITOR
        // Cargar desde Assets/Materials/Suelo/
        string matPath = $"Assets/Materials/Suelo/{texName}.mat";
        var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat != null)
        {
            if (mat.HasProperty("_BaseColorMap")) layer.diffuseTexture = mat.GetTexture("_BaseColorMap") as Texture2D;
            if (mat.HasProperty("_NormalMap"))    layer.normalMapTexture = mat.GetTexture("_NormalMap") as Texture2D;
        }
#endif
        if (layer.diffuseTexture == null)
            layer.diffuseTexture = Texture2D.grayTexture; // fallback

        return layer;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  B. CALLES — asfalto PBR + marcas + bordillos + aceras
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator MejorarCalles()
    {
        // Aplicar materiales PBR a las calles ya generadas
        var callesParent = GameObject.Find("Calles_Precisas") ?? GameObject.Find("Calles_OSM");
        if (callesParent == null) yield break;

        int n = 0;
        foreach (Transform calle in callesParent.transform)
        {
            if (calle == null) continue;
            var mr = calle.GetComponent<MeshRenderer>();
            var mf = calle.GetComponent<MeshFilter>();
            if (mr == null || mf == null) continue;

            string nombre = calle.name.ToLower();
            Material mat;
            if (nombre.Contains("pedestrian") || nombre.Contains("footway") || nombre.Contains("path"))
                mat = _matAcera;
            else
                mat = _matAsfalto;

            mr.sharedMaterial = mat;

            // Añadir bordillo lateral y acera
            var mesh = mf.sharedMesh;
            if (mesh != null)
            {
                AnadirBordillo(calle, mesh);
                AnadirAcera(calle, mesh);
                AnadirMarcasViales(calle, mesh);
            }

            n++;
            if (n % 30 == 0) yield return null;
        }
        AlsasuaLogger.Info("SueloAAA", $"Calles mejoradas: {n}");
    }

    void AnadirBordillo(Transform calle, Mesh mesh)
    {
        var bounds = mesh.bounds;
        float w = bounds.size.x;
        float d = bounds.size.z;
        float y = bounds.min.y;

        // Bordillo izquierdo
        CrearPrimitivo("Bordillo_L", calle,
            new Vector3(bounds.min.x - 0.07f, y + 0.08f, bounds.center.z),
            new Vector3(0.14f, 0.16f, d), _matBordillo);
        // Bordillo derecho
        CrearPrimitivo("Bordillo_R", calle,
            new Vector3(bounds.max.x + 0.07f, y + 0.08f, bounds.center.z),
            new Vector3(0.14f, 0.16f, d), _matBordillo);
    }

    void AnadirAcera(Transform calle, Mesh mesh)
    {
        var b = mesh.bounds;
        float y = b.min.y + 0.12f;
        float anchoAcera = 1.4f;

        CrearPrimitivo("Acera_L", calle,
            new Vector3(b.min.x - 0.14f - anchoAcera * 0.5f, y, b.center.z),
            new Vector3(anchoAcera, 0.06f, b.size.z), _matAcera);
        CrearPrimitivo("Acera_R", calle,
            new Vector3(b.max.x + 0.14f + anchoAcera * 0.5f, y, b.center.z),
            new Vector3(anchoAcera, 0.06f, b.size.z), _matAcera);
    }

    void AnadirMarcasViales(Transform calle, Mesh mesh)
    {
        // Línea central discontinua en calles principales
        var b = mesh.bounds;
        if (b.size.z < 12f) return; // solo calles largas

        float z = b.min.z;
        int nTrazos = Mathf.FloorToInt(b.size.z / 4f);
        for (int i = 0; i < nTrazos; i++)
        {
            if (i % 2 == 0) // discontinua
                CrearPrimitivo($"Marca_{i}", calle,
                    new Vector3(b.center.x, b.min.y + 0.005f, z + i * 4f + 1.2f),
                    new Vector3(0.12f, 0.01f, 2.4f), _matMarcaVial);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  C. VEGETACIÓN — 12 árboles nativos de Navarra
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator PlantarVegetacion()
    {
        if (_prefabsArboles.Count == 0) yield break;

        var terrain = Terrain.activeTerrain;

        // Zonas de vegetación densa (fuera del pueblo)
        var zonasVeg = new[]
        {
            new Vector3(OX - 600f, 0, OZ + 400f),  // norte
            new Vector3(OX + 700f, 0, OZ + 500f),  // noreste
            new Vector3(OX - 500f, 0, OZ - 600f),  // sur
            new Vector3(OX + 800f, 0, OZ - 400f),  // sureste
        };

        int total = 0;
        uint semillaBase = (uint)(System.DateTime.Now.Millisecond + 1);

        foreach (var zona in zonasVeg)
        {
            // Poisson Disk Sampling: distribución natural, sin clumping artificial
            // Radio 300m, separación mínima 12m entre árboles, máx 80 por zona
            var posicionesPoisson = IntegradorMatematicas.GenerarPosicionesArbolesPoisson(
                zona, radio: 300f, separacionMin: 12f, max: 80, semilla: semillaBase++);

            foreach (var posPois in posicionesPoisson)
            {
                float rx = posPois.x, rz = posPois.z;

                // No plantar dentro del pueblo
                float distPueblo = Vector2.Distance(new Vector2(rx, rz), new Vector2(OX, OZ));
                if (distPueblo < 250f) continue;

                float y = terrain != null ? terrain.SampleHeight(new Vector3(rx, 0, rz)) : 240f;
                float pendiente = terrain != null ? terrain.terrainData.GetSteepness(
                    Mathf.Clamp01((rx - terrain.transform.position.x) / terrain.terrainData.size.x),
                    Mathf.Clamp01((rz - terrain.transform.position.z) / terrain.terrainData.size.z)) : 0f;

                if (pendiente > 45f) continue; // no en pendientes extremas

                var prefab = _prefabsArboles[Random.Range(0, _prefabsArboles.Count)];
                if (prefab == null) continue;

                var arbol = Instantiate(prefab, new Vector3(rx, y, rz),
                    Quaternion.Euler(0, Random.Range(0f, 360f), 0), _parentVegetacion);
                float escala = Random.Range(0.7f, 1.4f);
                arbol.transform.localScale = Vector3.one * escala;
                arbol.isStatic = true;
                total++;

                if (total % 20 == 0) yield return null;
            }
        }

        // Árboles en las calles del pueblo (alineados)
        var callesParent = GameObject.Find("Calles_Precisas") ?? GameObject.Find("Calles_OSM");
        if (callesParent != null)
        {
            int callesConArboles = 0;
            foreach (Transform calle in callesParent.transform)
            {
                if (calle == null) continue;
                var mf = calle.GetComponent<MeshFilter>();
                if (mf == null) continue;
                var b = mf.sharedMesh.bounds;
                if (b.size.z < 20f || callesConArboles > 30) continue;

                // Árbol cada 15m a lo largo de la calle
                for (float z = b.min.z + 7f; z < b.max.z - 7f; z += 15f)
                {
                    if (Random.value < 0.6f)
                    {
                        float y2 = terrain != null ? terrain.SampleHeight(new Vector3(b.min.x - 1.5f, 0, z)) : 240f;
                        var prefab = _prefabsArboles[Random.Range(0, _prefabsArboles.Count)];
                        if (prefab == null) continue;
                        var arbol = Instantiate(prefab, new Vector3(b.min.x - 1.5f, y2, z),
                            Quaternion.Euler(0, Random.Range(0f, 360f), 0), _parentVegetacion);
                        arbol.transform.localScale = Vector3.one * 0.6f;
                        arbol.isStatic = true;
                        total++;
                    }
                }
                callesConArboles++;
                yield return null;
            }
        }

        AlsasuaLogger.Info("SueloAAA", $"Vegetación plantada: {total} árboles");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  D. MOBILIARIO URBANO
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator ColocarMobiliario()
    {
        if (_prefabsProps.Count == 0) yield break;

        var callesParent = GameObject.Find("Calles_Precisas") ?? GameObject.Find("Calles_OSM");
        if (callesParent == null) yield break;

        var terrain = Terrain.activeTerrain;
        int total = 0;

        foreach (Transform calle in callesParent.transform)
        {
            if (calle == null) continue;
            var mf = calle.GetComponent<MeshFilter>();
            if (mf == null) continue;
            var b = mf.sharedMesh.bounds;
            if (b.size.z < 10f) continue;

            // Colocar prop en la acera cada ~20m
            for (float z = b.min.z + 8f; z < b.max.z - 8f; z += Random.Range(15f, 30f))
            {
                if (Random.value < 0.35f)
                {
                    float x = b.min.x - 2.0f;
                    float y = terrain != null ? terrain.SampleHeight(new Vector3(x, 0, z)) : 240f;
                    var prefab = _prefabsProps[Random.Range(0, _prefabsProps.Count)];
                    if (prefab == null) continue;

                    var prop = Instantiate(prefab, new Vector3(x, y, z),
                        Quaternion.Euler(0, Random.Range(0f, 360f), 0), _parentProps);
                    prop.isStatic = true;
                    total++;
                }
            }

            if (total % 30 == 0) yield return null;
        }

        AlsasuaLogger.Info("SueloAAA", $"Mobiliario colocado: {total} props");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  E. ANIMALES
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator SoltarAnimales()
    {
        if (_prefabsAnimales.Count == 0) yield break;

        var terrain = Terrain.activeTerrain;
        var zonas   = new[]
        {
            new Vector3(OX - 800f, 0, OZ + 600f),
            new Vector3(OX + 900f, 0, OZ + 700f),
            new Vector3(OX,        0, OZ + 900f),
        };

        int total = 0;
        foreach (var zona in zonas)
        {
            int nAnimales = Random.Range(3, 8);
            for (int i = 0; i < nAnimales; i++)
            {
                float rx = zona.x + Random.Range(-150f, 150f);
                float rz = zona.z + Random.Range(-150f, 150f);
                float y  = terrain != null ? terrain.SampleHeight(new Vector3(rx, 0, rz)) : 240f;

                var prefab = _prefabsAnimales[Random.Range(0, _prefabsAnimales.Count)];
                if (prefab == null) continue;

                var animal = Instantiate(prefab, new Vector3(rx, y, rz),
                    Quaternion.Euler(0, Random.Range(0f, 360f), 0), _parentAnimales);
                animal.transform.localScale = Vector3.one * Random.Range(0.8f, 1.1f);
                total++;
            }
            yield return null;
        }

        AlsasuaLogger.Info("SueloAAA", $"Animales soltados: {total}");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  F. VEHÍCULOS PARQUEADOS
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator VehiculosParqueados()
    {
        if (_prefabsVehiculos.Count == 0) yield break;

        var callesParent = GameObject.Find("Calles_Precisas") ?? GameObject.Find("Calles_OSM");
        if (callesParent == null) yield break;

        var terrain = Terrain.activeTerrain;
        int total   = 0;

        foreach (Transform calle in callesParent.transform)
        {
            if (Random.value > 0.25f) continue; // 25% de calles tienen coches aparcados
            var mf = calle.GetComponent<MeshFilter>();
            if (mf == null) continue;
            var b = mf.sharedMesh.bounds;
            if (b.size.z < 15f) continue;

            float z = b.min.z + Random.Range(5f, b.size.z - 5f);
            float x = b.min.x - 2.5f;
            float y = terrain != null ? terrain.SampleHeight(new Vector3(x, 0, z)) : 240f;

            var prefab = _prefabsVehiculos[Random.Range(0, _prefabsVehiculos.Count)];
            if (prefab == null) continue;

            var veh = Instantiate(prefab, new Vector3(x, y, z),
                Quaternion.Euler(0, Random.Range(-15f, 15f), 0), _parentProps);
            veh.isStatic = true;
            total++;

            if (total % 20 == 0) yield return null;
            if (total >= 50) break; // máximo 50 coches aparcados
        }

        AlsasuaLogger.Info("SueloAAA", $"Vehículos aparcados: {total}");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  CARGA DE MATERIALES Y PREFABS
    // ════════════════════════════════════════════════════════════════════════

    void CargarMateriales()
    {
        _matAsfalto   = CargarMat("asphalt_01",        "Suelo")
                     ?? MatColor(new Color(0.22f,0.22f,0.24f), 0.35f);
        _matAcera     = CargarMat("cobblestone_01",    "Suelo")
                     ?? MatColor(new Color(0.72f,0.70f,0.66f), 0.30f);
        _matBordillo  = CargarMat("anti_slip_concrete","Suelo")
                     ?? MatColor(new Color(0.78f,0.78f,0.76f), 0.25f);
        _matHierba    = CargarMat("aerial_grass_rock", "Suelo")
                     ?? MatColor(new Color(0.32f,0.52f,0.24f), 0.20f);
        _matRoca      = CargarMat("aerial_rocks_01",   "Suelo")
                     ?? MatColor(new Color(0.55f,0.52f,0.48f), 0.45f);
        _matTierra    = CargarMat("brown_mud",         "Suelo")
                     ?? MatColor(new Color(0.48f,0.36f,0.24f), 0.20f);

        // Marca vial: blanco muy suave
        _matMarcaVial = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"))
        {
            color = new Color(0.90f, 0.90f, 0.85f)
        };
        _matMarcaVial.SetFloat("_Smoothness", 0.1f);

        AlsasuaLogger.Info("SueloAAA", "Materiales de suelo cargados");
    }

    Material CargarMat(string nombre, string subcarpeta)
    {
#if UNITY_EDITOR
        var path = $"Assets/Materials/{subcarpeta}/{nombre}.mat";
        var mat  = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
#endif
        return Resources.Load<Material>($"Materials/{subcarpeta}/{nombre}");
    }

    Material MatColor(Color c, float smooth)
    {
        var m = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard")) { color = c };
        m.SetFloat("_Smoothness", smooth);
        return m;
    }

    void CargarPrefabs()
    {
        string extractedBase = "Assets/_ExtractedAssets";

        // Árboles — 12 especies nativas de Navarra
        _prefabsArboles = CargarDesdeCarpeta(
            extractedBase + "/Vegetation",
            new[]{ "Roble_Ingles", "Haya_Europea", "Pino_Baltico", "Aliso_Negro",
                   "Avellano", "Alamo_Templon", "Sauce_Caprio", "Arce_Noruego",
                   "Pino_Carrasco", "plant-pack" });

        if (_prefabsArboles.Count == 0)
        {
            // Fallback procedural si los FBX no tienen prefab aún
            _prefabsArboles.Add(CrearArbolProcedural("Roble", 0.35f, new Color(0.15f,0.42f,0.12f)));
            _prefabsArboles.Add(CrearArbolProcedural("Pino",  0.25f, new Color(0.08f,0.32f,0.08f)));
        }

        // Props urbanos
        _prefabsProps = CargarDesdeCarpeta(
            extractedBase + "/Props",
            new[]{ "trashcan", "Container_Metal", "PicnicTable",
                   "playground_fbx", "Electric_Panel", "bridge_roads" });
        // Añadir props de Poly Haven
        _prefabsProps.AddRange(CargarDesdeCarpeta(
            extractedBase + "/Props/PolyHaven",
            new[]{ "Barrel_01", "WoodenChair_01", "WoodenTable_01",
                   "Shelf_01", "Lantern_01", "Megaphone_01" }));

        // Animales
        _prefabsAnimales = CargarDesdeCarpeta(
            extractedBase + "/Animals",
            new[]{ "Oveja", "Caballo", "Gallina", "deer-female-mesh",
                   "rabbit", "blackrat", "dog" });

        // Vehículos
        _prefabsVehiculos = CargarDesdeCarpeta(
            extractedBase + "/Vehicles",
            new[]{ "Trabant_601", "Bicicletas", "SM_JUNKCAR1_DEFORMED2" });

        AlsasuaLogger.Info("SueloAAA",
            $"Prefabs: {_prefabsArboles.Count} árboles, " +
            $"{_prefabsProps.Count} props, " +
            $"{_prefabsAnimales.Count} animales, " +
            $"{_prefabsVehiculos.Count} vehículos");
    }

    List<GameObject> CargarDesdeCarpeta(string carpeta, string[] nombres)
    {
        var lista = new List<GameObject>();
#if UNITY_EDITOR
        foreach (var n in nombres)
        {
            var guids = UnityEditor.AssetDatabase.FindAssets($"t:Model {n}", new[]{ carpeta });
            if (guids.Length == 0)
                guids = UnityEditor.AssetDatabase.FindAssets($"t:Prefab {n}", new[]{ "Assets/Prefabs/FBX" });
            foreach (var g in guids)
            {
                var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(g));
                if (go != null && !lista.Contains(go)) { lista.Add(go); break; }
            }
        }
#endif
        // Fallback: buscar en Resources
        foreach (var n in nombres)
        {
            var go = Resources.Load<GameObject>($"Prefabs/{n}");
            if (go != null && !lista.Contains(go)) lista.Add(go);
        }
        return lista;
    }

    GameObject CrearArbolProcedural(string nombre, float anchoTronco, Color colorCopa)
    {
        var root   = new GameObject(nombre);
        var tronco = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tronco.transform.SetParent(root.transform);
        tronco.transform.localPosition = new Vector3(0, 2f, 0);
        tronco.transform.localScale    = new Vector3(anchoTronco, 2f, anchoTronco);
        var matT = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"))
            { color = new Color(0.35f, 0.24f, 0.12f) };
        tronco.GetComponent<Renderer>().sharedMaterial = matT;
        Object.Destroy(tronco.GetComponent<Collider>());

        var copa = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        copa.transform.SetParent(root.transform);
        copa.transform.localPosition = new Vector3(0, 5f, 0);
        copa.transform.localScale    = new Vector3(3.5f, 3f, 3.5f);
        var matC = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"))
            { color = colorCopa };
        copa.GetComponent<Renderer>().sharedMaterial = matC;
        Object.Destroy(copa.GetComponent<Collider>());

        return root;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    void CrearParents()
    {
        _parentCalles    = new GameObject("Suelo_Calles").transform;
        _parentVegetacion= new GameObject("Vegetacion_OSM").transform;
        _parentProps     = new GameObject("Props_Urbanos").transform;
        _parentAnimales  = new GameObject("Animales_Campo").transform;
    }

    GameObject CrearPrimitivo(string n, Transform p, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = n;
        go.transform.SetParent(p);
        go.transform.position   = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.Destroy(go.GetComponent<Collider>());
        go.isStatic = true;
        return go;
    }

    void OnDestroy()
    {
        GeneradorMundoOSM.OnMundoGenerado -= OnMundoListo;
    }
}
