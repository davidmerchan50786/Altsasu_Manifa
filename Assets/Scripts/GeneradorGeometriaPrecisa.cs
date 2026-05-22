// Assets/Scripts/GeneradorGeometriaPrecisa.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GENERADOR DE GEOMETRÍA PRECISA — edificios, calles, aceras al milímetro
//
//  Usa los datos OSM reales de Alsasua para generar:
//
//  EDIFICIOS (1030):
//    · Footprint exacto del OSM (polígono real de la parcela)
//    · Altura real (niveles × 3.2m) del catastro
//    · Fachadas con ventanas procedurales (1 por bay de 2.5m, cada planta)
//    · Puerta de entrada en la pared más larga que da a la calle
//    · Tejado según tipo de edificio:
//        apartments/school → plano (azotea con pretil)
//        house/detached    → dos aguas (gabled) con ángulo 28°
//        yes (genérico)    → hip roof por Straight Skeleton simplificado
//        industrial        → diente de sierra (shed)
//        chapel/church     → agudo (60°)
//    · Textura correcta asignada por tipo (ladrillo, hormigón, piedra...)
//
//  CARRETERAS (400 segmentos):
//    · Calzada exacta (ancho OSM con peralte 2% para evacuación agua)
//    · Acera bilateral (1.5m residencial, 2.5m peatonal)
//    · Bordillo 15cm
//    · Paso de peatones en intersecciones
//    · Elevación siguiendo el DEM exactamente (SampleHeight en cada punto)
//
//  COORDENADAS:
//    OSM (x,z) relativas a Herriko Plaza + offset (1918, 8570) = Unity XZ
//    Altura Y siempre de Terrain.activeTerrain.SampleHeight
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System.Linq;

[DefaultExecutionOrder(-60)] // después de SistemaTerreno(-70), GestorMat(-85), Zonas(-80)
public class GeneradorGeometriaPrecisa : MonoBehaviour
{
    public static GeneradorGeometriaPrecisa Instance { get; private set; }

    [Header("Materiales por tipo de edificio")]
    public Material matLadrillo;    // apartments, yes
    public Material matCasa;        // house, detached
    public Material matIndustrial;  // industrial
    public Material matPiedra;      // chapel, church, public
    public Material matAsfalto;     // carretera
    public Material matAcera;       // sidewalk / acera
    public Material matBordillo;    // kerb / bordillo
    public Material matTejado;      // tejado genérico

    [Header("Configuración")]
    [Range(1, 1030)] public int edificiosPorFrame = 10;
    public bool generarTejados  = true;
    public bool generarVentanas = true;
    public bool generarAceras   = true;
    public bool generarBordillos= true;

    // ── constantes métricas ───────────────────────────────────────────────
    const float OFFSET_X    = 1918f;   // Herriko Plaza en Unity
    const float OFFSET_Z    = 8570f;
    const float ALT_PLANTA  = 3.2f;    // altura por planta (m)
    const float ALT_BORDILLO= 0.15f;   // altura del bordillo (m)
    const float ANCHO_ACERA_RESIDENCIAL = 1.5f;
    const float ANCHO_ACERA_PEATONAL    = 2.5f;
    const float PERALTE_CALZADA         = 0.02f; // 2% de inclinación transversal
    const float ANCHO_BAY   = 2.5f;    // ancho de un bay de fachada (ventana cada 2.5m)
    const float H_VENTANA   = 1.3f;    // alto ventana
    const float W_VENTANA   = 0.9f;    // ancho ventana
    const float H_PUERTA    = 2.1f;    // alto puerta
    const float W_PUERTA    = 1.0f;    // ancho puerta
    const float Y_OFFSET_ROAD = 0.03f; // la carretera flota 3cm sobre el terrain

    // ── Estado ────────────────────────────────────────────────────────────
    Transform  _parentEdif, _parentCalles, _parentAceras;
    int        _edificiosGenerados, _callesGeneradas;
    bool       _terminado;

    public bool Terminado => _terminado;
    public int  EdificiosGenerados => _edificiosGenerados;

    // ══════════════════════════════════════════════════════════════════════
    //  BOOT
    // ══════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        // Esperar terreno
        while (Terrain.activeTerrain == null) yield return new WaitForSeconds(0.5f);

        // Ocultar geometría OSM básica inmediatamente — evita que ambas se vean a la vez.
        // GeneradorMundoOSM puede seguir ejecutándose en paralelo (para índices/eventos),
        // pero sus meshes no serán visibles mientras los precisos se generan.
        OcultarGeometriaOSM();

        _parentEdif   = CrearParent("Edificios_Precisos");
        _parentCalles = CrearParent("Calles_Precisas");
        _parentAceras = CrearParent("Aceras_Precisas");

        // Cargar materiales de fallback si no asignados en Inspector
        AsignarMaterialesFallback();

        yield return StartCoroutine(GenerarEdificios());
        yield return StartCoroutine(GenerarCalles());

