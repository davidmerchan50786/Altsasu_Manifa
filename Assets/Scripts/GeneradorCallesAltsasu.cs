// Assets/Scripts/GeneradorCallesAltsasu.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GENERADOR DE CALLES — ALTSASU (proyecto CloudCompare / HDRP)
//
//  Genera los meshes de carretera usando coordenadas GPS reales de Altsasu
//  convertidas al espacio Unity del escenario CloudCompare.
//
//  El origen (0,0,0) corresponde a la Herriko Plaza / Plaza de los Fueros:
//    Lat: 42.9016 N    Lon: -2.1668 W
//
//  Para añadir calles: agrega entradas en el array CALLES_ALTSASU.
//  Cada punto es Vector3(metros_Este, 0, metros_Norte) relativo al origen.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[AddComponentMenu("Altsasu/Generador Calles Altsasu")]
public sealed class GeneradorCallesAltsasu : MonoBehaviour
{
    // ── Datos de calles hardcodeados (coordenadas métricas desde Herriko Plaza) ──
    // +X = Este, +Z = Norte, misma convención que Alsasua_Simulator
    static readonly CalleAltsasu[] CALLES_ALTSASU =
    {
        new CalleAltsasu("Nafarroa Kalea / C. Navarra",     7f, TipoVia.Primaria, new Vector3[]
        {
            new Vector3(-122f, 0f, -360f),
            new Vector3(-118f, 0f, -180f),
            new Vector3(-110f, 0f,    0f),
            new Vector3(-105f, 0f,  190f),
            new Vector3(-100f, 0f,  310f),
        }),
        new CalleAltsasu("Erdikale / Calle Central",        4f, TipoVia.Secundaria, new Vector3[]
        {
            new Vector3( -75f, 0f, -120f),
            new Vector3( -95f, 0f,  -60f),
            new Vector3(-110f, 0f,    0f),
            new Vector3(-130f, 0f,   50f),
            new Vector3(-120f, 0f,  120f),
        }),
        new CalleAltsasu("N-1 (tramo urbano norte)",       10f, TipoVia.Autovia, new Vector3[]
        {
            new Vector3(100f, 0f, -900f),
            new Vector3( 90f, 0f, -500f),
            new Vector3( 80f, 0f, -200f),
            new Vector3( 70f, 0f,    0f),
            new Vector3( 60f, 0f,  250f),
            new Vector3( 50f, 0f,  600f),
            new Vector3( 40f, 0f,  900f),
        }),
        new CalleAltsasu("Avenida de Pamplona",             8f, TipoVia.Primaria, new Vector3[]
        {
            new Vector3(-200f, 0f, -80f),
            new Vector3(-150f, 0f, -70f),
            new Vector3(-110f, 0f,   0f),
            new Vector3( -60f, 0f,  15f),
            new Vector3(   0f, 0f,  20f),
            new Vector3(  80f, 0f,  30f),
        }),
        new CalleAltsasu("Brentana Kalea",                  5f, TipoVia.Secundaria, new Vector3[]
        {
            new Vector3( 80f, 0f,  50f),
            new Vector3( 85f, 0f, 150f),
            new Vector3( 90f, 0f, 280f),
        }),
        new CalleAltsasu("Inurritza Kalea",                 5f, TipoVia.Secundaria, new Vector3[]
        {
            new Vector3( 20f, 0f, -350f),
            new Vector3( 25f, 0f, -220f),
            new Vector3( 30f, 0f,  -80f),
        }),
        new CalleAltsasu("Carretera Etxarri (NA-120)",     9f, TipoVia.Autovia, new Vector3[]
        {
            new Vector3(-400f, 0f, -100f),
            new Vector3(-300f, 0f,  -80f),
            new Vector3(-200f, 0f,  -60f),
            new Vector3(-110f, 0f,    0f),
        }),
        new CalleAltsasu("Calle Zapatería",                 3.5f, TipoVia.Peatonal, new Vector3[]
        {
            new Vector3(-90f, 0f, -30f),
            new Vector3(-95f, 0f,  20f),
            new Vector3(-85f, 0f,  60f),
        }),
    };

    // ── Inspector ────────────────────────────────────────────────────────
    [Header("Materiales HDRP")]
    public Material materialAsfalto;
    public Material materialArcen;
    public Material materialLineas;
    public Material materialPeatonal; // color diferente para calles peatonales

    [Header("Ajuste al terreno CloudCompare")]
    [Tooltip("Offset de altura sobre el mesh de CloudCompare (evita z-fighting).")]
    public float offsetAltura = 0.08f;
    [Tooltip("LayerMask del terreno CloudCompare para el raycast de altura.")]
    public LayerMask capasTerreno = ~0;

