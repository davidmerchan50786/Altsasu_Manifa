// Assets/Scripts/SistemaTuneles.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA TÚNELES DE AUTOVÍA  (A-1 / N-1 a su paso por Alsasua)
//
//  Construye tramos de túnel viario con sección en arco: dos hastiales,
//  bóveda curva, portales de embocadura, iluminación de sodio (naranja),
//  bordillos y línea central. El jugador/los coches pueden atravesarlo.
//
//  Antes de esto no existía ningún sistema de túneles.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

[DefaultExecutionOrder(-18)]
public class SistemaTuneles : MonoBehaviour
{
    public static SistemaTuneles Instance { get; private set; }

    [Header("Trazado")]
    [Tooltip("Dirección de la autovía en el plano (se normaliza).")]
    public Vector3 direccionAutovia = new(1f, 0f, -0.25f);
    [Tooltip("Longitud de cada túnel (m).")]
    public float longitudTunel = 120f;
    [Tooltip("Ancho interior de calzada (m).")]
    public float anchoCalzada = 9f;
    [Tooltip("Altura libre de la bóveda (m).")]
    public float alturaLibre = 6.5f;

    // Posiciones base de cada boca de túnel (offset en metros respecto a CarreteraN1)
    static readonly Vector2[] TUNELES =
    {
        new(-260f, -60f),
        new( 180f,  40f),
    };

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        Vector3 dir = direccionAutovia; dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) dir = Vector3.right;
        dir.Normalize();

        int n = 0;
        foreach (var off in TUNELES)
        {
            Vector3 baseN1 = GeoDataAlsasua.CarreteraN1;
            float x = baseN1.x + off.x, z = baseN1.z + off.y;
            float y = GeoDataAlsasua.AlturaTerreno(x, z);
            ConstruirTunel(new Vector3(x, y, z), dir, n++);
        }
        AlsasuaLogger.Info("Tuneles", $"{n} túneles de autovía construidos.");
    }

    void ConstruirTunel(Vector3 centro, Vector3 dir, int idx)
    {
        var raiz = new GameObject($"Tunel_{idx}").transform;
        raiz.SetParent(transform, false);
        raiz.position = centro;
        raiz.rotation = Quaternion.LookRotation(dir, Vector3.up);

        // Materiales PBR reales (Poly Haven CC0) con respaldo a color plano
        var matHormigon = TexturasVivo.Hormigon ?? Mat(new Color(0.52f, 0.52f, 0.50f), 0.25f, 0f);
        var matBoveda   = TexturasVivo.Hormigon ?? Mat(new Color(0.40f, 0.40f, 0.42f), 0.15f, 0f);
        var matAsfalto  = TexturasVivo.Asfalto  ?? Mat(new Color(0.10f, 0.10f, 0.11f), 0.30f, 0f);
        var matLinea    = Mat(new Color(0.85f, 0.80f, 0.20f), 0.2f, 0f);
        var matPortal   = TexturasVivo.Hormigon ?? Mat(new Color(0.34f, 0.33f, 0.32f), 0.2f, 0f);

        float L = longitudTunel;
        float w = anchoCalzada;
        float h = alturaLibre;
        float grosor = 0.6f;

        // ── Calzada (asfalto) ────────────────────────────────────────────────
        var via = Box(raiz, "Calzada", new Vector3(0, 0.05f, 0),
                      new Vector3(w, 0.1f, L), matAsfalto);

        // Línea central discontinua
        int rayas = Mathf.Max(1, Mathf.RoundToInt(L / 6f));
        for (int i = 0; i < rayas; i++)
        {
            float zz = -L * 0.5f + (i + 0.5f) * (L / rayas);
            Box(raiz, "Raya", new Vector3(0, 0.11f, zz), new Vector3(0.18f, 0.02f, 2.4f), matLinea);
        }

        // ── Hastiales (muros laterales) ──────────────────────────────────────
        for (int lado = -1; lado <= 1; lado += 2)
        {
            Box(raiz, lado < 0 ? "Muro_Izq" : "Muro_Der",
                new Vector3(lado * (w * 0.5f + grosor * 0.5f), h * 0.5f, 0),
                new Vector3(grosor, h, L), matHormigon);

            // Bordillo / acera de servicio
            Box(raiz, "Bordillo",
                new Vector3(lado * (w * 0.5f - 0.4f), 0.25f, 0),
                new Vector3(0.8f, 0.4f, L), matHormigon);
        }

        // ── Bóveda en arco (anillos de cilindro aplanado) ────────────────────
        int anillos = Mathf.Max(1, Mathf.RoundToInt(L / 4f));
        var bovedaParent = new GameObject("Boveda").transform;
        bovedaParent.SetParent(raiz, false);
        for (int i = 0; i <= anillos; i++)
        {
            float zz = -L * 0.5f + i * (L / anillos);
            var arco = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arco.name = "Arco";
            arco.transform.SetParent(bovedaParent, false);
            // Cilindro tumbado a lo ancho formando el techo curvo
            arco.transform.localPosition = new Vector3(0, h, zz);
            arco.transform.localRotation = Quaternion.Euler(0, 0, 90f);
            arco.transform.localScale    = new Vector3(w * 0.62f, (L / anillos) * 0.55f, w * 0.62f);
            arco.GetComponent<Renderer>().sharedMaterial = matBoveda;
            Destroy(arco.GetComponent<Collider>());
        }
        // Techo plano de cierre por encima del arco (tapa la luz exterior)
        Box(raiz, "TechoCierre", new Vector3(0, h + 0.4f, 0), new Vector3(w + grosor * 2f, 0.5f, L), matHormigon);

        // ── Portales de embocadura ───────────────────────────────────────────
        for (int e = -1; e <= 1; e += 2)
        {
            float zz = e * L * 0.5f;
            // Marco grueso alrededor de la boca
            Box(raiz, "Portal_Top", new Vector3(0, h + 0.9f, zz), new Vector3(w + 2.4f, 1.6f, 1.2f), matPortal);
            Box(raiz, "Portal_L",   new Vector3(-(w * 0.5f + 1.1f), h * 0.5f + 0.5f, zz), new Vector3(1.4f, h + 1.5f, 1.2f), matPortal);
            Box(raiz, "Portal_R",   new Vector3( (w * 0.5f + 1.1f), h * 0.5f + 0.5f, zz), new Vector3(1.4f, h + 1.5f, 1.2f), matPortal);
        }

        // ── Iluminación interior (sodio naranja) ─────────────────────────────
        int luces = Mathf.Max(1, Mathf.RoundToInt(L / 14f));
        for (int i = 0; i < luces; i++)
        {
            float zz = -L * 0.5f + (i + 0.5f) * (L / luces);
            var lGO = new GameObject("Luz_Tunel");
            lGO.transform.SetParent(raiz, false);
            lGO.transform.localPosition = new Vector3(0, h - 0.6f, zz);
            var luz = lGO.AddComponent<Light>();
            luz.type = LightType.Point;
            luz.range = 22f;
            luz.intensity = 2.2f;
            luz.color = new Color(1f, 0.66f, 0.32f);   // vapor de sodio
            // Carcasa de la luminaria
            Box(raiz, "Luminaria", new Vector3(0, h - 0.35f, zz), new Vector3(1.4f, 0.18f, 0.5f),
                Mat(new Color(0.9f, 0.85f, 0.6f), 0.3f, 0.2f));
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    static GameObject Box(Transform padre, string nombre, Vector3 localPos, Vector3 escala, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = nombre;
        go.transform.SetParent(padre, false);
        go.transform.localPosition = localPos;
        go.transform.localScale    = escala;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Material Mat(Color c, float smoothness, float metallic)
    {
        var m = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", metallic);
        return m;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
