// Assets/Scripts/Systems/InyectorVecinosTerreno.cs
// ═══════════════════════════════════════════════════════════════════════════
//  INYECTOR DE VECINOS — alimenta el shader de costuras con la información
//  espacial de los vecinos de cada tile del Mosaico V2 (capa SYSTEMS).
//
//  Diseño completo: Docs/arquitectura_costuras_terreno.md
//
//  Qué publica por tile (vía MaterialPropertyBlock + SetSplatMaterialPropertyBlock):
//    _TileSize           float        — lado del tile (m)
//    _TileOriginWS       float4(x,0,z,0) — esquina SW en mundo
//    _EdgeBandM          float        — ancho de la banda de borde (m)  → default 2.0
//    _TessBase, _TessEdge float       — factores de tess interior / borde
//    _OverlapBias        float        — bias Z (m); 0 para tiles "limpios"
//    _CosturasActivas    float        — 1 = aplicar; 0 = bypass (toggle QA)
//
//    Por cada cardinal {N,S,E,W}:
//      _Peso{N|S|E|W}       float        — 1 si hay vecino, 0 si no
//      _NbrLado{N..}        float        — lado del vecino (m)
//      _NbrAltura{N..}      float        — size.y del Terrain del vecino (m)
//      _NbrTexel{N..}       float4(1/r,1/r,0,0) — texel size del heightmap del vecino
//      _NbrHeightmap{N..}   Texture2D   — terrainData.heightmapTexture del vecino
//
//  Vecinos:
//    · Intra-anillo: Unity ya los rellena en Terrain.leftNeighbor/topNeighbor/
//      rightNeighbor/bottomNeighbor tras SetNeighbors (lo hace CargadorMosaicoTerreno).
//    · Inter-anillo: se consultan vía ITerrainService.TerrainEn(centro+ladoTile) en
//      el centro del borde correspondiente.
//
//  No depende del Omni-Grid ni del Orquestador. Una sola pasada al cargar.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[DefaultExecutionOrder(-120)] // tras ServicioTerreno (-250) y CargadorMosaicoTerreno; antes de gameplay
public sealed class InyectorVecinosTerreno : MonoBehaviour
{
    // ── Configuración (afina sin recompilar) ────────────────────────────────
    [Header("═══ ACTIVACIÓN ═══")]
    [Tooltip("Maestro: si está OFF, no se publica nada y el shader cae al camino base.")]
    [SerializeField] bool activo = true;

    [Header("═══ COSTURAS (uniforms del shader) ═══")]
    [Tooltip("Ancho de la banda de borde donde se aplica la corrección (m). Default 2.")]
    [SerializeField, Min(0.1f)] float edgeBandM = 2f;
    [Tooltip("Factor de tess en el INTERIOR del tile (lo decide el shader por distancia).")]
    [SerializeField, Min(1f)] float tessBase = 8f;
    [Tooltip("Factor de tess FORZADO en la banda de borde (independiente de la distancia).")]
    [SerializeField, Min(1f)] float tessEdge = 64f;
    [Tooltip("Z-bias (m) hacia la cámara en el borde. 0 = desactivado; 0.003 (3 mm) es seguro.")]
    [SerializeField, Range(0f, 0.02f)] float overlapBias = 0f;

    [Header("═══ DEBUG ═══")]
    [Tooltip("Vuelca un Info por tile cuando se publica el MPB.")]
    [SerializeField] bool logarPorTile = false;

    // ── Estado interno ──────────────────────────────────────────────────────
    static readonly int ID_TileSize        = Shader.PropertyToID("_TileSize");
    static readonly int ID_TileOriginWS    = Shader.PropertyToID("_TileOriginWS");
    static readonly int ID_EdgeBandM       = Shader.PropertyToID("_EdgeBandM");
    static readonly int ID_TessBase        = Shader.PropertyToID("_TessBase");
    static readonly int ID_TessEdge        = Shader.PropertyToID("_TessEdge");
    static readonly int ID_OverlapBias     = Shader.PropertyToID("_OverlapBias");
    static readonly int ID_CosturasActivas = Shader.PropertyToID("_CosturasActivas");