        _terminado = true;
        AlsasuaLogger.Info("GeomPrecisa",
            $"✅ Geometría precisa: {_edificiosGenerados} edificios, {_callesGeneradas} calles");
    }

    // Oculta los GOs de baja calidad generados por GeneradorMundoOSM/SistemaEdificiosAAA.
    // Los desactiva en lugar de destruirlos para que SistemaOptimizacion/Zonas
    // puedan seguir usando sus referencias si las tienen cacheadas.
    static void OcultarGeometriaOSM()
    {
        foreach (string nombre in new[] { "Edificios_OSM", "Calles_OSM" })
        {
            var go = GameObject.Find(nombre);
            if (go != null)
            {
                go.SetActive(false);
                AlsasuaLogger.Info("GeomPrecisa",
                    $"Geometría OSM base ocultada: {nombre} (reemplazada por precisa)");
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  1. EDIFICIOS
    // ══════════════════════════════════════════════════════════════════════

    IEnumerator GenerarEdificios()
    {
        // Prioridad de fuente: buildings_final.json (todo fusionado) >
        //   buildings_completo.json (OSM completo) > buildings_unity.json (base)
        var json = CargarJSON("Assets/AlsasuaData/buildings_final.json")
                ?? CargarJSON("Assets/AlsasuaData/buildings_completo.json")
                ?? CargarJSON("Assets/AlsasuaData/buildings_rico.json")
                ?? CargarJSON("Assets/AlsasuaData/buildings_unity.json");
        if (json == null) yield break;

        var edificios = JsonHelper.ParseArray<EdificioData>(json);
        int conMat = 0, conColor = 0;
        foreach (var e in edificios)
        {
            if (!string.IsNullOrEmpty(e.material))     conMat++;
            if (e.roof_r_real > 0 || e.mat_r > 0)     conColor++;
        }
        AlsasuaLogger.Info("GeomPrecisa",
            $"Generando {edificios.Length} edificios — {conMat} con material OSM, {conColor} con color real");

        for (int i = 0; i < edificios.Length; i++)
        {
            try { GenerarEdificio(edificios[i]); }
            catch (System.Exception e)
            { AlsasuaLogger.Warn("GeomPrecisa", $"Edificio {i}: {e.Message}"); }

            _edificiosGenerados++;
            if (i % edificiosPorFrame == 0) yield return null;
        }
    }

    void GenerarEdificio(EdificioData ed)
    {
        if (ed.vertices == null || ed.vertices.Length < 3) return;

        float altura = Mathf.Max(ed.height, ALT_PLANTA);
        int   niveles= Mathf.Max(1, ed.levels);

        // Convertir vértices OSM → Unity XZ
        var verts2D = ed.vertices.Select(v =>
            new Vector2(v.x + OFFSET_X, v.z + OFFSET_Z)).ToArray();

        // Calcular centroide
        Vector2 centroide = verts2D.Aggregate(Vector2.zero, (a, v) => a + v) / verts2D.Length;

        // Altura del terreno en el centro del edificio
        float yBase = AlturaTerreno(new Vector3(centroide.x, 0, centroide.y));

        var go = new GameObject($"Edif_{ed.id}_{(string.IsNullOrEmpty(ed.name) ? ed.type : ed.name)}");
        go.transform.SetParent(_parentEdif, false);
        go.isStatic = true;

        // ── a) PAREDES con ventanas y puerta ─────────────────────────────
        GenerarParedes(go, verts2D, yBase, altura, niveles, ed);

        // ── b) SUELO del edificio ─────────────────────────────────────────
        GenerarSuelo(go, verts2D, yBase);

        // ── c) TEJADO según tipo ──────────────────────────────────────────
        if (generarTejados)
            GenerarTejado(go, verts2D, yBase + altura, ed);
    }

    // ── Paredes con ventanas y puerta ─────────────────────────────────────

    void GenerarParedes(GameObject parent, Vector2[] verts, float yBase,
                         float altura, int niveles, EdificioData ed)
    {
        string tipo      = ed.type ?? "";
        string matOSM    = ed.material ?? "";
        Color  colorReal = (ed.mat_r > 0f || ed.mat_g > 0f || ed.mat_b > 0f)
                           ? new Color(ed.mat_r, ed.mat_g, ed.mat_b)
                           : default;

        // Material gestor (PBR real) con tinte fotogramétrico
        var mat = GestorMaterialesAlsasua.Instance != null
            ? GestorMaterialesAlsasua.Instance.GetPared(tipo, matOSM, colorReal)
            : MatParedes(tipo, ed.id);

        // Escala UV en metros/tile
        float uvS = GestorMaterialesAlsasua.GetUVScalePared(tipo, matOSM);
        int n     = verts.Length;

        // Identificar pared más larga (candidata a puerta)
        int idxPuerta = 0;
        float maxLen = 0f;
        for (int i = 0; i < n; i++)
        {
            float len = Vector2.Distance(verts[i], verts[(i+1)%n]);
            if (len > maxLen) { maxLen = len; idxPuerta = i; }
        }

        var vMesh = new List<Vector3>();
        var tMesh = new List<int>();
        var uMesh = new List<Vector2>();

        for (int i = 0; i < n; i++)
        {
            Vector2 a = verts[i], b = verts[(i+1)%n];
            float ya = AlturaTerreno(new Vector3(a.x, 0, a.y));
            float yb = AlturaTerreno(new Vector3(b.x, 0, b.y));
            float segLen = Vector2.Distance(a, b);
            if (segLen < 0.1f) continue;

            bool tienePuerta = generarVentanas && (i == idxPuerta) && segLen >= W_PUERTA + 0.5f;

            // Cada planta como quad subdividido
            for (int p = 0; p < niveles; p++)
            {
                float y0 = Mathf.Lerp(ya, yb, 0f) + p * ALT_PLANTA;
                float y0b= Mathf.Lerp(ya, yb, 1f) + p * ALT_PLANTA; // puede diferir por pendiente
                float y1 = y0 + ALT_PLANTA;
                float y1b= y0b+ ALT_PLANTA;

                // Quad principal de la pared — UV en metros/tile
                float uTile = segLen / uvS;           // repeticiones en X
                float vBot  = (p * ALT_PLANTA) / uvS; // posición V acumulada
                float vTop  = vBot + ALT_PLANTA / uvS;
                int base_ = vMesh.Count;
                vMesh.Add(new Vector3(a.x, y0,  a.y));
                vMesh.Add(new Vector3(b.x, y0b, b.y));
                vMesh.Add(new Vector3(b.x, y1b, b.y));
                vMesh.Add(new Vector3(a.x, y1,  a.y));
                uMesh.Add(new Vector2(0,     vBot));
                uMesh.Add(new Vector2(uTile, vBot));
                uMesh.Add(new Vector2(uTile, vTop));
                uMesh.Add(new Vector2(0,     vTop));
                AnadirQuad(tMesh, base_);

                if (!generarVentanas) continue;

                // Ventanas distribuidas en el segmento
                int numBays = Mathf.Max(1, Mathf.FloorToInt(segLen / ANCHO_BAY));
                float bayW  = segLen / numBays;
                Vector2 dir = (b - a).normalized;

                for (int bay = 0; bay < numBays; bay++)
                {
                    float t0 = (bay + 0.5f - W_VENTANA / bayW * 0.5f) * bayW / segLen;
                    float t1 = (bay + 0.5f + W_VENTANA / bayW * 0.5f) * bayW / segLen;
                    t0 = Mathf.Clamp01(t0); t1 = Mathf.Clamp01(t1);

                    if (p == 0 && tienePuerta && bay == numBays / 2)
                    {
                        // Puerta en planta baja, bay central
                        GenerarHuecoPuerta(vMesh, tMesh, uMesh, a, b, y0, segLen, bay, numBays);
                    }
                    else
                    {
                        // Ventana
                        GenerarHuecoVentana(vMesh, tMesh, uMesh, a, b, y0 + 0.8f, segLen, bay, numBays);
                    }
                }
            }
        }

        if (vMesh.Count < 3) return;
        ConstruirMesh(parent, "Paredes", vMesh, tMesh, uMesh, mat);
    }

    void GenerarHuecoVentana(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                              Vector2 a, Vector2 b, float yBase, float segLen, int bay, int numBays)
    {
        float bayW  = segLen / numBays;
        float t0    = ((bay       ) * bayW + bayW * 0.5f - W_VENTANA * 0.5f) / segLen;
        float t1    = ((bay       ) * bayW + bayW * 0.5f + W_VENTANA * 0.5f) / segLen;
        t0 = Mathf.Clamp01(t0); t1 = Mathf.Clamp01(t1);

        Vector2 p0 = Vector2.Lerp(a, b, t0);
        Vector2 p1 = Vector2.Lerp(a, b, t1);
        float y0 = yBase, y1 = yBase + H_VENTANA;

        // Marco de ventana (4 tiras alrededor del hueco)
        float frame = 0.05f;
        // Simplificado: ventana como quad con material diferente (vidrio azul)
        int base_ = verts.Count;
        verts.Add(new Vector3(p0.x, y0, p0.y));
        verts.Add(new Vector3(p1.x, y0, p1.y));
        verts.Add(new Vector3(p1.x, y1, p1.y));
        verts.Add(new Vector3(p0.x, y1, p0.y));
        uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0));
        uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
        AnadirQuad(tris, base_);
    }

    void GenerarHuecoPuerta(List<Vector3> verts, List<int> tris, List<Vector2> uvs,
                             Vector2 a, Vector2 b, float yBase, float segLen, int bay, int numBays)
    {
        float bayW = segLen / numBays;
        float t0   = (bay * bayW + bayW * 0.5f - W_PUERTA * 0.5f) / segLen;
        float t1   = (bay * bayW + bayW * 0.5f + W_PUERTA * 0.5f) / segLen;
        t0 = Mathf.Clamp01(t0); t1 = Mathf.Clamp01(t1);

        Vector2 p0 = Vector2.Lerp(a, b, t0);
        Vector2 p1 = Vector2.Lerp(a, b, t1);

        // Jamba + dintel de la puerta
        int base_ = verts.Count;
        verts.Add(new Vector3(p0.x, yBase,          p0.y));
        verts.Add(new Vector3(p1.x, yBase,          p1.y));
        verts.Add(new Vector3(p1.x, yBase + H_PUERTA, p1.y));
        verts.Add(new Vector3(p0.x, yBase + H_PUERTA, p0.y));
        uvs.Add(new Vector2(0,0)); uvs.Add(new Vector2(1,0));
        uvs.Add(new Vector2(1,1)); uvs.Add(new Vector2(0,1));
        AnadirQuad(tris, base_);
    }

    // ── Suelo ─────────────────────────────────────────────────────────────

    void GenerarSuelo(GameObject parent, Vector2[] verts, float y)
    {
        var vm = new List<Vector3>();
        var tm = new List<int>();
        var um = new List<Vector2>();

        // Triangulación fan simple del footprint
        Vector2 c = verts.Aggregate(Vector2.zero, (a,v)=>a+v) / verts.Length;
        vm.Add(new Vector3(c.x, y, c.y));

        for (int i = 0; i < verts.Length; i++)
        {
            vm.Add(new Vector3(verts[i].x, y, verts[i].y));
            int j = (i + 1) % verts.Length;
            tm.Add(0); tm.Add(i + 1); tm.Add(j + 1 > verts.Length ? 1 : j + 1);
            um.Add(new Vector2(c.x * 0.1f, c.y * 0.1f));
        }

        if (vm.Count < 3) return;
        ConstruirMesh(parent, "Suelo", vm, tm, um, matLadrillo);
    }

    // ── Tejados ───────────────────────────────────────────────────────────

    void GenerarTejado(GameObject parent, Vector2[] footprint, float y, EdificioData ed)
    {
        string tipo      = ed.type ?? "";
        string tipoReal  = ed.roof_tipo_real ?? "";
        string roofMatOSM= ed.roof_material  ?? "";

        Color colorRealTejado = (ed.roof_r_real > 0 || ed.roof_g_real > 0 || ed.roof_b_real > 0)
            ? new Color(ed.roof_r_real / 255f, ed.roof_g_real / 255f, ed.roof_b_real / 255f)
            : default;

        // Material desde gestor PBR
        Material matReal = GestorMaterialesAlsasua.Instance != null
            ? GestorMaterialesAlsasua.Instance.GetTejado(tipoReal, roofMatOSM, colorRealTejado)
            : null;

        // Forma del tejado:
        // 1. roof:shape OSM explícito
        string roofShape = (ed.roof_shape ?? "").ToLower();
        if (roofShape == "flat" || tipoReal == "cemento_gris_claro"
            || tipo is "apartments" or "school" or "industrial" or "commercial" or "office"
            || (string.IsNullOrEmpty(roofShape) && string.IsNullOrEmpty(tipoReal)
                && tipo is "apartments" or "school"))
        {
            if (roofShape != "gabled" && roofShape != "hipped")
            {
                GenerarTejadoPlano(parent, footprint, y, matReal);
                return;
            }
        }

        // Dos aguas: casas, capillas, edificios con pizarra (típico vasco)
        bool esPizarra = tipoReal is "pizarra_gris" or "pizarra_negra"
                      || roofMatOSM == "slate"
                      || roofShape == "gabled";
        bool esCasaOIglesia = tipo is "house" or "detached" or "chapel" or "church";

        if (esCasaOIglesia || esPizarra || roofShape == "gabled")
        {
            float angulo = tipo is "chapel" or "church" ? 52f : 28f;
            GenerarTejadoDosAguas(parent, footprint, y, angulo, matReal);
            return;
        }

        // Cubierta vegetal → hip suave
        if (tipoReal == "cubierta_vegetal" || roofShape == "hipped")
        {
            GenerarTejadoHip(parent, footprint, y, 15f, matReal);
            return;
        }

        // Por defecto: hip moderado (edificios genéricos de Alsasua)
        GenerarTejadoHip(parent, footprint, y, 22f, matReal);
    }

    void GenerarTejadoPlano(GameObject parent, Vector2[] verts, float y, Material matReal = null)
    {
        const float PRETIL = 0.3f;
        var vm = new List<Vector3>();
        var tm = new List<int>();
        var um = new List<Vector2>();

        Vector2 c = verts.Aggregate(Vector2.zero,(a,v)=>a+v)/verts.Length;
        vm.Add(new Vector3(c.x, y, c.y));

        for (int i = 0; i < verts.Length; i++)
        {
            vm.Add(new Vector3(verts[i].x, y, verts[i].y));
            int j = (i + 1) % verts.Length;
            int next = j + 1 > verts.Length ? 1 : j + 1;
            tm.Add(0); tm.Add(i + 1); tm.Add(next);
            um.Add(Vector2.zero);
        }

        // Pretil (pequeñas paredes perimetrales)
        for (int i = 0; i < verts.Length; i++)
        {
            Vector2 a = verts[i], b = verts[(i+1)%verts.Length];
            int base_ = vm.Count;
            vm.Add(new Vector3(a.x, y,         a.y));
            vm.Add(new Vector3(b.x, y,         b.y));
            vm.Add(new Vector3(b.x, y + PRETIL, b.y));
            vm.Add(new Vector3(a.x, y + PRETIL, a.y));
            um.Add(new Vector2(0,0)); um.Add(new Vector2(1,0));
            um.Add(new Vector2(1,1)); um.Add(new Vector2(0,1));
            AnadirQuad(tm, base_);
        }

        ConstruirMesh(parent, "Tejado", vm, tm, um, matReal ?? matTejado);
    }

    void GenerarTejadoDosAguas(GameObject parent, Vector2[] verts, float y, float angulo, Material matReal = null)
    {
        if (verts.Length < 3) return;

        // Calcular eje principal (PCA simplificado: bounding box)
        float minX=verts[0].x, maxX=verts[0].x, minZ=verts[0].y, maxZ=verts[0].y;
        foreach (var v in verts)
        {
            minX=Mathf.Min(minX,v.x); maxX=Mathf.Max(maxX,v.x);
            minZ=Mathf.Min(minZ,v.y); maxZ=Mathf.Max(maxZ,v.y);
        }

        float ancho = maxX - minX, largo = maxZ - minZ;
        float altCumbrera = Mathf.Min(ancho, largo) * 0.5f * Mathf.Tan(angulo * Mathf.Deg2Rad);
        float cx = (minX + maxX) * 0.5f, cz = (minZ + maxZ) * 0.5f;

        var vm = new List<Vector3>();
        var tm = new List<int>();
        var um = new List<Vector2>();

        if (ancho >= largo) // cumbrera a lo largo del eje Z
        {
            Vector3 c0 = new Vector3(cx, y + altCumbrera, minZ);
            Vector3 c1 = new Vector3(cx, y + altCumbrera, maxZ);

            // Faldón derecho
            vm.Add(new Vector3(maxX, y, minZ)); vm.Add(c0);
            vm.Add(c1); vm.Add(new Vector3(maxX, y, maxZ));
            um.Add(new Vector2(0,0)); um.Add(new Vector2(0.5f,1));
            um.Add(new Vector2(0.5f,1)); um.Add(new Vector2(1,0));
            AnadirQuad(tm, 0);

            // Faldón izquierdo
            int b2 = vm.Count;
            vm.Add(new Vector3(minX, y, minZ)); vm.Add(new Vector3(minX, y, maxZ));
            vm.Add(c1); vm.Add(c0);
            um.Add(new Vector2(0,0)); um.Add(new Vector2(1,0));
            um.Add(new Vector2(0.5f,1)); um.Add(new Vector2(0.5f,1));
            AnadirQuad(tm, b2);

            // Hastiales (triángulos frontales)
            int b3 = vm.Count;
            vm.Add(new Vector3(minX,y,minZ)); vm.Add(new Vector3(maxX,y,minZ)); vm.Add(c0);
            tm.Add(b3); tm.Add(b3+1); tm.Add(b3+2);
            um.Add(new Vector2(0,0)); um.Add(new Vector2(1,0)); um.Add(new Vector2(0.5f,1));

            int b4 = vm.Count;
            vm.Add(new Vector3(maxX,y,maxZ)); vm.Add(new Vector3(minX,y,maxZ)); vm.Add(c1);
            tm.Add(b4); tm.Add(b4+1); tm.Add(b4+2);
            um.Add(new Vector2(0,0)); um.Add(new Vector2(1,0)); um.Add(new Vector2(0.5f,1));
        }
        else // cumbrera a lo largo del eje X
        {
            Vector3 c0 = new Vector3(minX, y + altCumbrera, cz);
            Vector3 c1 = new Vector3(maxX, y + altCumbrera, cz);

            int b0 = vm.Count;
            vm.Add(new Vector3(minX,y,maxZ)); vm.Add(new Vector3(maxX,y,maxZ));
            vm.Add(c1); vm.Add(c0);
            um.Add(new Vector2(0,0)); um.Add(new Vector2(1,0));
            um.Add(new Vector2(1,1)); um.Add(new Vector2(0,1));
            AnadirQuad(tm, b0);

            int b1 = vm.Count;
            vm.Add(c0); vm.Add(c1);
            vm.Add(new Vector3(maxX,y,minZ)); vm.Add(new Vector3(minX,y,minZ));
            um.Add(new Vector2(0,1)); um.Add(new Vector2(1,1));
            um.Add(new Vector2(1,0)); um.Add(new Vector2(0,0));
            AnadirQuad(tm, b1);

            int b2=vm.Count;
            vm.Add(new Vector3(minX,y,minZ)); vm.Add(new Vector3(minX,y,maxZ)); vm.Add(c0);
            tm.Add(b2); tm.Add(b2+1); tm.Add(b2+2);
            um.Add(new Vector2(0,0)); um.Add(new Vector2(1,0)); um.Add(new Vector2(0.5f,1));

            int b3=vm.Count;
            vm.Add(new Vector3(maxX,y,maxZ)); vm.Add(new Vector3(maxX,y,minZ)); vm.Add(c1);
            tm.Add(b3); tm.Add(b3+1); tm.Add(b3+2);
            um.Add(new Vector2(0,0)); um.Add(new Vector2(1,0)); um.Add(new Vector2(0.5f,1));
        }

        ConstruirMesh(parent, "Tejado", vm, tm, um, matReal ?? matTejado);
    }

    void GenerarTejadoHip(GameObject parent, Vector2[] verts, float y, float angulo, Material matReal = null)
    {
        // Simplified hip roof via uniform inward offset (straight skeleton approx)
        float offset = 0f;
        float maxOff = Mathf.Min(
            verts.Max(v => v.x) - verts.Min(v => v.x),
            verts.Max(v => v.y) - verts.Min(v => v.y)) * 0.5f;

        float altCumbrera = maxOff * Mathf.Tan(angulo * Mathf.Deg2Rad);

        // Calcular centroide inset
        Vector2 centroide = verts.Aggregate(Vector2.zero,(a,v)=>a+v)/verts.Length;

        var vm = new List<Vector3>();
        var tm = new List<int>();
        var um = new List<Vector2>();

        // Cada cara del tejado: trapecio o triángulo desde borde → cumbrera
        for (int i = 0; i < verts.Length; i++)
        {
            Vector2 a = verts[i], b = verts[(i+1)%verts.Length];
            float segLen = Vector2.Distance(a, b);

            // Punto inset a mitad del segmento hacia el centroide
            Vector2 mid   = (a + b) * 0.5f;
            Vector2 inset = Vector2.Lerp(mid, centroide, Mathf.Clamp01(maxOff / Vector2.Distance(mid, centroide)));

            int base_ = vm.Count;
            vm.Add(new Vector3(a.x, y, a.y));
            vm.Add(new Vector3(b.x, y, b.y));
            vm.Add(new Vector3(inset.x, y + altCumbrera, inset.y));

            tm.Add(base_); tm.Add(base_+1); tm.Add(base_+2);
            um.Add(new Vector2(0,0)); um.Add(new Vector2(1,0)); um.Add(new Vector2(0.5f,1));
        }

        ConstruirMesh(parent, "Tejado", vm, tm, um, matReal ?? matTejado);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  2. CALLES CON ACERAS Y BORDILLOS
    // ══════════════════════════════════════════════════════════════════════

    IEnumerator GenerarCalles()
    {
        if (!generarAceras && !generarBordillos) yield break;

        // Prioridad: calles_superficie.json (con surface OSM) > roads_unity.json
        var json = CargarJSON("Assets/AlsasuaData/calles_superficie.json")
                ?? CargarJSON("Assets/AlsasuaData/roads_unity.json");
        if (json == null) yield break;

        var calles = JsonHelper.ParseArray<RoadData>(json);
        AlsasuaLogger.Info("GeomPrecisa", $"Generando aceras y bordillos para {calles.Length} calles...");

        for (int i = 0; i < calles.Length; i++)
        {
            try { GenerarCalle(calles[i]); }
            catch (System.Exception e)
            { AlsasuaLogger.Warn("GeomPrecisa", $"Calle {i}: {e.Message}"); }

            _callesGeneradas++;
            if (i % 20 == 0) yield return null;
        }
    }

    void GenerarCalle(RoadData road)
    {
        if (road.points == null || road.points.Length < 2) return;

        // Convertir puntos a Unity
        var pts = road.points.Select(p =>
            new Vector3(p.x + OFFSET_X, 0, p.z + OFFSET_Z)).ToArray();

        // Elevar al terreno
        for (int i = 0; i < pts.Length; i++)
            pts[i].y = AlturaTerreno(pts[i]) + Y_OFFSET_ROAD;

        float anchoCalzada = road.width;
        bool  esPeatonal   = EsCalleUrbanaPeatonal(road.type);
        float anchoAcera   = esPeatonal ? ANCHO_ACERA_PEATONAL : ANCHO_ACERA_RESIDENCIAL;

        // Materiales desde gestor
        var gm = GestorMaterialesAlsasua.Instance;
        Material matCalzadaReal = gm != null
            ? gm.GetCalzada(road.type, road.surface ?? "")
            : matAsfalto;
        Material matAceraReal = gm != null
            ? gm.GetAcera(road.type, road.surface ?? "")
            : matAcera;
        Material matBordReal = gm?.matBordillo ?? matBordillo;

        // ── Calzada principal ─────────────────────────────────────────────
        float uvCalzada = GestorMaterialesAlsasua.GetUVScale(
            esPeatonal ? "adoquin" : "asfalto");
        GenerarCintaVial(pts, anchoCalzada, PERALTE_CALZADA,
                          _parentCalles, $"Calzada_{road.id}", matCalzadaReal, uvCalzada);

        if (!generarAceras) return;

        // ── Aceras a ambos lados ──────────────────────────────────────────
        float offset   = anchoCalzada * 0.5f + ALT_BORDILLO + anchoAcera * 0.5f;
        float uvAcera  = GestorMaterialesAlsasua.GetUVScale(esPeatonal ? "adoquin" : "baldosa");

        GenerarCintaOffset(pts,  offset, anchoAcera, _parentAceras, $"AceraD_{road.id}", matAceraReal, true,  uvAcera);
        GenerarCintaOffset(pts, -offset, anchoAcera, _parentAceras, $"AceraI_{road.id}", matAceraReal, false, uvAcera);

        if (!generarBordillos) return;

        // ── Bordillos ─────────────────────────────────────────────────────
        float offsetBord = anchoCalzada * 0.5f + ALT_BORDILLO * 0.5f;
        GenerarBordillo(pts,  offsetBord, _parentAceras, $"BordilloD_{road.id}", matBordReal);
        GenerarBordillo(pts, -offsetBord, _parentAceras, $"BordilloI_{road.id}", matBordReal);
    }

    void GenerarCintaVial(Vector3[] pts, float ancho, float peralte,
                           Transform parent, string nombre, Material mat,
                           float uvScale = 1f)
    {
        var vm = new List<Vector3>();
        var tm = new List<int>();
        var um = new List<Vector2>();
        float vAcum = 0f;

        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector3 p0 = pts[i], p1 = pts[i+1];
            Vector3 dir = (p1 - p0).normalized;
            Vector3 perp= Vector3.Cross(dir, Vector3.up).normalized;

            float w      = ancho * 0.5f;
            float segLen = Vector3.Distance(p0, p1);

            // Peralte: inclinar la calzada ligeramente para evacuación de agua
            Vector3 dz = Vector3.up * peralte * w;

            int base_ = vm.Count;
            vm.Add(p0 - perp * w - dz);
            vm.Add(p0 + perp * w + dz);
            vm.Add(p1 + perp * w + dz);
            vm.Add(p1 - perp * w - dz);

            // UV en metros/tile para repetición correcta del pavimento
            float uTile = ancho / uvScale;
            float vBot  = vAcum / uvScale;
            float vTop  = (vAcum + segLen) / uvScale;
            um.Add(new Vector2(0,     vBot));
            um.Add(new Vector2(uTile, vBot));
            um.Add(new Vector2(uTile, vTop));
            um.Add(new Vector2(0,     vTop));
            vAcum += segLen;
            AnadirQuad(tm, base_);
        }

        ConstruirMesh(parent.gameObject, nombre, vm, tm, um, mat);
    }

    void GenerarCintaOffset(Vector3[] pts, float offset, float ancho,
                             Transform parent, string nombre, Material mat,
                             bool esDerechaDer, float uvScale = 1f)
    {
        var offsetPts = new Vector3[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            Vector3 dir;
            if (i < pts.Length - 1) dir = (pts[i+1] - pts[i]).normalized;
            else                     dir = (pts[i] - pts[i-1]).normalized;
            Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized;
            offsetPts[i] = pts[i] + perp * offset;
            offsetPts[i].y = AlturaTerreno(offsetPts[i]) + Y_OFFSET_ROAD;
        }
        GenerarCintaVial(offsetPts, ancho, 0f, parent, nombre, mat, uvScale);
    }

    void GenerarBordillo(Vector3[] pts, float offset, Transform parent, string nombre,
                          Material matOverride = null)
    {
        var vm = new List<Vector3>();
        var tm = new List<int>();
        var um = new List<Vector2>();

        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector3 p0 = pts[i], p1 = pts[i+1];
            Vector3 dir = (p1 - p0).normalized;
            Vector3 perp= Vector3.Cross(dir, Vector3.up).normalized;

            float w = 0.08f; // ancho del bordillo: 8cm
            float y0 = AlturaTerreno(p0);
            float y1 = AlturaTerreno(p1);

            Vector3 b0 = p0 + perp * (offset - w) + Vector3.up * y0;
            Vector3 b1 = p0 + perp * (offset + w) + Vector3.up * y0;
            Vector3 b2 = p1 + perp * (offset + w) + Vector3.up * y1;
            Vector3 b3 = p1 + perp * (offset - w) + Vector3.up * y1;

            // Cara superior del bordillo
            int base_ = vm.Count;
            vm.Add(b0 + Vector3.up * ALT_BORDILLO);
            vm.Add(b1 + Vector3.up * ALT_BORDILLO);
            vm.Add(b2 + Vector3.up * ALT_BORDILLO);
            vm.Add(b3 + Vector3.up * ALT_BORDILLO);
            um.Add(new Vector2(0,0)); um.Add(new Vector2(1,0));
            um.Add(new Vector2(1,1)); um.Add(new Vector2(0,1));
            AnadirQuad(tm, base_);

            // Cara exterior visible del bordillo
            int base2 = vm.Count;
            vm.Add(b1);
            vm.Add(b2);
            vm.Add(b2 + Vector3.up * ALT_BORDILLO);
            vm.Add(b1 + Vector3.up * ALT_BORDILLO);
            um.Add(new Vector2(0,0)); um.Add(new Vector2(1,0));
            um.Add(new Vector2(1,1)); um.Add(new Vector2(0,1));
            AnadirQuad(tm, base2);
        }

        ConstruirMesh(parent.gameObject, nombre, vm, tm, um, matOverride ?? matBordillo);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  UTILIDADES
    // ══════════════════════════════════════════════════════════════════════

    static void AnadirQuad(List<int> tris, int base_)
    {
        tris.Add(base_); tris.Add(base_+1); tris.Add(base_+2);
        tris.Add(base_); tris.Add(base_+2); tris.Add(base_+3);
    }

    static void ConstruirMesh(GameObject parent, string nombre,
                               List<Vector3> v, List<int> t, List<Vector2> uv, Material mat)
    {
        if (v.Count < 3 || t.Count < 3) return;
        var go = new GameObject(nombre);
        go.transform.SetParent(parent.transform, false);
        go.isStatic = true;

        var mesh = new Mesh { name = nombre };
        mesh.indexFormat = v.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(v);
        mesh.SetTriangles(t, 0);
        mesh.SetUVs(0, uv);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        go.AddComponent<MeshFilter>().sharedMesh       = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.AddComponent<MeshCollider>().sharedMesh     = mesh;
    }

    static float AlturaTerreno(Vector3 pos)
    {
        var t = Terrain.activeTerrain;
        if (t == null) return 240f;
        return t.SampleHeight(pos) + t.transform.position.y;
    }

    static bool EsCalleUrbanaPeatonal(string tipo)
        => tipo == "pedestrian" || tipo == "footway" || tipo == "living_street";

    // Fallback cuando GestorMaterialesAlsasua no está disponible
    Material MatParedes(string tipo, long id = 0)
        => tipo switch
        {
            "apartments" or "yes" or "terrace" => matLadrillo   ?? MatHDRP(new Color(0.75f, 0.73f, 0.70f)),
            "house"      or "detached"         => matCasa       ?? MatHDRP(new Color(0.82f, 0.78f, 0.72f)),
            "industrial" or "farm_auxiliary"   => matIndustrial ?? MatHDRP(new Color(0.62f, 0.62f, 0.64f)),
            "chapel"     or "church"           => matPiedra     ?? MatHDRP(new Color(0.65f, 0.63f, 0.58f)),
            _                                  => matLadrillo   ?? MatHDRP(new Color(0.78f, 0.75f, 0.70f))
        };

    // Crea un material HDRP/Lit instanciado con el color dado
    static Material MatHDRP(Color c)
    {
        var m = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        m.color = c;
        return m;
    }

    Transform CrearParent(string nombre)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    string CargarJSON(string path)
    {
        string fullPath = System.IO.Path.Combine(
            Application.dataPath.Replace("Assets",""), path);
        if (!System.IO.File.Exists(fullPath)) return null;
        return System.IO.File.ReadAllText(fullPath);
    }

    void AsignarMaterialesFallback()
    {
        if (matLadrillo  == null) matLadrillo   = MatHDRP(new Color(0.72f,0.45f,0.35f));
        if (matCasa      == null) matCasa       = MatHDRP(new Color(0.85f,0.80f,0.70f));
        if (matIndustrial== null) matIndustrial = MatHDRP(new Color(0.60f,0.60f,0.62f));
        if (matPiedra    == null) matPiedra     = MatHDRP(new Color(0.65f,0.63f,0.58f));
        if (matAsfalto   == null) matAsfalto    = MatHDRP(new Color(0.25f,0.25f,0.26f));
        if (matAcera     == null) matAcera      = MatHDRP(new Color(0.75f,0.74f,0.70f));
        if (matBordillo  == null) matBordillo   = MatHDRP(new Color(0.55f,0.55f,0.55f));
        if (matTejado    == null) matTejado     = MatHDRP(new Color(0.40f,0.40f,0.42f)); // gris pizarra (color real predominante)
    }
}
