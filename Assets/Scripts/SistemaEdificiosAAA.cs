// Assets/Scripts/SistemaEdificiosAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE EDIFICIOS AAA — fachadas perfectas para Alsasua
//
//  Genera edificios desde OSM con:
//    • 30 materiales de fachada PBR reales (Poly Haven CC0)
//    • 5 materiales de tejado reales (tejas cerámicas, pizarra)
//    • Geometría de fachada completa por planta:
//        - Ventanas con vidrio + marco + dintel
//        - Balcones con losa + barandilla de hierro fundido
//        - Cornisa superior
//        - Zócalo de piedra
//        - Puertas con marquesina en PB
//    • Tejados con pendiente (residencial) o planos (comercial)
//    • Chimeneas procedurales (1-3 por edificio)
//    • Variación por barrio: casco histórico vs moderno
//    • Luces de ventana nocturnas (emisión dinámica)
//    • LOD 3 niveles (completo / caja / culled)
//    • Graffiti político vasco en 35% de fachadas
//    • BatchStaticFlag para GPU batching
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

[DefaultExecutionOrder(-75)]
public class SistemaEdificiosAAA : MonoBehaviour
{
    public static SistemaEdificiosAAA Instance { get; private set; }
    public static bool Listo { get; private set; }

    // ── Paleta de fachadas reales (Poly Haven) ─────────────────────────────
    // Nombres exactos de Assets/Textures_AAA/Fachadas/
    static readonly string[] PALETA_FACHADA_RESIDENCIAL = {
        "beige_wall_001", "beige_wall_002", "clay_plaster", "rough_plaster_03",
        "rough_plaster_brick", "plaster_brick_01", "blue_plaster_weathered",
        "sandstone_blocks_04", "sandstone_brick_wall_01", "white_sandstone_bricks",
        "brown_brick_02", "clay_block_wall"
    };
    static readonly string[] PALETA_FACHADA_HISTORICA = {
        "brick_wall_001", "brick_wall_002", "brick_wall_003", "brick_wall_005",
        "brick_wall_006", "brick_wall_07", "brown_brick_02",
        "castle_brick_01", "church_bricks_03", "stone_brick_wall_001",
        "worn_brick_wall", "broken_brick_wall"
    };
    static readonly string[] PALETA_FACHADA_COMERCIAL = {
        "brushed_concrete", "brushed_concrete_03", "brushed_concrete_2",
        "brick_wall_04", "brick_wall_02"
    };
    static readonly string[] PALETA_TEJADO = {
        "ceramic_roof_01", "clay_roof_tiles", "clay_roof_tiles_02",
        "clay_roof_tiles_03", "castle_wall_slates"
    };

    // ── Textos graffiti vascos ─────────────────────────────────────────────
    static readonly (string texto, Color color)[] GRAFFITI = {
        ("ASKATASUNA",      new Color(0.9f,0.1f,0.1f)),
        ("PRESOAK ETXERA",  new Color(0.1f,0.1f,0.8f)),
        ("INDEPENDENTZIA",  new Color(0.8f,0.7f,0.0f)),
        ("AMNISTIA",        new Color(0.1f,0.6f,0.1f)),
        ("GORA EH",         new Color(0.9f,0.1f,0.1f)),
        ("ACAB",            new Color(0.0f,0.0f,0.0f)),
        ("NO PASARÁN",      new Color(0.7f,0.0f,0.0f)),
        ("HERRIRA",         new Color(0.0f,0.4f,0.8f)),
        ("ETA",             new Color(0.9f,0.1f,0.1f)),
        ("BIZI NAIZ",       new Color(0.1f,0.5f,0.2f)),
    };

    // ── Cache de materiales cargados ───────────────────────────────────────
    readonly Dictionary<string, Material> _matCache = new();
    Material[] _matsResidencial, _matsHistorico, _matsComercial, _matsTejado;
    Material   _matVidrio, _matMarco, _matHierro, _matCornisa, _matZocalo;