    static readonly int[] ID_Peso       = { Prop("_PesoN"),       Prop("_PesoS"),       Prop("_PesoE"),       Prop("_PesoW") };
    static readonly int[] ID_NbrLado    = { Prop("_NbrLadoN"),    Prop("_NbrLadoS"),    Prop("_NbrLadoE"),    Prop("_NbrLadoW") };
    static readonly int[] ID_NbrAltura  = { Prop("_NbrAlturaN"),  Prop("_NbrAlturaS"),  Prop("_NbrAlturaE"),  Prop("_NbrAlturaW") };
    static readonly int[] ID_NbrTexel   = { Prop("_NbrTexelN"),   Prop("_NbrTexelS"),   Prop("_NbrTexelE"),   Prop("_NbrTexelW") };
    static readonly int[] ID_NbrHeightmap = { Prop("_NbrHeightmapN"), Prop("_NbrHeightmapS"), Prop("_NbrHeightmapE"), Prop("_NbrHeightmapW") };

    static int Prop(string n) => Shader.PropertyToID(n);

    enum Cardinal { N = 0, S = 1, E = 2, W = 3 }

    MaterialPropertyBlock _mpb;
    bool _aplicadoYa;

    // ── Lifecycle ───────────────────────────────────────────────────────────
    void OnEnable()
    {
        EventBus.Subscribe<MosaicoCompletoEvent>(AlMosaicoCompleto);
        EventBus.Subscribe<TerrenoListoEvent>(AlTerrenoListo);
    }

    void OnDisable()
    {
        EventBus.Unsubscribe<MosaicoCompletoEvent>(AlMosaicoCompleto);
        EventBus.Unsubscribe<TerrenoListoEvent>(AlTerrenoListo);
        Shader.SetGlobalFloat(ID_CosturasActivas, 0f);
    }

    void AlTerrenoListo(TerrenoListoEvent _)
    {
        // Si el suelo es un mosaico, esperamos al evento de completo para no aplicar a medias.
        if (!_aplicadoYa) AplicarTodo();
    }

    void AlMosaicoCompleto(MosaicoCompletoEvent _)
    {
        _aplicadoYa = false; // forzar reaplicación con todos los tiles ya instanciados
        AplicarTodo();
    }

    /// <summary>Reaplica el MPB a un único tile (tras editar su heightmap, etc.).</summary>
    public void Reactualizar(Terrain t)
    {
        if (t == null || t.terrainData == null) return;
        AplicarA(t);
    }

    // ── Núcleo ─────────────────────────────────────────────────────────────
    void AplicarTodo()
    {
        Shader.SetGlobalFloat(ID_CosturasActivas, activo ? 1f : 0f);
        if (!activo) return;

        var servicio = ServiceLocator.Get<ITerrainService>();
        if (servicio == null) return;

        var tiles = servicio.Tiles;
        if (tiles == null || tiles.Count == 0)
        {
            // Terreno único (DEM 6×6 km o plano): aplica al singular para mantener consistencia.
            if (servicio.Terreno != null) AplicarA(servicio.Terreno);
            return;
        }

        int aplicados = 0;
        for (int i = 0; i < tiles.Count; i++)
            if (AplicarA(tiles[i])) aplicados++;

        _aplicadoYa = true;
        AlsasuaLogger.Info("CosturasTerreno",
            $"Inyector listo: {aplicados}/{tiles.Count} tiles con vecinos publicados " +
            $"(banda {edgeBandM:F1} m, tessEdge {tessEdge:F0}, bias {overlapBias * 1000f:F1} mm).");
    }

