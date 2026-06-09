// Assets/Scripts/SistemaMobiliarioUrbano.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MOBILIARIO URBANO — popula las calles de Alsasua con props reales
//
//  Coloca en runtime mobiliario de calle usando los prefabs de
//  Resources/Prefabs/Mobiliario/ (Polygon City + PolyHaven + FBX Mundo):
//
//  Zonas y elementos:
//    Herriko Plaza (radio ~80m)  → bancos, mesas de picnic, hortensias,
//                                  farolas PolyHaven, toldo de bar
//    Calles principales          → bancos cada ~25m, farola cada ~18m
//    Nafarroa Kalea              → toldos de comercios, bicicletas aparcadas
//    Polígono Isasia             → contenedores metálicos, megáfonos
//    Zonas verdes / ribera       → hortensias, mesas de picnic
//
//  Quality tier:
//    0-1: densidad completa (80 objetos)
//    2:   densidad media (40 objetos)
//    3:   solo farolas (impacto visual mínimo pero importante)
//
//  DirectorMundo:
//    Disturbio → bicicletas y bancos cerca del centro se convierten en
//                barricadas (SistemaManifestacion ya coloca barricadas;
//                este sistema solo "vuelca" los props existentes).
//
//  Implementación sin alloc: todos los objetos se crean en Start() y
//  permanecen estáticos. GPU Instancing activo en todos los materiales.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[DefaultExecutionOrder(185)]
public class SistemaMobiliarioUrbano : MonoBehaviour
{
    public static SistemaMobiliarioUrbano Instance { get; private set; }

    // ── Config ────────────────────────────────────────────────────────────
    [Range(10, 120)]
    [SerializeField] int maxObjetosUltra = 80;
    [SerializeField] float alturaBase    = 0.05f;   // offset sobre terreno

    // ── Estado ────────────────────────────────────────────────────────────
    readonly List<GameObject> _objetos   = new();
    bool _volcadoRedada;

    // ── Zonas de colocación (coordenadas Unity) ───────────────────────────
    static readonly Vector3 HERRIKO_PLAZA   = new(1918f, 0f, 8570f);
    static readonly Vector3 NAFARROA_KALEA  = new(1870f, 0f, 8590f);
    static readonly Vector3 POLIGONO_ISASIA = new(2350f, 0f, 8400f);
    static readonly Vector3 RIBERA_ARAKIL   = new(1918f, 0f, 8420f);

    // ════════════════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(20f);   // después de edificios y terreno

        var assets = SistemaAssets.Instance;
        if (assets == null) yield break;

        int max = MaxPorTier();
        yield return StartCoroutine(PoblarZonas(assets, max));