    [Header("Dimensiones")]
    public float anchoArcen         = 0.9f;
    public float altoBordillo       = 0.15f;
    public float anchoLineaCentral  = 0.14f;
    public float longSegDiscontínuo = 3f;
    public float espacioDiscontínuo = 4f;

    [Header("UV / Tiling")]
    public float tilingLong = 6f;

    // ── Internos ─────────────────────────────────────────────────────────
    private const float RAYCAST_ALTO = 500f;

    void Start()
    {
        if (transform.childCount == 0) GenerarTodas();
    }

    public void GenerarTodas()
    {
        // Limpiar anterior
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var hijo = transform.GetChild(i);
#if UNITY_EDITOR
            DestroyImmediate(hijo.gameObject);
#else
            Destroy(hijo.gameObject);
#endif
        }

        CrearMaterialesDefecto();
        int n = 0;
        foreach (var calle in CALLES_ALTSASU)
        {
            GenerarCalle(calle);
            n++;
        }
        Debug.Log($"[GeneradorCallesAltsasu] {n} calles generadas sobre terreno CloudCompare.");
    }

    void GenerarCalle(CalleAltsasu c)
    {
        var raiz = new GameObject(c.Nombre);
        raiz.transform.SetParent(transform);

        Material matSuelo = c.Tipo == TipoVia.Peatonal
            ? (materialPeatonal != null ? materialPeatonal : materialArcen)
            : materialAsfalto;

        // Calzada
        var mCalzada = BuildBanda(c.Waypoints, c.Ancho, offsetAltura, 0f);
        AddMeshChild(raiz.transform, "Calzada", mCalzada, matSuelo);

        // Arcenes (no en peatonales)
        if (c.Tipo != TipoVia.Peatonal && anchoArcen > 0f)
        {
            var mArI = BuildBanda(c.Waypoints, anchoArcen, offsetAltura + altoBordillo, -(c.Ancho * .5f + anchoArcen * .5f));
            var mArD = BuildBanda(c.Waypoints, anchoArcen, offsetAltura + altoBordillo,  (c.Ancho * .5f + anchoArcen * .5f));
            AddMeshChild(raiz.transform, "Arcen_Izq", mArI, materialArcen);
            AddMeshChild(raiz.transform, "Arcen_Der", mArD, materialArcen);
        }

        // Líneas (solo calzadas, no peatonal)
        if (c.Tipo != TipoVia.Peatonal)
        {
            var mLineas = BuildLineas(c.Waypoints, c.Ancho, c.Tipo == TipoVia.Autovia);
            if (mLineas != null) AddMeshChild(raiz.transform, "Lineas", mLineas, materialLineas);
        }

#if UNITY_EDITOR
        foreach (var t in raiz.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                StaticEditorFlags.ContributeGI |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.BatchingStatic);
#endif
    }

    // ─── Mesh banda ───────────────────────────────────────────────────────

    Mesh BuildBanda(Vector3[] pts, float ancho, float elevacion, float lateralOffset)
    {
        var v = new List<Vector3>();
        var u = new List<Vector2>();
        var t = new List<int>();
        float dist = 0f;

        for (int i = 0; i < pts.Length; i++)
        {
            Vector3 fwd   = GetFwd(pts, i);
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            Vector3 c     = pts[i] + right * lateralOffset;
            c.y = SampleY(c, elevacion);

            if (i > 0) dist += Vector3.Distance(pts[i], pts[i - 1]);
            float pu = dist / tilingLong;

            v.Add(c - right * (ancho * .5f));
            v.Add(c + right * (ancho * .5f));
            u.Add(new Vector2(0f, pu));
            u.Add(new Vector2(1f, pu));

            if (i > 0)
            {
                int b = (i - 1) * 2, tt = i * 2;
                t.Add(b); t.Add(tt); t.Add(b + 1);
                t.Add(tt); t.Add(tt + 1); t.Add(b + 1);
            }
        }

        var mesh = new Mesh { name = "Banda" };
        mesh.SetVertices(v); mesh.SetUVs(0, u); mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
        return mesh;
    }

    Mesh BuildLineas(Vector3[] pts, float anchoCalzada, bool esAutovia)
    {
        var v = new List<Vector3>(); var u = new List<Vector2>();
        var t = new List<int>(); var n = new List<Vector3>();
        float dist = 0f; bool on = true;

        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector3 p0 = pts[i], p1 = pts[i + 1];
            float   len = Vector3.Distance(p0, p1);
            Vector3 dir = (p1 - p0).normalized;
            Vector3 r   = Vector3.Cross(Vector3.up, dir).normalized;
            float avz = 0f;

            while (avz < len)
            {
                float periodo = on ? longSegDiscontínuo : espacioDiscontínuo;
                float hasta = Mathf.Min(avz + periodo, len);
                if (on)
                {
                    Vector3 a = p0 + dir * avz;  a.y = SampleY(a, offsetAltura + 0.006f);
                    Vector3 b = p0 + dir * hasta; b.y = SampleY(b, offsetAltura + 0.006f);
                    int idx = v.Count;
                    v.Add(a - r * (anchoLineaCentral * .5f)); v.Add(a + r * (anchoLineaCentral * .5f));
                    v.Add(b - r * (anchoLineaCentral * .5f)); v.Add(b + r * (anchoLineaCentral * .5f));
                    float u0 = (dist + avz) / tilingLong, u1 = (dist + hasta) / tilingLong;
                    u.Add(new Vector2(0,u0)); u.Add(new Vector2(1,u0));
                    u.Add(new Vector2(0,u1)); u.Add(new Vector2(1,u1));
                    for (int k=0;k<4;k++) n.Add(Vector3.up);
                    t.Add(idx); t.Add(idx+2); t.Add(idx+1);
                    t.Add(idx+1); t.Add(idx+2); t.Add(idx+3);
                }
                avz += periodo; on = !on;
            }
            dist += len;
        }

        if (v.Count == 0) return null;
        var mesh = new Mesh { name = "Lineas" };
        mesh.SetVertices(v); mesh.SetUVs(0, u); mesh.SetNormals(n); mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds(); return mesh;
    }

    void AddMeshChild(Transform padre, string nombre, Mesh mesh, Material mat)
    {
        if (mesh == null) return;
        var go = new GameObject(nombre);
        go.transform.SetParent(padre);
        go.transform.localPosition = Vector3.zero;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
    }

    float SampleY(Vector3 p, float offset)
    {
        var ray = new Ray(p + Vector3.up * RAYCAST_ALTO, Vector3.down);
        if (Physics.Raycast(ray, out var hit, RAYCAST_ALTO * 2f, capasTerreno))
            return hit.point.y + offset;
        return offset;
    }

    static Vector3 GetFwd(Vector3[] pts, int i)
    {
        if (i < pts.Length - 1) return (pts[i + 1] - pts[i]).normalized;
        if (i > 0)              return (pts[i] - pts[i - 1]).normalized;
        return Vector3.forward;
    }

    void CrearMaterialesDefecto()
    {
        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        if (materialAsfalto == null)
        {
            materialAsfalto = new Material(shader) { name = "M_Asfalto" };
            materialAsfalto.color = new Color(0.10f, 0.10f, 0.10f);
        }
        if (materialArcen == null)
        {
            materialArcen = new Material(shader) { name = "M_Arcen" };
            materialArcen.color = new Color(0.42f, 0.40f, 0.37f);
        }
        if (materialLineas == null)
        {
            materialLineas = new Material(shader) { name = "M_Lineas" };
            materialLineas.color = new Color(0.95f, 0.95f, 0.80f);
        }
        if (materialPeatonal == null)
        {
            materialPeatonal = new Material(shader) { name = "M_Peatonal" };
            materialPeatonal.color = new Color(0.55f, 0.52f, 0.47f);
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Regenerar Calles")]
    void Regenerar() => GenerarTodas();

    void OnDrawGizmosSelected()
    {
        foreach (var c in CALLES_ALTSASU)
        {
            Gizmos.color = c.Tipo == TipoVia.Autovia ? Color.red
                         : c.Tipo == TipoVia.Primaria ? Color.yellow
                         : c.Tipo == TipoVia.Peatonal ? Color.green
                         : Color.cyan;
            for (int i = 0; i < c.Waypoints.Length - 1; i++)
                Gizmos.DrawLine(c.Waypoints[i] + Vector3.up, c.Waypoints[i+1] + Vector3.up);
        }
    }
#endif

    // ── Tipos internos ───────────────────────────────────────────────────
    enum TipoVia { Autovia, Primaria, Secundaria, Peatonal }

    struct CalleAltsasu
    {
        public string    Nombre;
        public float     Ancho;
        public TipoVia   Tipo;
        public Vector3[] Waypoints;

        public CalleAltsasu(string n, float a, TipoVia t, Vector3[] w)
        { Nombre = n; Ancho = a; Tipo = t; Waypoints = w; }
    }
}