    bool AplicarA(Terrain t)
    {
        if (t == null || t.terrainData == null) return false;
        _mpb ??= new MaterialPropertyBlock();
        _mpb.Clear();

        var td = t.terrainData;
        Vector3 pos = t.transform.position;     // esquina SW del tile
        float lado = td.size.x;                 // los tiles del mosaico son cuadrados

        _mpb.SetFloat(ID_TileSize,        lado);
        _mpb.SetVector(ID_TileOriginWS,   new Vector4(pos.x, 0f, pos.z, 0f));
        _mpb.SetFloat(ID_EdgeBandM,       edgeBandM);
        _mpb.SetFloat(ID_TessBase,        tessBase);
        _mpb.SetFloat(ID_TessEdge,        tessEdge);
        _mpb.SetFloat(ID_OverlapBias,     overlapBias);
        _mpb.SetFloat(ID_CosturasActivas, 1f);

        // ── Vecinos ───────────────────────────────────────────────────────
        // Unity rellena leftNeighbor/topNeighbor/rightNeighbor/bottomNeighbor
        // tras SetNeighbors (intra-anillo). Para inter-anillo se consulta el servicio.
        // Cardinal vs API Unity:
        //   N (norte, +Z) → t.topNeighbor    o  TerrainEn(pos + (lado/2, 0, lado + ε))
        //   S (sur,  -Z) → t.bottomNeighbor o  TerrainEn(pos + (lado/2, 0,        - ε))
        //   E (este, +X) → t.rightNeighbor  o  TerrainEn(pos + (lado + ε, 0, lado/2))
        //   W (oeste,-X) → t.leftNeighbor   o  TerrainEn(pos + (        - ε, 0, lado/2))
        const float EPS = 1f;

        Terrain nN = t.topNeighbor    ?? ServicioVecino(t, pos.x + lado * 0.5f, pos.z + lado + EPS);
        Terrain nS = t.bottomNeighbor ?? ServicioVecino(t, pos.x + lado * 0.5f, pos.z        - EPS);
        Terrain nE = t.rightNeighbor  ?? ServicioVecino(t, pos.x + lado + EPS,  pos.z + lado * 0.5f);
        Terrain nW = t.leftNeighbor   ?? ServicioVecino(t, pos.x        - EPS,  pos.z + lado * 0.5f);

        PonerVecino(_mpb, Cardinal.N, nN);
        PonerVecino(_mpb, Cardinal.S, nS);
        PonerVecino(_mpb, Cardinal.E, nE);
        PonerVecino(_mpb, Cardinal.W, nW);

        // En HDRP el material del Terrain se override via SetSplatMaterialPropertyBlock.
        // El MPB es por-tile; no afecta a los demás Terrain del proyecto.
        t.SetSplatMaterialPropertyBlock(_mpb);

        if (logarPorTile)
            AlsasuaLogger.Info("CosturasTerreno",
                $"  · {t.name}: N={NombreOTrazo(nN)} S={NombreOTrazo(nS)} E={NombreOTrazo(nE)} W={NombreOTrazo(nW)}");

        return true;
    }

    void PonerVecino(MaterialPropertyBlock mpb, Cardinal c, Terrain vec)
    {
        int idx = (int)c;
        if (vec == null || vec.terrainData == null)
        {
            mpb.SetFloat(ID_Peso[idx],     0f);
            mpb.SetFloat(ID_NbrLado[idx],   1f);
            mpb.SetFloat(ID_NbrAltura[idx], 1f);
            mpb.SetVector(ID_NbrTexel[idx], new Vector4(1f, 1f, 0f, 0f));
            // El shader no muestreará (peso 0), pero MPB exige textura asignada en HDRP/Lit Tess:
            // se deja sin asignar — el sampler clamp + peso 0 anula la contribución.
            return;
        }

        var td = vec.terrainData;
        int res = td.heightmapResolution;
        float invRes = res > 1 ? 1f / (res - 1) : 1f;

        mpb.SetFloat(ID_Peso[idx],      1f);
        mpb.SetFloat(ID_NbrLado[idx],   td.size.x);
        mpb.SetFloat(ID_NbrAltura[idx], td.size.y);
        mpb.SetVector(ID_NbrTexel[idx], new Vector4(invRes, invRes, 0f, 0f));

        // terrainData.heightmapTexture es una RenderTexture R16 viva (la sube SetHeights).
        var tex = td.heightmapTexture;
        if (tex != null) mpb.SetTexture(ID_NbrHeightmap[idx], tex);
    }

    Terrain ServicioVecino(Terrain propio, float x, float z)
    {
        var s = ServiceLocator.Get<ITerrainService>();
        var v = s?.TerrainEn(new Vector3(x, 0f, z));
        return (v == propio) ? null : v;
    }

    static string NombreOTrazo(Terrain t) => t != null ? t.name : "—";
}
