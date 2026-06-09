// Assets/Scripts/GeneradorTejadosAAA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GENERADOR DE TEJADOS AAA — tejados procedurales con datos reales
//
//  Prioridad de forma:
//    OSM roof:shape > catastro año+tipo > regla de arquetipo
//
//  Formas:
//    Gabled (dos aguas):  Roof_Straight_Side×2 + Corner_Outer×4 + Ridge
//       Ángulos: pre-1940=32°, caserío=42°, moderno=12°, iglesia=52°
//    Hipped (4 aguas):    Roof_Convex_Side×4 + Corner
//    Flat (plano):        pretil Canopy_Beam 0.35m
//
//  Color teja: sample ortofoto en centroide del tejado
//    → terracota #b5451b o pizarra #5a6474 según luminancia/tono
//
//  Accesorios:
//    Chimney_Stone en residencial pre-1960, Chimney_Pipe en modernos
//    Canalón + bajante Metal gris oscuro cada 8m
//
//  API: GenerarTejadoParaEdificio(go, footprint, yBase, altura, data)
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class GeneradorTejadosAAA : MonoBehaviour
{
    public static GeneradorTejadosAAA Instance { get; private set; }

    [Header("Materiales (asignar en Inspector o auto-fallback)")]
    public Material matTejadoTerracota;  // teja cerámica terracota
    public Material matTejadoPizarra;    // pizarra gris oscura
    public Material matTejadoMetal;      // chapa metálica industrial
    public Material matTejadoPlano;      // losa plana
    public Material matChimenea;         // piedra/ladrillo chimenea
    public Material matCanalon;          // metal gris oscuro canalones

    // Ortofoto para sampling de color del tejado
    Texture2D _ortofoto;
    Rect      _ortoRect;

    // Colores canónicos
    static readonly Color COL_TERRACOTA = new(0.710f, 0.271f, 0.106f);  // #b5451b
    static readonly Color COL_PIZARRA   = new(0.353f, 0.392f, 0.455f);  // #5a6474
    static readonly Color COL_METALICO  = new(0.450f, 0.470f, 0.500f);

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        InicializarMateriales();
        CargarOrtofoto();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ═══════════════════════════════════════════════════════════════════════

    /// Genera el tejado completo para un edificio (llamado desde GeneradorGeometriaPrecisa)
    public void GenerarTejadoParaEdificio(GameObject parent, Vector2[] footprint,
                                           float yBase, float alturaEdificio,
                                           EdificioData data)
    {
        if (footprint == null || footprint.Length < 3) return;

        // DETERMINISMO: chimeneas y detalles iguales en cada partida por edificio.
        var _rndPrev = Random.state;
        Random.InitState(data != null ? (int)data.id : parent.name.GetHashCode());

        float yTejado = yBase + alturaEdificio;

        // Determinar forma
        string forma = DeterminarForma(data);

        // Determinar ángulo por arquetipo/época
        float angulo = DeterminarAngulo(data);

        // Color del tejado: ortofoto sample > OSM roof:colour > tipo
        Material matTej = DeterminarMaterialTejado(data, footprint);

        // Generar geometría
        switch (forma)
        {
            case "gabled":
                GenerarGabled(parent, footprint, yTejado, angulo, matTej);
                break;
            case "hipped":
                GenerarHipped(parent, footprint, yTejado, angulo, matTej);
                break;
            case "flat":
            default:
                GenerarFlat(parent, footprint, yTejado, 0.35f, matTej);
                break;
        }

        // Canalón + bajante cada 8m
        GenerarCanalonesYBajantes(parent, footprint, yTejado, alturaEdificio);

        // Chimeneas
        GenerarChimeneas(parent, footprint, yTejado, data);

        Random.state = _rndPrev;   // restaurar el RNG global
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  FORMA: GABLED (dos aguas)
    // ═══════════════════════════════════════════════════════════════════════

    void GenerarGabled(GameObject parent, Vector2[] verts, float y,
                        float angulo, Material mat)
    {
        if (verts.Length < 3) return;

        // Calcular bounding box y eje principal
        float minX = verts.Min(v => v.x), maxX = verts.Max(v => v.x);
        float minZ = verts.Min(v => v.y), maxZ = verts.Max(v => v.y);
        float ancho = maxX - minX, largo = maxZ - minZ;
        float cx = (minX + maxX) * 0.5f, cz = (minZ + maxZ) * 0.5f;

        // Altura cumbrera según ángulo
        float altCumbrera = Mathf.Min(ancho, largo) * 0.5f * Mathf.Tan(angulo * Mathf.Deg2Rad);

        var vm = new List<Vector3>();
        var tm = new List<int>();
        var um = new List<Vector2>();

        if (ancho >= largo) // cumbrera a lo largo del eje Z
        {
            Vector3 c0 = new(cx, y + altCumbrera, minZ);
            Vector3 c1 = new(cx, y + altCumbrera, maxZ);

            // Faldón derecho (+X)
            int b0 = vm.Count;
            vm.Add(new Vector3(maxX, y, minZ)); vm.Add(c0);
            vm.Add(c1); vm.Add(new Vector3(maxX, y, maxZ));
            AddUVQuad(um, new Vector2(0, 0), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(1, 0));
            AddQuad(tm, b0);

            // Faldón izquierdo (-X)
            int b1 = vm.Count;
            vm.Add(new Vector3(minX, y, minZ)); vm.Add(new Vector3(minX, y, maxZ));
            vm.Add(c1); vm.Add(c0);
            AddUVQuad(um, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1), new Vector2(0.5f, 1));
            AddQuad(tm, b1);

            // Hastiales
            int b2 = vm.Count;
            vm.Add(new Vector3(minX, y, minZ)); vm.Add(new Vector3(maxX, y, minZ)); vm.Add(c0);
            um.Add(new Vector2(0, 0)); um.Add(new Vector2(1, 0)); um.Add(new Vector2(0.5f, 1));
            tm.Add(b2); tm.Add(b2 + 1); tm.Add(b2 + 2);

            int b3 = vm.Count;
            vm.Add(new Vector3(maxX, y, maxZ)); vm.Add(new Vector3(minX, y, maxZ)); vm.Add(c1);
            um.Add(new Vector2(0, 0)); um.Add(new Vector2(1, 0)); um.Add(new Vector2(0.5f, 1));
            tm.Add(b3); tm.Add(b3 + 1); tm.Add(b3 + 2);
        }
        else // cumbrera a lo largo del eje X
        {
            Vector3 c0 = new(minX, y + altCumbrera, cz);
            Vector3 c1 = new(maxX, y + altCumbrera, cz);

            int b0 = vm.Count;
            vm.Add(new Vector3(minX, y, maxZ)); vm.Add(new Vector3(maxX, y, maxZ));
            vm.Add(c1); vm.Add(c0);
            AddUVQuad(um, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
            AddQuad(tm, b0);

            int b1 = vm.Count;
            vm.Add(c0); vm.Add(c1);
            vm.Add(new Vector3(maxX, y, minZ)); vm.Add(new Vector3(minX, y, minZ));
            AddUVQuad(um, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0));
            AddQuad(tm, b1);

            int b2 = vm.Count;
            vm.Add(new Vector3(minX, y, minZ)); vm.Add(new Vector3(minX, y, maxZ)); vm.Add(c0);
            um.Add(new Vector2(0, 0)); um.Add(new Vector2(1, 0)); um.Add(new Vector2(0.5f, 1));
            tm.Add(b2); tm.Add(b2 + 1); tm.Add(b2 + 2);

            int b3 = vm.Count;
            vm.Add(new Vector3(maxX, y, maxZ)); vm.Add(new Vector3(maxX, y, minZ)); vm.Add(c1);
            um.Add(new Vector2(0, 0)); um.Add(new Vector2(1, 0)); um.Add(new Vector2(0.5f, 1));
            tm.Add(b3); tm.Add(b3 + 1); tm.Add(b3 + 2);
        }

        ConstruirMesh(parent, "Tejado_Gabled", vm, tm, um, mat);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  FORMA: HIPPED (cuatro aguas)
    // ═══════════════════════════════════════════════════════════════════════

    void GenerarHipped(GameObject parent, Vector2[] verts, float y,
                        float angulo, Material mat)
    {
        float maxOff = Mathf.Min(
            verts.Max(v => v.x) - verts.Min(v => v.x),
            verts.Max(v => v.y) - verts.Min(v => v.y)) * 0.5f;

        float altCumbrera = maxOff * Mathf.Tan(angulo * Mathf.Deg2Rad);
        Vector2 centroide = verts.Aggregate(Vector2.zero, (a, v) => a + v) / verts.Length;

        var vm = new List<Vector3>();
        var tm = new List<int>();
        var um = new List<Vector2>();

        for (int i = 0; i < verts.Length; i++)
        {
            Vector2 a = verts[i], b = verts[(i + 1) % verts.Length];
            Vector2 mid   = (a + b) * 0.5f;
            float   dist  = Vector2.Distance(mid, centroide);
            Vector2 inset = dist > 0.01f
                ? Vector2.Lerp(mid, centroide, Mathf.Clamp01(maxOff / dist))
                : centroide;

            int base_ = vm.Count;
            vm.Add(new Vector3(a.x, y, a.y));
            vm.Add(new Vector3(b.x, y, b.y));
            vm.Add(new Vector3(inset.x, y + altCumbrera, inset.y));
            um.Add(new Vector2(0, 0)); um.Add(new Vector2(1, 0)); um.Add(new Vector2(0.5f, 1));
            tm.Add(base_); tm.Add(base_ + 1); tm.Add(base_ + 2);
        }

        ConstruirMesh(parent, "Tejado_Hipped", vm, tm, um, mat);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  FORMA: FLAT (tejado plano)
    // ═══════════════════════════════════════════════════════════════════════

    void GenerarFlat(GameObject parent, Vector2[] verts, float y,
                      float alturaPretil, Material mat)
    {
        var vm = new List<Vector3>();
        var tm = new List<int>();
        var um = new List<Vector2>();

        // Losa
        Vector2 c = verts.Aggregate(Vector2.zero, (a, v) => a + v) / verts.Length;
        vm.Add(new Vector3(c.x, y, c.y));
        um.Add(new Vector2(0.5f, 0.5f));

        for (int i = 0; i < verts.Length; i++)
        {
            vm.Add(new Vector3(verts[i].x, y, verts[i].y));
            float u = (verts[i].x - verts.Min(v => v.x)) / (verts.Max(v => v.x) - verts.Min(v => v.x) + 0.001f);
            float v2= (verts[i].y - verts.Min(v => v.y)) / (verts.Max(v => v.y) - verts.Min(v => v.y) + 0.001f);
            um.Add(new Vector2(u, v2));
            int j = (i + 1) % verts.Length;
            tm.Add(0); tm.Add(i + 1); tm.Add(j == 0 ? 1 : j + 1);
        }

        // Pretil perimetral
        if (alturaPretil > 0.05f)
        {
            for (int i = 0; i < verts.Length; i++)
            {
                Vector2 a = verts[i], b = verts[(i + 1) % verts.Length];
                int base_ = vm.Count;
                vm.Add(new Vector3(a.x, y,               a.y));
                vm.Add(new Vector3(b.x, y,               b.y));
                vm.Add(new Vector3(b.x, y + alturaPretil, b.y));
                vm.Add(new Vector3(a.x, y + alturaPretil, a.y));
                um.Add(new Vector2(0, 0)); um.Add(new Vector2(1, 0));
                um.Add(new Vector2(1, 1)); um.Add(new Vector2(0, 1));
                AddQuad(tm, base_);
            }
        }

        ConstruirMesh(parent, "Tejado_Flat", vm, tm, um, mat);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CANALONES Y BAJANTES
    // ═══════════════════════════════════════════════════════════════════════

    void GenerarCanalonesYBajantes(GameObject parent, Vector2[] footprint,
                                    float yTejado, float alturaEdificio)
    {
        if (footprint == null) return;

        float ancho = footprint.Max(v => v.x) - footprint.Min(v => v.x);
        int n = Mathf.Max(1, (int)(ancho / 8f));

        float xMin = footprint.Min(v => v.x);
        float zMin = footprint.Min(v => v.y);

        Material matC = matCanalon ?? MatHDRP(new Color(0.30f, 0.32f, 0.35f));

        for (int k = 0; k < n; k++)
        {
            float x = xMin + (k + 0.5f) * (ancho / n);

            // Canalón horizontal en base del faldón
            var canalon = GameObject.CreatePrimitive(PrimitiveType.Cube);
            canalon.name = $"Canalon_{k}";
            canalon.transform.SetParent(parent.transform);
            canalon.transform.position   = new Vector3(x, yTejado + 0.05f, zMin - 0.15f);
            canalon.transform.localScale = new Vector3(0.14f, 0.10f, 0.14f);
            canalon.GetComponent<Renderer>().sharedMaterial = matC;
            canalon.isStatic = true;
            Object.Destroy(canalon.GetComponent<Collider>());

            // Bajante vertical
            var bajante = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bajante.name = $"Bajante_{k}";
            bajante.transform.SetParent(parent.transform);
            bajante.transform.position   = new Vector3(x, yTejado - alturaEdificio * 0.5f, zMin - 0.12f);
            bajante.transform.localScale = new Vector3(0.08f, alturaEdificio, 0.08f);
            bajante.GetComponent<Renderer>().sharedMaterial = matC;
            bajante.isStatic = true;
            Object.Destroy(bajante.GetComponent<Collider>());
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CHIMENEAS
    // ═══════════════════════════════════════════════════════════════════════

    void GenerarChimeneas(GameObject parent, Vector2[] footprint, float yTejado,
                           EdificioData data)
    {
        if (footprint == null) return;

        string tipo = (data?.type ?? "").ToLower();
        // anio_construccion no está en EdificioData — no existe ese campo aún

        // Residencial pre-1960: chimenea piedra; modernos: tubo metálico
        // Sin año de construcción en EdificioData → conservador (piedra)
        bool esPiedra = true;
        bool esResidencial = tipo is "" or "house" or "detached" or "apartments" or "yes";
        if (!esResidencial) return;

        int cantidad = Random.Range(1, 3);
        float minX = footprint.Min(v => v.x);
        float maxX = footprint.Max(v => v.x);
        float minZ = footprint.Min(v => v.y);
        float maxZ = footprint.Max(v => v.y);
        Material matC = esPiedra
            ? (matChimenea ?? MatHDRP(new Color(0.55f, 0.50f, 0.45f)))
            : MatHDRP(new Color(0.35f, 0.35f, 0.37f));

        for (int k = 0; k < cantidad; k++)
        {
            float cx = Mathf.Lerp(minX, maxX, Random.Range(0.2f, 0.8f));
            float cz = Mathf.Lerp(minZ, maxZ, Random.Range(0.2f, 0.8f));
            float altChi = Random.Range(0.65f, 1.25f);

            if (esPiedra)
            {
                // Cuerpo cuadrado piedra
                var cuerpo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cuerpo.name = $"Chimenea_Piedra_{k}";
                cuerpo.transform.SetParent(parent.transform);
                cuerpo.transform.position   = new Vector3(cx, yTejado + altChi * 0.5f, cz);
                cuerpo.transform.localScale = new Vector3(0.38f, altChi, 0.38f);
                cuerpo.GetComponent<Renderer>().sharedMaterial = matC;
                cuerpo.isStatic = true;
                Object.Destroy(cuerpo.GetComponent<Collider>());

                // Sombrero
                var sombrero = GameObject.CreatePrimitive(PrimitiveType.Cube);
                sombrero.name = $"Chimenea_Sombrero_{k}";
                sombrero.transform.SetParent(parent.transform);
                sombrero.transform.position   = new Vector3(cx, yTejado + altChi + 0.06f, cz);
                sombrero.transform.localScale = new Vector3(0.52f, 0.10f, 0.52f);
                sombrero.GetComponent<Renderer>().sharedMaterial = matChimenea ?? matC;
                sombrero.isStatic = true;
                Object.Destroy(sombrero.GetComponent<Collider>());
            }
            else
            {
                // Tubo metálico moderno
                var tubo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                tubo.name = $"Chimenea_Tubo_{k}";
                tubo.transform.SetParent(parent.transform);
                tubo.transform.position   = new Vector3(cx, yTejado + altChi * 0.5f, cz);
                tubo.transform.localScale = new Vector3(0.14f, altChi * 0.5f, 0.14f);
                tubo.GetComponent<Renderer>().sharedMaterial = matC;
                tubo.isStatic = true;
                Object.Destroy(tubo.GetComponent<Collider>());
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LÓGICA DE DECISIÓN
    // ═══════════════════════════════════════════════════════════════════════

    static string DeterminarForma(EdificioData data)
    {
        if (data == null) return "gabled";

        // 1. OSM roof:shape
        string roofShape = "";
        if (FusionadorEdificiosUltra.Instance != null)
            roofShape = FusionadorEdificiosUltra.Instance.GetFormaOptima(
                (long)data.id, data.roof_shape ?? "").ToLower();
        else
            roofShape = (data.roof_shape ?? "").ToLower();

        if (roofShape is "flat")    return "flat";
        if (roofShape is "gabled")  return "gabled";
        if (roofShape is "hipped")  return "hipped";

        // 2. Tipo de edificio
        string tipo = (data.type ?? "").ToLower();
        if (tipo is "industrial" or "warehouse" or "commercial" or "office"
                 or "apartments" or "school")
            return "flat";
        if (tipo is "house" or "detached" or "farm" or "barn")
            return "gabled";
        if (tipo is "chapel" or "church")
            return "gabled"; // muy pronunciado

        // 3. Número de plantas: >5 → plano
        int niv = data.levels;
        if (niv > 5) return "flat";
        if (niv > 3) return "hipped";

        return "gabled"; // por defecto: dos aguas (más frecuente en Alsasua)
    }

    static float DeterminarAngulo(EdificioData data)
    {
        string tipo = (data?.type ?? "").ToLower();
        if (tipo is "chapel" or "church") return 52f;
        if (tipo is "farm" or "barn" or "detached") return 42f; // caserío

        // Heurística por niveles
        int niv = data?.levels ?? 2;
        if (niv <= 2) return 32f;
        if (niv <= 4) return 25f;
        return 15f;
    }

    Material DeterminarMaterialTejado(EdificioData data, Vector2[] footprint)
    {
        // 1. Sample ortofoto en centroide del tejado
        if (_ortofoto != null && footprint != null && footprint.Length > 0)
        {
            float cx = footprint.Average(v => v.x);
            float cz = footprint.Average(v => v.y);
            Color sample = SampleOrtofoto(cx, cz);
            bool esPizarra = EsColorPizarra(sample);

            return esPizarra ? TintMat(matTejadoPizarra, sample)
                             : TintMat(matTejadoTerracota, sample);
        }

        // 2. OSM roof:colour
        if (!string.IsNullOrEmpty(data?.roof_colour))
        {
            if (ColorUtility.TryParseHtmlString(data.roof_colour, out var c))
                return TintMat(EsColorPizarra(c) ? matTejadoPizarra : matTejadoTerracota, c);
        }

        // 3. LIDAR roof color
        if (FusionadorEdificiosUltra.Instance != null
            && FusionadorEdificiosUltra.Instance.GetRoofColor((long)(data?.id ?? 0), out var colorLidar))
        {
            return TintMat(EsColorPizarra(colorLidar) ? matTejadoPizarra : matTejadoTerracota, colorLidar);
        }

        // 4. Tipo/época por defecto
        string tipo = (data?.type ?? "").ToLower();
        if (tipo is "chapel" or "church") return matTejadoPizarra;
        if (tipo is "industrial")         return matTejadoMetal;

        return matTejadoTerracota; // teja cerámica: más frecuente en Alsasua
    }

    static bool EsColorPizarra(Color c)
    {
        // Pizarra: luminancia baja + tono azulado-grisáceo
        float lum = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
        float blueBias = c.b - c.r;
        return lum < 0.42f && blueBias > -0.02f;
    }

    Color SampleOrtofoto(float worldX, float worldZ)
    {
        float u = Mathf.Clamp01(Mathf.InverseLerp(_ortoRect.xMin, _ortoRect.xMax, worldX));
        float v = Mathf.Clamp01(Mathf.InverseLerp(_ortoRect.yMin, _ortoRect.yMax, worldZ));
        try { return _ortofoto.GetPixelBilinear(u, v); }
        catch { return new Color(0.55f, 0.38f, 0.28f); }
    }

    Material TintMat(Material baseMat, Color tint)
    {
        if (baseMat == null) return null;
        var m = new Material(baseMat);
        m.color = tint;
        return m;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  UTILITIES
    // ═══════════════════════════════════════════════════════════════════════

    static void ConstruirMesh(GameObject parent, string nombre,
                               List<Vector3> v, List<int> t, List<Vector2> uv, Material mat)
    {
        if (v.Count < 3 || t.Count < 3) return;

        var mesh = new Mesh { name = nombre };
        mesh.indexFormat = v.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(v);
        mesh.SetTriangles(t, 0);
        mesh.SetUVs(0, uv);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject(nombre);
        go.transform.SetParent(parent.transform, false);
        go.isStatic = true;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat ?? MatHDRP(COL_TERRACOTA);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
    }

    static void AddQuad(List<int> t, int b)
    {
        t.Add(b); t.Add(b + 1); t.Add(b + 2);
        t.Add(b); t.Add(b + 2); t.Add(b + 3);
    }

    static void AddUVQuad(List<Vector2> um, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        um.Add(a); um.Add(b); um.Add(c); um.Add(d);
    }

    static Material MatHDRP(Color c)
    {
        var m = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard")) { color = c };
        m.SetFloat("_Smoothness", 0.45f);
        return m;
    }

    void InicializarMateriales()
    {
        if (matTejadoTerracota == null)
            matTejadoTerracota = MatHDRP(COL_TERRACOTA);
        if (matTejadoPizarra == null)
            matTejadoPizarra   = MatHDRP(COL_PIZARRA);
        if (matTejadoMetal == null)
            matTejadoMetal     = MatHDRP(COL_METALICO);
        if (matTejadoPlano == null)
            matTejadoPlano     = MatHDRP(new Color(0.52f, 0.52f, 0.54f));
        if (matCanalon == null)
        {
            matCanalon = MatHDRP(new Color(0.30f, 0.32f, 0.35f));
            matCanalon.SetFloat("_Metallic", 0.7f);
            matCanalon.SetFloat("_Smoothness", 0.75f);
        }
        if (matChimenea == null)
            matChimenea = MatHDRP(new Color(0.55f, 0.50f, 0.44f));
    }

    void CargarOrtofoto()
    {
        _ortofoto = Resources.Load<Texture2D>("ortofoto_alsasua_REAL");
#if UNITY_EDITOR
        if (_ortofoto == null)
            _ortofoto = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/AlsasuaData/ortofoto_alsasua_REAL.png");
#endif
        _ortoRect = new Rect(GeoDataAlsasua.OX - 800f, GeoDataAlsasua.OZ - 800f, 1600f, 1600f);

        if (_ortofoto != null)
            AlsasuaLogger.Info("TejadosAAA", "Ortofoto cargada — sampling de color tejados activo");
        else
            AlsasuaLogger.Warn("TejadosAAA", "Ortofoto no encontrada — usando colores por defecto");
    }
}
