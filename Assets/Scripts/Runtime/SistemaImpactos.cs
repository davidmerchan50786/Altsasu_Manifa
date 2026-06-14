// Assets/Scripts/SistemaImpactos.cs
// ═══════════════════════════════════════════════════════════════════════════
//  IMPACTOS POR MATERIAL — partículas y marcas de bala AAA
//
//  Detecta el material del objeto impactado y genera:
//    Metal     → chispas naranjas + decal negro
//    Hormigón  → polvo gris + fragmentos
//    Madera    → astillas marrón + decal
//    Tierra    → polvo beige + cratera
//    Cristal   → fragmentos blancos + rotura procedural
//    Carne     → partículas rojas (enemigos)
//    Asfalto   → polvo gris oscuro + decal
//
//  Uso: SistemaImpactos.Impacto(hit, tipoArma);
//       SistemaImpactos.Explosion(pos, radio);
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;

public class SistemaImpactos : MonoBehaviour
{
    public static SistemaImpactos I { get; private set; }

    // ── Pool de partículas ────────────────────────────────────────────────
    const int POOL_SIZE = 40;
    readonly Queue<ParticleSystem> _pool = new();

    // PERF: retornos diferidos por timer (sin StartCoroutine/WaitForSeconds por impacto).
    struct RetornoPS { public ParticleSystem ps; public float t; }
    readonly List<RetornoPS> _retornos = new();

    [Header("Prefabs bullet holes (auto desde ConfiguradorAssetsAAA)")]
    public GameObject prefabImpactoHormigon;
    public GameObject prefabImpactoMetal;
    public GameObject prefabImpactoMadera;
    public GameObject prefabSangre;

    // ── Decals de impacto ─────────────────────────────────────────────────
    const int MAX_DECALS = 150;
    readonly List<GameObject> _decals = new();
    // BUG FIX: un material compartido por tipo de decal procedural (antes: new Material por
    // impacto → al expulsar el GameObject el material quedaba huérfano = fuga continua).
    readonly Dictionary<TipoMaterial, Material> _matDecalCache = new();

    // ── Configuración por material ────────────────────────────────────────
    enum TipoMaterial { Metal, Hormigon, Madera, Tierra, Cristal, Carne, Asfalto, Generico }

    struct ConfigImpacto
    {
        public Color colorParticula;
        public Color colorDecal;
        public float tamano;
        public int   cantidad;
        public float velocidad;
        public float gravedad;
        public float duracion;
        public bool  chispas;
    }

    static readonly Dictionary<TipoMaterial, ConfigImpacto> CONFIGS = new()
    {
        [TipoMaterial.Metal]     = new ConfigImpacto { colorParticula = new Color(1.0f, 0.6f, 0.1f), colorDecal = new Color(0.1f,0.1f,0.1f,0.9f), tamano = 0.04f, cantidad = 18, velocidad = 6f,  gravedad = 1.5f, duracion = 0.8f, chispas = true  },
        [TipoMaterial.Hormigon]  = new ConfigImpacto { colorParticula = new Color(0.7f, 0.68f, 0.62f), colorDecal = new Color(0.15f,0.14f,0.12f,0.85f), tamano = 0.06f, cantidad = 22, velocidad = 4f,  gravedad = 2f,   duracion = 1.2f, chispas = false },
        [TipoMaterial.Madera]    = new ConfigImpacto { colorParticula = new Color(0.55f,0.38f,0.18f), colorDecal = new Color(0.2f,0.12f,0.05f,0.8f),  tamano = 0.05f, cantidad = 14, velocidad = 3.5f,gravedad = 2.5f, duracion = 1.0f, chispas = false },
        [TipoMaterial.Tierra]    = new ConfigImpacto { colorParticula = new Color(0.62f,0.52f,0.38f), colorDecal = new Color(0.3f,0.24f,0.16f,0.7f),  tamano = 0.08f, cantidad = 20, velocidad = 3f,  gravedad = 3f,   duracion = 1.5f, chispas = false },
        [TipoMaterial.Cristal]   = new ConfigImpacto { colorParticula = new Color(0.85f,0.95f,1.0f,0.7f), colorDecal = new Color(0.8f,0.9f,1.0f,0.5f), tamano = 0.03f, cantidad = 28, velocidad = 5f,  gravedad = 2f,   duracion = 1.0f, chispas = true  },
        [TipoMaterial.Carne]     = new ConfigImpacto { colorParticula = new Color(0.75f,0.05f,0.05f), colorDecal = new Color(0.5f,0.02f,0.02f,0.9f),  tamano = 0.04f, cantidad = 12, velocidad = 2.5f,gravedad = 1.8f, duracion = 0.6f, chispas = false },
        [TipoMaterial.Asfalto]   = new ConfigImpacto { colorParticula = new Color(0.3f, 0.3f, 0.32f), colorDecal = new Color(0.08f,0.08f,0.08f,0.8f), tamano = 0.06f, cantidad = 16, velocidad = 3f,  gravedad = 2.5f, duracion = 1.2f, chispas = false },
        [TipoMaterial.Generico]  = new ConfigImpacto { colorParticula = new Color(0.65f,0.63f,0.60f), colorDecal = new Color(0.12f,0.12f,0.12f,0.75f),tamano = 0.05f, cantidad = 15, velocidad = 4f,  gravedad = 2f,   duracion = 1.0f, chispas = false },
    };

    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (I != null && I != this) { Destroy(this); return; }
        I = this;
        PrecargarPool();