    // ── Estado ────────────────────────────────────────────────────────────
    Transform _parentEdificios;
    int       _totalEdificios;

    // ── Luces nocturnas ───────────────────────────────────────────────────
    readonly List<Renderer> _ventanas    = new(); // renderers de vidrios
    bool _esNoche;

    // ═══════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        // Cargar materiales inmediatamente (no esperar al mundo)
        CargarMateriales();

        // Hook con SistemaZonas — enriquecer edificios al cargarse cada zona
        SistemaZonas.OnZonaCargada += OnZonaCargada;

        // Hook legado por si GeneradorMundoOSM crea directamente (fallback)
        GeneradorMundoOSM.OnMundoGenerado += OnMundoGeneradoLegado;

        Listo = true;
        AlsasuaLogger.Info("EdificiosAAA", "Listo — materiales PBR cargados");
    }

    void OnZonaCargada(Vector2Int _)
    {
        // Opcional: enriquecer edificios básicos que llegaran sin pasar por ConstruirEdificio
        // (ej. si SistemaZonas usó el fallback de GeneradorMundoOSM)
    }

    void OnMundoGeneradoLegado()
    {
        // Solo aplica si no se usa zone streaming ni geometría precisa
        if (GeneradorGeometriaPrecisa.Instance != null) return;
        var parent = GameObject.Find("Edificios_OSM");
        if (parent == null) return;
        StartCoroutine(EnriquecerTodo());
    }

    void OnDestroy()
    {
        SistemaZonas.OnZonaCargada            -= OnZonaCargada;
        GeneradorMundoOSM.OnMundoGenerado     -= OnMundoGeneradoLegado;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  API PÚBLICA — llamada por SistemaZonas para cada edificio de zona
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Construye un edificio AAA completo (base + detalles) y lo añade bajo <paramref name="parent"/>.
    /// Reemplaza GeneradorMundoOSM.ConstruirEdificio() con calidad visual superior.
    /// </summary>
    public void ConstruirEdificio(EdificioData e, Transform parent)
    {
        if (e.vertices == null || e.vertices.Length < 3) return;

        // ── Base mesh (misma lógica que GeneradorMundoOSM pero con material AAA) ──
        var verts2D = new List<Vector2>();
        foreach (var v in e.vertices)
            verts2D.Add(new Vector2(v.x + GeoDataAlsasua.OX, v.z + GeoDataAlsasua.OZ));

        float cx = 0, cz = 0;
        foreach (var v in verts2D) { cx += v.x; cz += v.y; }
        cx /= verts2D.Count; cz /= verts2D.Count;

        float suelo  = AlturaTerreno(cx, cz);
        float altura = Mathf.Max(3.2f, e.height > 0 ? e.height : e.levels * 3.2f);

        var mesh = GeneradorMundoOSM.Instance != null
            ? GenerarMeshEdificio(verts2D, suelo, altura)
            : null;

        if (mesh == null) return;

        string nombre = string.IsNullOrEmpty(e.name)
            ? $"Edif_{e.id}" : $"Edif_{e.name.Replace(" ", "_")}";
        var go = new GameObject(nombre);
        go.transform.SetParent(parent);
        go.isStatic = true;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        bool esHistorico = e.levels <= 3 && (cx + cz) % 2 < 1.1f;
        bool esComercial = e.type is "commercial" or "retail" or "office";
        var matFachada   = ElegirMaterialFachada(esHistorico, esComercial);

        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = matFachada;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        // Collider solo cercanos al centro de la ciudad
        if (Vector2.Distance(new Vector2(cx, cz), new Vector2(GeoDataAlsasua.OX, GeoDataAlsasua.OZ)) < 200f)
            go.AddComponent<MeshCollider>().sharedMesh = mesh;

        // ── Enriquecimiento AAA ───────────────────────────────────────────────
        EnriquecerEdificio(go.transform);

        _totalEdificios++;
    }

    Mesh GenerarMeshEdificio(List<Vector2> planta, float suelo, float altura)
        => MeshBuilder.Edificio(planta, suelo, altura);

    static float AlturaTerreno(float x, float z) => GeoDataAlsasua.AlturaTerreno(x, z);

    // ════════════════════════════════════════════════════════════════════════
    //  PIPELINE PRINCIPAL
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator EnriquecerTodo()
    {
        yield return new WaitForSeconds(0.5f);
        // CargarMateriales() ya fue llamado en Start() — no repetir
        yield return null;

        // Si ya hay geometría precisa activa, SistemaEdificiosAAA no necesita actuar
        if (GeneradorGeometriaPrecisa.Instance != null)
        {
            AlsasuaLogger.Info("EdificiosAAA",
                "GeneradorGeometriaPrecisa activo — SistemaEdificiosAAA cede el paso");
            yield break;
        }

        _parentEdificios = (GameObject.Find("Edificios_OSM"))?.transform;
        if (_parentEdificios == null) yield break;

        int i = 0;
        foreach (Transform edif in _parentEdificios)
        {
            if (edif == null) continue;
            var mf = edif.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            EnriquecerEdificio(edif);
            i++;
            if (i % 20 == 0) yield return null;
        }

        Listo = true;
        AlsasuaLogger.Info("EdificiosAAA", $"✅ {i} edificios enriquecidos con fachadas PBR");

        // Iniciar ciclo noche/día
        StartCoroutine(CicloLucesNocturnas());
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ENRIQUECIMIENTO POR EDIFICIO
    // ════════════════════════════════════════════════════════════════════════

    void EnriquecerEdificio(Transform edif)
    {
        var mesh   = edif.GetComponent<MeshFilter>().sharedMesh;
        var bounds = mesh.bounds;
        float h    = bounds.size.y;
        float w    = Mathf.Max(bounds.size.x, bounds.size.z);
        int   pisos= Mathf.Max(1, Mathf.RoundToInt(h / 3.2f));

        // ── Determinar tipo y material de fachada ─────────────────────────
        bool esHistorico = pisos <= 3 && Random.value < 0.55f;
        bool esComercial = edif.name.Contains("commercial") || edif.name.Contains("retail");
        var matFachada   = ElegirMaterialFachada(esHistorico, esComercial);

        // Aplicar material al mesh base
        edif.GetComponent<MeshRenderer>().sharedMaterial = matFachada;

        // ── Zócalo ────────────────────────────────────────────────────────
        AnadirZocalo(edif, bounds);

        // ── Ventanas por planta ───────────────────────────────────────────
        for (int piso = 0; piso < pisos; piso++)
        {
            float yBase    = bounds.min.y + 0.35f + piso * 3.2f;
            bool  esPlBaja = piso == 0;
            AnadirVentanasFachada(edif, bounds, yBase, esPlBaja);
        }

        // ── Balcones (pisos 1-penúltimo, 45% probabilidad) ───────────────
        for (int piso = 1; piso < pisos - 1; piso++)
        {
            if (Random.value < 0.45f)
                AnadirBalcon(edif, bounds, bounds.min.y + piso * 3.2f + 0.9f);
        }

        // ── Tejado ────────────────────────────────────────────────────────
        if (esHistorico && pisos <= 4)
            AnadirTejadoPendiente(edif, bounds, h);
        else
            AnadirCornisa(edif, bounds, h);

        // ── Chimeneas (25% edificios con tejado) ──────────────────────────
        if (esHistorico && Random.value < 0.25f)
            AnadirChimeneas(edif, bounds, h);

        // ── Graffiti (35% fachadas) ───────────────────────────────────────
        if (Random.value < 0.35f)
            AnadirGraffiti(edif, bounds);

        // ── LOD ───────────────────────────────────────────────────────────
        ConfigurarLOD(edif, edif.GetComponent<MeshRenderer>(), mesh);

        // Static para batching
        edif.gameObject.isStatic = true;
#if UNITY_EDITOR
        UnityEditor.GameObjectUtility.SetStaticEditorFlags(edif.gameObject,
            UnityEditor.StaticEditorFlags.BatchingStatic |
            UnityEditor.StaticEditorFlags.ContributeGI   |
            UnityEditor.StaticEditorFlags.OccluderStatic);
#endif
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ELEMENTOS DE FACHADA
    // ════════════════════════════════════════════════════════════════════════

    void AnadirZocalo(Transform edif, Bounds b)
    {
        var go = PrimitivoHijo("Zocalo", edif,
            new Vector3(b.center.x, b.min.y + 0.175f, b.center.z),
            new Vector3(b.size.x + 0.08f, 0.35f, b.size.z + 0.08f),
            _matZocalo);
        go.isStatic = true;
    }

    void AnadirVentanasFachada(Transform edif, Bounds b, float yBase, bool esPlBaja)
    {
        float altV = esPlBaja ? 2.0f : 1.1f;
        float ancV = esPlBaja ? 1.3f : 0.85f;
        float paso = esPlBaja ? 2.8f : 1.9f;

        int nW = Mathf.Max(1, Mathf.FloorToInt(b.size.x / paso));
        int nD = Mathf.Max(1, Mathf.FloorToInt(b.size.z / paso));

        // Fachadas principal y trasera
        for (int k = 0; k < nW; k++)
        {
            float x = b.min.x + (k + 0.5f) * (b.size.x / nW);
            float yV = yBase + (esPlBaja ? 0.6f : 1.0f);
            CrearVentana(edif, new Vector3(x, yV, b.min.z - 0.005f), Quaternion.Euler(0,180,0), ancV, altV, esPlBaja);
            CrearVentana(edif, new Vector3(x, yV, b.max.z + 0.005f), Quaternion.identity,       ancV, altV, false);
        }
        // Laterales
        for (int k = 0; k < nD; k++)
        {
            float z = b.min.z + (k + 0.5f) * (b.size.z / nD);
            float yV = yBase + 1.0f;
            CrearVentana(edif, new Vector3(b.min.x - 0.005f, yV, z), Quaternion.Euler(0,90,0),   ancV, altV, false);
            CrearVentana(edif, new Vector3(b.max.x + 0.005f, yV, z), Quaternion.Euler(0,-90,0),  ancV, altV, false);
        }
    }

    void CrearVentana(Transform edif, Vector3 pos, Quaternion rot, float ancho, float alto, bool esVitrina)
    {
        var root = new GameObject(esVitrina ? "Vitrina" : "Ventana");
        root.transform.SetParent(edif);
        root.transform.SetPositionAndRotation(pos, rot);
        root.isStatic = true;

        // Dintel superior
        PrimitivoHijo("Dintel", root.transform,
            new Vector3(0, alto * 0.5f + 0.05f, 0f),
            new Vector3(ancho + 0.1f, 0.1f, 0.08f), _matCornisa);

        // Marco
        PrimitivoHijo("Marco", root.transform,
            Vector3.zero,
            new Vector3(ancho + 0.1f, alto + 0.1f, 0.07f), _matMarco);

        // Vidrio (con emisión nocturna)
        var vidrio = PrimitivoHijo("Vidrio", root.transform,
            new Vector3(0, 0, -0.02f),
            new Vector3(ancho - 0.05f, alto - 0.05f, 0.02f), _matVidrio);
        _ventanas.Add(vidrio.GetComponent<MeshRenderer>());

        // División central (solo ventanas normales)
        if (!esVitrina)
            PrimitivoHijo("Division", root.transform,
                new Vector3(0, 0, -0.015f),
                new Vector3(0.04f, alto - 0.05f, 0.025f), _matMarco);
    }

    void AnadirBalcon(Transform edif, Bounds b, float y)
    {
        float anchoB = Mathf.Min(b.size.x * 0.6f, 2.2f);

        // Losa
        PrimitivoHijo("BalconLosa", edif,
            new Vector3(b.center.x, y, b.min.z - 0.5f),
            new Vector3(anchoB, 0.12f, 1.0f), _matCornisa).isStatic = true;

        // Barandilla — perfil en U de hierro
        float bx0 = b.center.x - anchoB * 0.5f;
        float bz   = b.min.z - 0.95f;
        int  nBarras = Mathf.Max(3, (int)(anchoB * 2.8f));

        for (int k = 0; k <= nBarras; k++)
        {
            float bx = bx0 + k * (anchoB / nBarras);
            PrimitivoHijo($"Barra{k}", edif,
                new Vector3(bx, y + 0.44f, bz),
                new Vector3(0.035f, 0.8f, 0.035f), _matHierro).isStatic = true;
        }
        // Barra horizontal superior
        PrimitivoHijo("BarraH", edif,
            new Vector3(b.center.x, y + 0.86f, bz),
            new Vector3(anchoB, 0.055f, 0.055f), _matHierro).isStatic = true;
        // Barra horizontal inferior
        PrimitivoHijo("BarraHB", edif,
            new Vector3(b.center.x, y + 0.05f, bz),
            new Vector3(anchoB, 0.045f, 0.045f), _matHierro).isStatic = true;
    }

    void AnadirCornisa(Transform edif, Bounds b, float h)
    {
        PrimitivoHijo("Cornisa", edif,
            new Vector3(b.center.x, b.min.y + h + 0.15f, b.center.z),
            new Vector3(b.size.x + 0.35f, 0.30f, b.size.z + 0.35f),
            _matCornisa).isStatic = true;
    }

    void AnadirTejadoPendiente(Transform edif, Bounds b, float h)
    {
        var matTejado = _matsTejado != null && _matsTejado.Length > 0
            ? _matsTejado[Mathf.Abs(edif.GetInstanceID()) % _matsTejado.Length]
            : _matCornisa;

        // Tejado a dos aguas — dos triángulos prismáticos
        float crest = Mathf.Min(b.size.x, b.size.z) * 0.42f; // altura de cresta

        // Lado derecho
        var tejR = new GameObject("TejadoR");
        tejR.transform.SetParent(edif);
        tejR.transform.position  = new Vector3(b.center.x, b.min.y + h, b.center.z);
        tejR.transform.rotation  = Quaternion.Euler(0, 0, -Mathf.Atan2(crest, b.size.x * 0.5f) * Mathf.Rad2Deg);
        tejR.isStatic = true;
        var mf = tejR.AddComponent<MeshFilter>();
        mf.sharedMesh = GenerarMeshTejado(b.size.x, b.size.z, crest, true);
        var mr = tejR.AddComponent<MeshRenderer>();
        mr.sharedMaterial = matTejado;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        // Lado izquierdo (espejo)
        var tejL = new GameObject("TejadoL");
        tejL.transform.SetParent(edif);
        tejL.transform.position = new Vector3(b.center.x, b.min.y + h, b.center.z);
        tejL.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(crest, b.size.x * 0.5f) * Mathf.Rad2Deg);
        tejL.isStatic = true;
        var mf2 = tejL.AddComponent<MeshFilter>();
        mf2.sharedMesh = GenerarMeshTejado(b.size.x, b.size.z, crest, false);
        var mr2 = tejL.AddComponent<MeshRenderer>();
        mr2.sharedMaterial = matTejado;

        // Cornisa bajo el tejado
        AnadirCornisa(edif, b, h - 0.1f);
    }

    Mesh GenerarMeshTejado(float w, float d, float crest, bool lado)
    {
        // Plano inclinado de tejado
        float sign = lado ? 1f : -1f;
        var verts = new Vector3[]
        {
            new Vector3( sign * w * 0.5f, 0, -d * 0.5f),
            new Vector3( sign * w * 0.5f, 0,  d * 0.5f),
            new Vector3( 0,           crest, -d * 0.5f),
            new Vector3( 0,           crest,  d * 0.5f),
        };
        var tris = new int[] { 0, 2, 1, 1, 2, 3 };
        var uvs  = new Vector2[] {
            new(0,0), new(0,1), new(1,0), new(1,1)
        };
        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        return mesh;
    }

    void AnadirChimeneas(Transform edif, Bounds b, float h)
    {
        int n = Random.Range(1, 3);
        for (int k = 0; k < n; k++)
        {
            float cx = b.min.x + Random.Range(0.2f, 0.8f) * b.size.x;
            float cz = b.min.z + Random.Range(0.2f, 0.8f) * b.size.z;
            float cy = b.min.y + h;
            float altChi = Random.Range(0.6f, 1.2f);
            float anchoChi = Random.Range(0.25f, 0.45f);

            // Cuerpo de chimenea
            PrimitivoHijo($"Chimenea{k}", edif,
                new Vector3(cx, cy + altChi * 0.5f, cz),
                new Vector3(anchoChi, altChi, anchoChi), _matZocalo).isStatic = true;

            // Sombrero
            PrimitivoHijo($"ChimSombrero{k}", edif,
                new Vector3(cx, cy + altChi + 0.05f, cz),
                new Vector3(anchoChi + 0.12f, 0.1f, anchoChi + 0.12f), _matCornisa).isStatic = true;
        }
    }

    void AnadirGraffiti(Transform edif, Bounds b)
    {
        var (texto, color) = GRAFFITI[Random.Range(0, GRAFFITI.Length)];
        bool fachada = Random.value > 0.5f;

        Vector3 pos;
        Quaternion rot;
        if (fachada)
        {
            pos = new Vector3(
                b.min.x + Random.Range(0.15f, 0.85f) * b.size.x,
                b.min.y + Random.Range(0.5f, 2.0f), b.min.z - 0.012f);
            rot = Quaternion.Euler(0, 180, 0);
        }
        else
        {
            pos = new Vector3(b.min.x - 0.012f,
                b.min.y + Random.Range(0.5f, 2.0f),
                b.min.z + Random.Range(0.15f, 0.85f) * b.size.z);
            rot = Quaternion.Euler(0, 90, 0);
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = $"Graffiti_{texto.Replace(" ","_")}";
        go.transform.SetParent(edif);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = new Vector3(
            Random.Range(0.9f, 2.2f), Random.Range(0.35f, 0.6f), 1f);

        var matG = new Material(Shader.Find("HDRP/Unlit") ?? Shader.Find("Standard"))
            { color = new Color(color.r, color.g, color.b, 0.88f) };
        go.GetComponent<MeshRenderer>().sharedMaterial = matG;
        go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Object.Destroy(go.GetComponent<Collider>());
        go.isStatic = true;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  LOD — 3 niveles
    // ════════════════════════════════════════════════════════════════════════

    void ConfigurarLOD(Transform edif, MeshRenderer mrBase, Mesh mesh)
    {
        // Todos los renderers del edificio (base + ventanas + detalles)
        var allRenderers = edif.GetComponentsInChildren<MeshRenderer>();

        // LOD1: solo el cuerpo base + primer nivel de ventanas
        var rendLOD1 = allRenderers.Where(r =>
            r.gameObject.name == edif.name ||
            r.gameObject.name.StartsWith("Ventana") ||
            r.gameObject.name.StartsWith("Vitrina")).ToArray();

        // LOD2: caja simplificada
        var cajaSimpGO = new GameObject("LOD2_Caja");
        cajaSimpGO.transform.SetParent(edif);
        cajaSimpGO.transform.localPosition = mesh.bounds.center - edif.position;
        var cajaMF = cajaSimpGO.AddComponent<MeshFilter>();
        cajaMF.sharedMesh = CuboUnitarioCacheado();
        var cajaMR = cajaSimpGO.AddComponent<MeshRenderer>();
        cajaMR.sharedMaterial = mrBase.sharedMaterial;
        cajaMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cajaSimpGO.transform.localScale = mesh.bounds.size;
        cajaSimpGO.isStatic = true;

        if (edif.GetComponent<LODGroup>() != null) return; // ya tiene LOD

        var lod = edif.gameObject.AddComponent<LODGroup>();
        lod.fadeMode = LODFadeMode.CrossFade;
        lod.SetLODs(new LOD[]
        {
            new LOD(0.06f, allRenderers),               // LOD0 completo < 60m
            new LOD(0.015f, rendLOD1),                  // LOD1 parcial < 200m
            new LOD(0.003f, new Renderer[]{cajaMR}),    // LOD2 caja < 500m
            new LOD(0f,     new Renderer[0])            // culled
        });
        lod.RecalculateBounds();
    }

    static Mesh _cuboCache;
    static Mesh CuboUnitarioCacheado()
    {
        if (_cuboCache != null) return _cuboCache;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cuboCache = tmp.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(tmp);
        return _cuboCache;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  LUCES NOCTURNAS
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator CicloLucesNocturnas()
    {
        while (true)
        {
            yield return new WaitForSeconds(30f);

            var atm = AltsasuCore.I?.atmosferaSystem;
            bool noche = atm != null && (!atm.EsDeDia);

            if (noche != _esNoche)
            {
                _esNoche = noche;
                yield return StartCoroutine(TransicionLuces(noche));
            }
        }
    }

    IEnumerator TransicionLuces(bool encender)
    {
        // Solo actualizar un subconjunto para evitar spike de CPU
        for (int i = 0; i < _ventanas.Count; i += 3)
        {
            var r = _ventanas[i];
            if (r == null) continue;

            // 70% de ventanas encendidas de noche — resto apagadas
            bool estaEncendida = encender && (i % 10 != 0) && (i % 7 != 0);
            var mat = r.material; // instancia propia

            if (mat.HasProperty("_EmissiveColor"))
            {
                Color emissive = estaEncendida
                    ? new Color(1.0f, 0.88f, 0.62f) * 2.5f  // cálido interior
                    : Color.black;
                mat.SetColor("_EmissiveColor", emissive);

                if (estaEncendida)
                    mat.EnableKeyword("_EMISSION");
                else
                    mat.DisableKeyword("_EMISSION");
            }

            if (i % 50 == 0) yield return null;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  MATERIALES PBR
    // ════════════════════════════════════════════════════════════════════════

    void CargarMateriales()
    {
        _matsResidencial = CargarPaleta(PALETA_FACHADA_RESIDENCIAL, "Fachadas");
        _matsHistorico   = CargarPaleta(PALETA_FACHADA_HISTORICA,   "Fachadas");
        _matsComercial   = CargarPaleta(PALETA_FACHADA_COMERCIAL,   "Fachadas");
        _matsTejado      = CargarPaleta(PALETA_TEJADO,              "Tejados");

        _matVidrio  = MatPBR(new Color(0.55f,0.75f,0.90f,0.25f), "Vidrio",  0.9f, 0.05f);
        _matMarco   = MatPBR(new Color(0.22f,0.20f,0.17f),       "Marco",   0.3f, 0.0f);
        _matHierro  = MatPBR(new Color(0.18f,0.18f,0.20f),       "Hierro",  0.6f, 0.85f);
        _matCornisa = MatPBR(new Color(0.82f,0.79f,0.75f),       "Cornisa", 0.4f, 0.0f);
        _matZocalo  = MatPBR(new Color(0.52f,0.48f,0.44f),       "Zocalo",  0.5f, 0.0f);

        AlsasuaLogger.Info("EdificiosAAA",
            $"Materiales: {_matsResidencial.Length} residencial, " +
            $"{_matsHistorico.Length} histórico, {_matsComercial.Length} comercial, " +
            $"{_matsTejado.Length} tejado");
    }

    Material[] CargarPaleta(string[] nombres, string subcarpeta)
    {
        var lista = new List<Material>();
        string shader = "HDRP/Lit";

        foreach (var n in nombres)
        {
            // Buscar el material HDRP ya creado (por FLUJO_COMPLETO / CreadorEscenaPrincipal)
            string matPath = $"Assets/Materials/Edificios/{n}.mat";
            // Cargar material pre-creado desde Resources (si está exportado como asset)
        var matAsset = Resources.Load<Material>($"Materials/Edificios/{n}");
#if UNITY_EDITOR
        if (matAsset == null)
            matAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(matPath);
#endif
        if (matAsset != null)
        {
            lista.Add(matAsset);
            continue;
        }
            // Fallback: crear material desde texturas en Resources o con color plano
            var mat = CrearMaterialDesdePaleta(n, subcarpeta, shader);
            if (mat != null) lista.Add(mat);
        }

        // Garantizar al menos 1 material
        if (lista.Count == 0)
            lista.Add(MatPBR(new Color(0.82f,0.79f,0.72f), "Fallback", 0.5f, 0.0f));

        return lista.ToArray();
    }

    Material CrearMaterialDesdePaleta(string nombre, string subcarpeta, string shaderName)
    {
        if (_matCache.TryGetValue(nombre, out var cached)) return cached;

        var shader = Shader.Find(shaderName) ?? Shader.Find("Standard");
        var mat    = new Material(shader) { name = nombre };

        // Buscar textura Albedo en Resources (si está ahí)
        var tex = Resources.Load<Texture2D>($"Textures/{nombre}_Albedo");
        if (tex == null) tex = Resources.Load<Texture2D>($"Textures/{nombre}_diffuse");
        if (tex != null)
        {
            if (mat.HasProperty("_BaseColorMap")) mat.SetTexture("_BaseColorMap", tex);
            else if (mat.HasProperty("_MainTex"))  mat.SetTexture("_MainTex",     tex);
        }

        // Colores de fallback por nombre
        mat.color = ColorDesdNombre(nombre);

        _matCache[nombre] = mat;
        return mat;
    }

    Color ColorDesdNombre(string n)
    {
        if (n.Contains("beige"))     return new Color(0.88f, 0.84f, 0.76f);
        if (n.Contains("brick"))     return new Color(0.72f, 0.42f, 0.32f);
        if (n.Contains("brown"))     return new Color(0.60f, 0.38f, 0.26f);
        if (n.Contains("blue"))      return new Color(0.55f, 0.65f, 0.75f);
        if (n.Contains("concrete"))  return new Color(0.68f, 0.68f, 0.70f);
        if (n.Contains("clay"))      return new Color(0.78f, 0.68f, 0.58f);
        if (n.Contains("plaster"))   return new Color(0.86f, 0.84f, 0.80f);
        if (n.Contains("sandstone")) return new Color(0.85f, 0.78f, 0.62f);
        if (n.Contains("stone"))     return new Color(0.72f, 0.70f, 0.66f);
        if (n.Contains("church"))    return new Color(0.80f, 0.76f, 0.68f);
        if (n.Contains("castle"))    return new Color(0.70f, 0.66f, 0.60f);
        if (n.Contains("ceramic"))   return new Color(0.80f, 0.38f, 0.22f);
        if (n.Contains("slate"))     return new Color(0.38f, 0.38f, 0.42f);
        return new Color(0.78f, 0.75f, 0.70f);
    }

    Material ElegirMaterialFachada(bool esHistorico, bool esComercial)
    {
        Material[] paleta;
        if (esComercial)       paleta = _matsComercial;
        else if (esHistorico)  paleta = _matsHistorico;
        else                   paleta = _matsResidencial;

        if (paleta == null || paleta.Length == 0)
            return MatPBR(new Color(0.82f,0.79f,0.74f), "FB", 0.4f, 0.0f);

        return paleta[Random.Range(0, paleta.Length)];
    }

    Material MatPBR(Color color, string nombre, float smoothness, float metallic)
    {
        var mat = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"))
            { name = nombre, color = color };
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic",   metallic);
        return mat;
    }

    // ── Primitivo helper ──────────────────────────────────────────────────

    GameObject PrimitivoHijo(string nombre, Transform padre, Vector3 pos, Vector3 escala, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = nombre;
        go.transform.SetParent(padre);
        go.transform.position    = pos;
        go.transform.localScale  = escala;
        go.GetComponent<Renderer>().sharedMaterial = mat ?? _matMarco;
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        return go;
    }

}