        DirectorMundo.OnEvento += ReaccionarDirector;
        AlsasuaLogger.Info("MobiliarioUrbano", $"{_objetos.Count} props colocados");
    }

    void OnDestroy() => DirectorMundo.OnEvento -= ReaccionarDirector;

    // ════════════════════════════════════════════════════════════════════════
    //  POBLACIÓN POR ZONAS
    // ════════════════════════════════════════════════════════════════════════

    IEnumerator PoblarZonas(SistemaAssets assets, int max)
    {
        int colocados = 0;

        // ── Herriko Plaza ─────────────────────────────────────────────────
        int plazaMax = max / 4;
        // Bancos alrededor de la plaza (radio 30-60m)
        for (int i = 0; i < plazaMax / 3; i++)
        {
            ColocarPrefab("Bench", assets.PropUrbanoAleatorio() ?? PrefabMobiliario("Bench"),
                PuntoEnAnillo(HERRIKO_PLAZA, 30f, 60f));
        }
        // Mesas con hortensia
        for (int i = 0; i < plazaMax / 4; i++)
        {
            ColocarPrefab("Hortensia", PrefabMobiliario("Hydrangea"),
                PuntoEnAnillo(HERRIKO_PLAZA, 15f, 45f));
        }
        // Farolas
        for (int i = 0; i < plazaMax / 3; i++)
        {
            float angulo = i * (360f / (plazaMax / 3)) * Mathf.Deg2Rad;
            Vector3 pos = HERRIKO_PLAZA + new Vector3(Mathf.Sin(angulo) * 50f, 0f, Mathf.Cos(angulo) * 50f);
            ColocarPrefab("Farola_Plaza", PrefabMobiliario("Lantern"), PosEnTerreno(pos));
        }
        colocados += plazaMax;
        yield return null;

        // ── Nafarroa Kalea — toldos y bicicletas ─────────────────────────
        int kalMax = max / 5;
        for (int i = 0; i < kalMax; i++)
        {
            Vector3 pos = NAFARROA_KALEA + new Vector3(Random.Range(-80f, 80f), 0f, Random.Range(-20f, 20f));
            string tipo = i % 3 == 0 ? "Awning" : i % 3 == 1 ? "Post lamp" : "Bicicletas";
            ColocarPrefab(tipo, PrefabMobiliario(tipo), PosEnTerreno(pos));
        }
        colocados += kalMax;
        yield return null;

        // ── Polígono Isasia — contenedores e industrial ───────────────────
        int polMax = max / 6;
        for (int i = 0; i < polMax; i++)
        {
            Vector3 pos = POLIGONO_ISASIA + new Vector3(Random.Range(-120f, 120f), 0f, Random.Range(-60f, 60f));
            string tipo = i % 2 == 0 ? "Container" : "Megaphone";
            ColocarPrefab(tipo, PrefabMobiliario(tipo), PosEnTerreno(pos));
        }
        colocados += polMax;
        yield return null;

        // ── Ribera del Arakil — mesas de picnic y bancos ─────────────────
        int ribMax = max / 5;
        for (int i = 0; i < ribMax; i++)
        {
            Vector3 pos = RIBERA_ARAKIL + new Vector3(Random.Range(-200f, 200f), 0f, Random.Range(-20f, 20f));
            ColocarPrefab("PicnicTable", PrefabMobiliario("PicnicTable"), PosEnTerreno(pos));
        }
        colocados += ribMax;
        yield return null;

        // ── Farolas a lo largo de las calles principales ──────────────────
        int farolaMax = Mathf.Max(0, max - colocados);
        for (int i = 0; i < farolaMax; i++)
        {
            float t  = (float)i / farolaMax;
            float x  = Mathf.Lerp(HERRIKO_PLAZA.x - 300f, HERRIKO_PLAZA.x + 300f, t);
            Vector3 pos = PosEnTerreno(new Vector3(x, 0f, HERRIKO_PLAZA.z + Random.Range(-5f, 5f)));
            ColocarPrefab("FarolaCalle", PrefabMobiliario("lamp"), pos);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    void ColocarPrefab(string tag, GameObject prefab, Vector3 pos)
    {
        if (prefab == null || pos == Vector3.zero) return;

        var go = Instantiate(prefab, pos,
            Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
        go.name = $"Mobiliario_{tag}_{_objetos.Count}";
        go.isStatic = true;

        // GPU instancing en todos los renderers
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
            if (mr.sharedMaterial != null) mr.sharedMaterial.enableInstancing = true;

        // Registrar luces en SistemaVidaNocturna (farolas, lámparas)
        foreach (var luz in go.GetComponentsInChildren<Light>())
            SistemaVidaNocturna.Instance?.RegistrarFarola(luz);

        _objetos.Add(go);
    }

    Vector3 PosEnTerreno(Vector3 xz)
    {
        if (Terrain.activeTerrain != null)
            xz.y = Terrain.activeTerrain.SampleHeight(xz) + alturaBase;
        return xz;
    }

    static Vector3 PuntoEnAnillo(Vector3 centro, float rMin, float rMax)
    {
        float angulo = Random.Range(0f, Mathf.PI * 2f);
        float radio  = Random.Range(rMin, rMax);
        var pos = centro + new Vector3(Mathf.Sin(angulo) * radio, 0f, Mathf.Cos(angulo) * radio);
        if (Terrain.activeTerrain != null) pos.y = Terrain.activeTerrain.SampleHeight(pos) + 0.05f;
        return pos;
    }

    static GameObject PrefabMobiliario(string keyword)
    {
        // Buscar en Resources/Prefabs/Mobiliario por keyword
        var todos = Resources.LoadAll<GameObject>("Prefabs/Mobiliario");
        keyword = keyword.ToLower();
        foreach (var p in todos)
            if (p.name.ToLower().Contains(keyword)) return p;
        return todos.Length > 0 ? todos[Random.Range(0, todos.Length)] : null;
    }

    int MaxPorTier() => SistemaOptimizacion.TierCalidad switch
    {
        0 => maxObjetosUltra,
        1 => Mathf.RoundToInt(maxObjetosUltra * 0.7f),
        2 => Mathf.RoundToInt(maxObjetosUltra * 0.4f),
        _ => Mathf.Max(5, Mathf.RoundToInt(maxObjetosUltra * 0.1f)),
    };

    // ── Director ──────────────────────────────────────────────────────────

    void ReaccionarDirector(DirectorMundo.EventoMundo ev)
    {
        if (ev == DirectorMundo.EventoMundo.Disturbio && !_volcadoRedada)
        {
            _volcadoRedada = true;
            VolcarPropsCercanos();
        }
        else if (ev == DirectorMundo.EventoMundo.Calma)
        {
            _volcadoRedada = false;
        }
    }

    void VolcarPropsCercanos()
    {
        // Bicicletas y bancos cercanos al centro se "vuelcan" (rotación aleatoria)
        foreach (var go in _objetos)
        {
            if (go == null) continue;
            string n = go.name.ToLower();
            if (!n.Contains("bicicl") && !n.Contains("bench")) continue;
            if (Vector3.Distance(go.transform.position, HERRIKO_PLAZA) > 150f) continue;

            go.isStatic = false;
            go.transform.rotation = Quaternion.Euler(
                Random.Range(60f, 120f),
                Random.Range(0f, 360f),
                Random.Range(-30f, 30f));
            go.isStatic = true;
        }
        AlsasuaLogger.Info("MobiliarioUrbano", "Props volcados por disturbio");
    }
}