        // Auto-asignar bullet holes desde ConfiguradorAssetsAAA
        var cfg = ConfiguradorAssetsAAA.Instance;
        if (cfg != null)
        {
            if (prefabImpactoHormigon == null) prefabImpactoHormigon = cfg.prefabImpactoHormigon;
            if (prefabImpactoMetal    == null) prefabImpactoMetal    = cfg.prefabImpactoMetal;
            if (prefabImpactoMadera   == null) prefabImpactoMadera   = cfg.prefabImpactoMadera;
            if (prefabSangre          == null) prefabSangre          = cfg.prefabSangre;
        }
    }

    void PrecargarPool()
    {
        for (int i = 0; i < POOL_SIZE; i++)
            _pool.Enqueue(CrearSistemaParticula());
    }

    // PERF: un solo Update centraliza los retornos al pool — antes cada impacto lanzaba
    // una corrutina con su WaitForSeconds (ambos asignan en el heap).
    void Update()
    {
        if (_retornos.Count == 0) return;
        float dt = Time.deltaTime;
        for (int i = _retornos.Count - 1; i >= 0; i--)
        {
            var r = _retornos[i];
            r.t -= dt;
            if (r.t <= 0f) { DevolverInmediato(r.ps); _retornos.RemoveAt(i); }
            else _retornos[i] = r;
        }
    }

    void ProgramarRetorno(ParticleSystem ps, float delay)
    {
        if (ps != null) _retornos.Add(new RetornoPS { ps = ps, t = delay });
    }

    // ════════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ════════════════════════════════════════════════════════════════════════

    public static void Impacto(RaycastHit hit, float fuerzaImpacto = 1f)
    {
        if (I == null) return;
        var tipo = I.DetectarMaterial(hit.collider.gameObject, hit.point);
        I.SpawnImpacto(hit.point, hit.normal, tipo, fuerzaImpacto);
        I.SpawnDecal(hit.point, hit.normal, tipo, hit.collider.transform);
        // Screen shake leve al impactar metal/hormigon
        if (tipo == TipoMaterial.Metal || tipo == TipoMaterial.Hormigon)
            SistemaPolish.Shake(0.06f);
    }

    public static void Explosion(Vector3 centro, float radio)
    {
        if (I == null) return;
        // Impactos radiales en el suelo
        for (int i = 0; i < 12; i++)
        {
            Vector3 dir = Random.insideUnitSphere; dir.y = Mathf.Abs(dir.y);
            if (Physics.Raycast(centro, dir, out var hit, radio))
                I.SpawnImpacto(hit.point, hit.normal, TipoMaterial.Hormigon, 2f);
        }
        // Shake proporcional al radio
        SistemaPolish.Shake(Mathf.Clamp01(radio / 15f));
    }

    public static void Chispa(Vector3 pos, Vector3 normal, int cantidad = 8)
    {
        if (I == null) return;
        I.SpawnImpacto(pos, normal, TipoMaterial.Metal, 0.5f, cantidad);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  DETECCIÓN DE MATERIAL
    // ════════════════════════════════════════════════════════════════════════

    // PERF: tablas de términos pre-asignadas (static readonly). Antes cada impacto pasaba
    // los términos como params string[] → un array nuevo por llamada (~6/impacto), más dos
    // string.ToLower(). Ahora: cero asignaciones; la comparación es IndexOf ordinal-ignore-case.
    static readonly string[] N_METAL    = { "metal","coche","car","vehiculo","hierro","fence","reja" };
    static readonly string[] N_HORMIGON = { "hormigon","concrete","muro","wall","edificio","building","asfalto","road","carretera","suelo" };
    static readonly string[] N_MADERA   = { "madera","wood","puerta","door","ventana","window" };
    static readonly string[] N_CRISTAL  = { "cristal","glass","vidrio" };
    static readonly string[] N_TIERRA   = { "tierra","dirt","terrain","terreno","grass","hierba" };
    static readonly string[] N_CARNE    = { "guardia","policia","civil","npc","enemigo","persona" };
    static readonly string[] N_ASFALTO  = { "asfalto","road" };
    static readonly string[] M_METAL    = { "metal","steel","iron" };
    static readonly string[] M_HORMIGON = { "concrete","stone","brick" };
    static readonly string[] M_MADERA   = { "wood","plank" };
    static readonly string[] M_CRISTAL  = { "glass" };
    static readonly string[] M_TIERRA   = { "ground","dirt","grass" };

    TipoMaterial DetectarMaterial(GameObject go, Vector3 punto)
    {
        // Por nombre del objeto (sin ToLower: ContainsAny es case-insensitive)
        string nombre = go.name;
        if (ContainsAny(nombre, N_METAL))    return TipoMaterial.Metal;
        if (ContainsAny(nombre, N_HORMIGON)) return ContainsAny(nombre, N_ASFALTO) ? TipoMaterial.Asfalto : TipoMaterial.Hormigon;
        if (ContainsAny(nombre, N_MADERA))   return TipoMaterial.Madera;
        if (ContainsAny(nombre, N_CRISTAL))  return TipoMaterial.Cristal;
        if (ContainsAny(nombre, N_TIERRA))   return TipoMaterial.Tierra;
        if (ContainsAny(nombre, N_CARNE))    return TipoMaterial.Carne;

        // Por capa
        int layer = go.layer;
        if (LayerMask.LayerToName(layer) == "Ground") return TipoMaterial.Tierra;

        // Por componente Terrain
        if (go.GetComponent<Terrain>() != null) return TipoMaterial.Tierra;

        // Por material del renderer
        var r = go.GetComponentInChildren<Renderer>();
        if (r != null && r.sharedMaterial != null)
        {
            string mat = r.sharedMaterial.name;
            if (ContainsAny(mat, M_METAL))    return TipoMaterial.Metal;
            if (ContainsAny(mat, M_HORMIGON)) return TipoMaterial.Hormigon;
            if (ContainsAny(mat, M_MADERA))   return TipoMaterial.Madera;
            if (ContainsAny(mat, M_CRISTAL))  return TipoMaterial.Cristal;
            if (ContainsAny(mat, M_TIERRA))   return TipoMaterial.Tierra;
        }

        return TipoMaterial.Generico;
    }

    // Sin params (evita asignar un array por llamada) y sin ToLower (IndexOf ordinal-ignore-case).
    static bool ContainsAny(string s, string[] terms)
    {
        foreach (var t in terms)
            if (s.IndexOf(t, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  SPAWN
    // ════════════════════════════════════════════════════════════════════════

    void SpawnImpacto(Vector3 pos, Vector3 normal, TipoMaterial tipo,
                      float fuerzaMult = 1f, int cantidadOverride = -1)
    {
        var cfg = CONFIGS[tipo];
        var ps  = ObtenerDelPool();
        if (ps == null) return;

        ps.transform.position = pos + normal * 0.01f;
        ps.transform.rotation = Quaternion.LookRotation(normal);

        var main = ps.main;
        main.startColor    = new ParticleSystem.MinMaxGradient(
            Color.Lerp(cfg.colorParticula, Color.white, 0.15f), cfg.colorParticula);
        main.startSize     = new ParticleSystem.MinMaxCurve(cfg.tamano * 0.6f, cfg.tamano * fuerzaMult);
        main.startSpeed    = new ParticleSystem.MinMaxCurve(cfg.velocidad * 0.5f, cfg.velocidad * fuerzaMult);
        main.startLifetime = new ParticleSystem.MinMaxCurve(cfg.duracion * 0.5f, cfg.duracion);
        main.gravityModifier = new ParticleSystem.MinMaxCurve(cfg.gravedad);
        main.maxParticles = 60;

        var emit = ps.emission;
        emit.SetBurst(0, new ParticleSystem.Burst(0f, cantidadOverride > 0 ? cantidadOverride : cfg.cantidad));

        ps.gameObject.SetActive(true);
        ps.Play();

        // Devolver al pool tras la duración (timer, sin corrutina)
        ProgramarRetorno(ps, cfg.duracion + 0.2f);

        // Chispas adicionales para metal/cristal
        if (cfg.chispas && fuerzaMult > 0.5f) SpawnChispas(pos, normal, cfg);
    }

    void SpawnChispas(Vector3 pos, Vector3 normal, ConfigImpacto cfg)
    {
        var ps = ObtenerDelPool();
        if (ps == null) return;

        ps.transform.position = pos + normal * 0.01f;
        ps.transform.rotation = Quaternion.LookRotation(normal);

        var main = ps.main;
        main.startColor    = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.8f, 0.2f), new Color(1f, 0.4f, 0.05f));
        main.startSize     = new ParticleSystem.MinMaxCurve(0.01f, 0.025f);
        main.startSpeed    = new ParticleSystem.MinMaxCurve(3f, 9f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        main.gravityModifier = new ParticleSystem.MinMaxCurve(3f);
        main.maxParticles = 30;

        var trails = ps.trails;
        trails.enabled = true;
        trails.ratio   = 0.6f;
        trails.lifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(0.008f);

        var emit = ps.emission;
        emit.SetBurst(0, new ParticleSystem.Burst(0f, 12));

        ps.gameObject.SetActive(true);
        ps.Play();
        ProgramarRetorno(ps, 0.6f);
    }

    void SpawnDecal(Vector3 pos, Vector3 normal, TipoMaterial tipo, Transform padre)
    {
        var cfg = CONFIGS[tipo];

        // Usar prefab de bullet hole si disponible (Assets/#Tools/Resources/Particles/BulletHoles)
        GameObject prefabDecal = tipo switch
        {
            TipoMaterial.Metal    => prefabImpactoMetal,
            TipoMaterial.Madera   => prefabImpactoMadera,
            TipoMaterial.Carne    => prefabSangre,
            _                     => prefabImpactoHormigon,
        };

        GameObject go;
        if (prefabDecal != null)
        {
            go = Instantiate(prefabDecal, pos + normal * 0.01f, Quaternion.LookRotation(-normal), padre);
            go.name = "Decal_Impacto";
        }
        else
        {
            // Fallback procedural
            go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Decal_Impacto";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.position = pos + normal * 0.008f;
            go.transform.rotation = Quaternion.LookRotation(-normal);
            go.GetComponent<MeshRenderer>().sharedMaterial = MaterialDecal(tipo, cfg.colorDecal);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.SetParent(padre, true);
        }

        float size = Random.Range(0.06f, 0.14f);
        go.transform.localScale = Vector3.one * size;

        _decals.Add(go);
        if (_decals.Count > MAX_DECALS)
        {
            if (_decals[0] != null) Destroy(_decals[0]);
            _decals.RemoveAt(0);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  POOL
    // ════════════════════════════════════════════════════════════════════════

    ParticleSystem ObtenerDelPool()
    {
        if (_pool.Count > 0) return _pool.Dequeue();
        return CrearSistemaParticula(); // overflow
    }

    void DevolverInmediato(ParticleSystem ps)
    {
        if (ps == null) return;
        ps.Stop();
        ps.Clear();
        ps.gameObject.SetActive(false);
        _pool.Enqueue(ps);
    }

    ParticleSystem CrearSistemaParticula()
    {
        var go = new GameObject("ImpactoPS");
        go.transform.SetParent(transform);
        go.SetActive(false);

        var ps   = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop         = false;
        main.playOnAwake  = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 60;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle     = 35f;
        shape.radius    = 0.01f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        return ps;
    }

    // BUG FIX: un material compartido por tipo de decal en vez de uno nuevo por impacto.
    // Los decals procedurales solo varían en escala (transform), no en material → compartir es correcto.
    Material MaterialDecal(TipoMaterial tipo, Color color)
    {
        if (_matDecalCache.TryGetValue(tipo, out var m) && m != null) return m;
        m = new Material(Shader.Find("Unlit/Transparent") ?? Shader.Find("Standard")) { color = color };
        _matDecalCache[tipo] = m;
        return m;
    }

    void OnDestroy()
    {
        if (I == this) I = null;
        foreach (var m in _matDecalCache.Values) if (m != null) Destroy(m);
        _matDecalCache.Clear();
    }
}
