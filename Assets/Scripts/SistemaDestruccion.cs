// SistemaDestruccion.cs
// Fuego, cristales rotos, coches volando y calcinados.
// Cóctel molotov, bomba lapa, coche bomba.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SistemaDestruccion : SingletonMono<SistemaDestruccion>
{

    [Header("Efectos de fuego")]
    public GameObject prefabFuegoGrande;
    public GameObject prefabFuegoMedio;
    public GameObject prefabFuegoChico;
    public GameObject prefabHumoNegro;
    public GameObject prefabCenizas;

    [Header("Cristales")]
    public GameObject prefabFragmentoCristal;
    public int numFragmentosCristal = 12;

    [Header("Coches calcinados")]
    public Material matCalcinado;    // material ennegrecido, sin brillo
    public GameObject prefabCocheCalcinado;

    [Header("Explosión coche")]
    public GameObject prefabExplosionGrande;
    public float fuerzaExplosionCoche   = 1800f;
    public float alturaExplosionCoche   = 22f;    // sube 22m al explotar
    public float radioExplosionCoche    = 12f;
    public AudioClip clipExplosion;
    public AudioClip clipCristales;

    [Header("Molotov")]
    public GameObject prefabMolotov;
    public float radioFuegoMolotov = 5f;
    public float duracionFuegoMolotov = 25f;

    // ── Colas de efectos activos ──────────────────────────────────────────
    readonly List<GameObject> _fuegoActivos = new();
    const int MAX_FUEGOS = 30;


    // =========================================================================
    //  COCHE BOMBA / EXPLOSIÓN DE COCHE
    // =========================================================================

    public void ExplotarCoche(GameObject coche, bool esCocheBomba = false)
    {
        if (coche == null) return;
        StartCoroutine(SecuenciaExplosionCoche(coche, esCocheBomba));
    }

    IEnumerator SecuenciaExplosionCoche(GameObject coche, bool esCocheBomba)
    {
        Vector3 pos = coche.transform.position;

        // 1. Flash de llamas previo
        SpawnFuego(pos + Vector3.up, TamanoFuego.Medio);
        yield return new WaitForSeconds(0.15f);

        // 2. Explosión visual
        if (prefabExplosionGrande != null)
            Instantiate(prefabExplosionGrande, pos, Quaternion.identity);
        else
            ExplosionProcedural(pos, 6f);

        // 3. Audio
        if (clipExplosion != null)
            AudioSource.PlayClipAtPoint(clipExplosion, pos, 1f);

        // 4. El coche sale volando hacia arriba
        var rb = coche.GetComponent<Rigidbody>();
        if (rb == null) rb = coche.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.mass = 600f;

        // Impulso hacia arriba + rotación caótica
        Vector3 fuerza = Vector3.up * fuerzaExplosionCoche;
        fuerza += new Vector3(Random.Range(-200f, 200f), 0, Random.Range(-200f, 200f));
        rb.AddForce(fuerza, ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * 1500f, ForceMode.Impulse);

        // 5. Daño a entidades cercanas
        DanarEntidadesCercanas(pos, esCocheBomba ? radioExplosionCoche * 2f : radioExplosionCoche, esCocheBomba ? 200 : 80);

        // 6. Esperar a que caiga (~3-5s para 22m)
        yield return new WaitForSeconds(3.5f);

        // 7. Coche aterriza calcinado y en llamas
        CalcinarCoche(coche);
        SpawnFuego(coche.transform.position, TamanoFuego.Grande);
        SpawnHumoNegro(coche.transform.position);

        if (esCocheBomba)
        {
            // Segunda explosión al aterrizar
            yield return new WaitForSeconds(0.5f);
            ExplosionProcedural(coche.transform.position, 8f);
            DanarEntidadesCercanas(coche.transform.position, radioExplosionCoche, 150);
        }

        // Fuego durante 60 segundos y luego extinguir
        yield return new WaitForSeconds(60f);
        ExtinguirFuego(coche.transform.position, 8f);
    }

    void CalcinarCoche(GameObject coche)
    {
        // Cambiar todos los materiales a calcinado
        foreach (var mr in coche.GetComponentsInChildren<MeshRenderer>())
        {
            if (matCalcinado != null)
                mr.sharedMaterial = matCalcinado;
            else
            {
                // Crear material calcinado proceduralmente
                var mat = new Material(mr.sharedMaterial);
                mat.color = new Color(0.08f, 0.08f, 0.08f);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.02f);
                mr.sharedMaterial = mat;
            }
        }
        // Abollar ligeramente (escala irregular)
        coche.transform.localScale = new Vector3(
            Random.Range(0.85f, 1.05f), Random.Range(0.70f, 0.90f), Random.Range(0.85f, 1.05f));
    }

    // =========================================================================
    //  CÓCTEL MOLOTOV
    // =========================================================================

    public void LanzarMolotov(Vector3 posicion, Vector3 velocidad)
    {
        GameObject molotov;
        if (prefabMolotov != null)
            molotov = Instantiate(prefabMolotov, posicion, Quaternion.identity);
        else
        {
            molotov = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            molotov.name = "Molotov";
            molotov.transform.localScale = Vector3.one * 0.15f;
            molotov.GetComponent<MeshRenderer>().material.color = new Color(0.8f, 0.5f, 0.2f);
        }

        var rb = molotov.GetComponent<Rigidbody>() ?? molotov.AddComponent<Rigidbody>();
        rb.mass = 0.5f;
        rb.linearVelocity = velocidad;

        molotov.AddComponent<ProyectilMolotov>().Init(this, radioFuegoMolotov, duracionFuegoMolotov);
    }

    public void ExplotarMolotov(Vector3 pos)
    {
        // Pool de fuego en radio
        int num = Random.Range(4, 8);
        for (int i = 0; i < num; i++)
        {
            var offset = Random.insideUnitCircle * radioFuegoMolotov;
            var fpos = pos + new Vector3(offset.x, 0.1f, offset.y);
            float y = Terrain.activeTerrain != null ? Terrain.activeTerrain.SampleHeight(fpos) + 0.1f : fpos.y;
            fpos.y = y;
            SpawnFuego(fpos, TamanoFuego.Chico);
        }
        // Daño
        DanarEntidadesCercanas(pos, radioFuegoMolotov, 30);
        SistemaApoyoPopular.Instance?.SumarParanoia(8f);
        GameManagerAltsasua.Instance?.AumentarBusqueda(1);
    }

    // =========================================================================
    //  CRISTALES ROTOS
    // =========================================================================

    public void RomperCristales(Vector3 pos, Vector3 normal)
    {
        if (clipCristales != null) AudioSource.PlayClipAtPoint(clipCristales, pos, 0.8f);
        for (int i = 0; i < numFragmentosCristal; i++)
        {
            GameObject frag;
            if (prefabFragmentoCristal != null)
                frag = Instantiate(prefabFragmentoCristal, pos, Random.rotation);
            else
            {
                frag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                frag.transform.position = pos;
                frag.transform.localScale = Vector3.one * Random.Range(0.03f, 0.12f);
                frag.transform.rotation = Random.rotation;
                var mat = new Material(Shader.Find("Standard")); mat.color = new Color(0.8f, 0.9f, 1f, 0.6f);
                if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 3);
                frag.GetComponent<MeshRenderer>().material = mat;
            }
            var rb = frag.GetComponent<Rigidbody>() ?? frag.AddComponent<Rigidbody>();
            rb.mass = 0.05f;
            var dir = (normal + Random.insideUnitSphere * 2f).normalized;
            rb.AddForce(dir * Random.Range(1f, 6f), ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * 10f, ForceMode.Impulse);
            Destroy(frag, 8f);
        }
    }

    // =========================================================================
    //  FUEGO GENÉRICO
    // =========================================================================

    enum TamanoFuego { Chico, Medio, Grande }

    void SpawnFuego(Vector3 pos, TamanoFuego tam)
    {
        if (_fuegoActivos.Count >= MAX_FUEGOS)
        {
            Destroy(_fuegoActivos[0]);
            _fuegoActivos.RemoveAt(0);
        }

        var prefab = tam == TamanoFuego.Grande ? prefabFuegoGrande
                   : tam == TamanoFuego.Medio  ? prefabFuegoMedio  : prefabFuegoChico;

        GameObject go = prefab != null ? Instantiate(prefab, pos, Quaternion.identity)
                                       : CrearFuegoProcedural(pos, tam);
        _fuegoActivos.Add(go);
    }

    static GameObject CrearFuegoProcedural(Vector3 pos, TamanoFuego tam)
    {
        var go = new GameObject("Fuego");
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        float size = tam == TamanoFuego.Grande ? 2.5f : tam == TamanoFuego.Medio ? 1.2f : 0.5f;
        main.startColor     = new ParticleSystem.MinMaxGradient(Color.yellow, new Color(1f,0.2f,0f));
        main.startSpeed     = new ParticleSystem.MinMaxCurve(size * 0.8f, size * 2f);
        main.startSize      = new ParticleSystem.MinMaxCurve(size * 0.3f, size * 0.8f);
        main.startLifetime  = 1.8f; main.maxParticles = (int)(100 * size);
        var em = ps.emission; em.rateOverTime = 40 * size;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = size * 0.3f;
        ps.Play();

        // Luz de fuego
        var lightGO = new GameObject("LuzFuego");
        lightGO.transform.SetParent(go.transform);
        lightGO.transform.localPosition = Vector3.up * size;
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Point; light.color = new Color(1f, 0.5f, 0.1f);
        light.range = size * 8f; light.intensity = size * 3f;

        return go;
    }

    void SpawnHumoNegro(Vector3 pos)
    {
        var go = prefabHumoNegro != null ? Instantiate(prefabHumoNegro, pos, Quaternion.identity) : CrearHumoProcedural(pos);
        _fuegoActivos.Add(go);
    }

    static GameObject CrearHumoProcedural(Vector3 pos)
    {
        var go = new GameObject("Humo");
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startColor    = new ParticleSystem.MinMaxGradient(new Color(0.1f,0.1f,0.1f,0.8f), new Color(0.3f,0.3f,0.3f,0.4f));
        main.startSpeed    = new ParticleSystem.MinMaxCurve(1.5f, 3.5f); main.startSize = new ParticleSystem.MinMaxCurve(2f, 6f);
        main.startLifetime = 8f; main.maxParticles = 50;
        var em = ps.emission; em.rateOverTime = 5f;
        ps.Play();
        return go;
    }

    void ExtinguirFuego(Vector3 pos, float radio)
    {
        for (int i = _fuegoActivos.Count - 1; i >= 0; i--)
        {
            if (_fuegoActivos[i] == null) { _fuegoActivos.RemoveAt(i); continue; }
            if (Vector3.Distance(_fuegoActivos[i].transform.position, pos) < radio)
            { Destroy(_fuegoActivos[i]); _fuegoActivos.RemoveAt(i); }
        }
    }

    // =========================================================================
    //  EXPLOSIÓN PROCEDIMENTAL
    // =========================================================================

    void ExplosionProcedural(Vector3 pos, float radio)
    {
        // Onda expansiva de fuerza
        var cols = Physics.OverlapSphere(pos, radio);
        foreach (var col in cols)
        {
            var rb = col.GetComponent<Rigidbody>();
            if (rb != null)
            {
                float dist = Vector3.Distance(rb.transform.position, pos);
                float fuerza = Mathf.Lerp(fuerzaExplosionCoche * 0.3f, 0, dist / radio);
                rb.AddExplosionForce(fuerza, pos, radio, 3f, ForceMode.Impulse);
            }
        }
        SpawnFuego(pos, TamanoFuego.Grande);
        SpawnHumoNegro(pos);
    }

    void DanarEntidadesCercanas(Vector3 pos, float radio, int daño)
    {
        var cols = Physics.OverlapSphere(pos, radio);
        foreach (var col in cols)
        {
            float falloff = 1f - Mathf.Clamp01(Vector3.Distance(col.transform.position, pos) / radio);
            int   danoCal = Mathf.RoundToInt(daño * falloff);

            var damageable = col.GetComponent<IDamageable>()
                          ?? col.GetComponentInParent<IDamageable>();
            if (damageable != null) { damageable.RecibirDano(danoCal, pos, TipoDano.Explosion); continue; }
        }
    }
}

// ── Proyectil molotov ─────────────────────────────────────────────────────

public class ProyectilMolotov : MonoBehaviour
{
    SistemaDestruccion _sistema;
    float _radio, _duracion;
    bool  _explotado;

    public void Init(SistemaDestruccion s, float r, float d) { _sistema = s; _radio = r; _duracion = d; }

    void OnCollisionEnter(Collision col)
    {
        if (_explotado) return;
        _explotado = true;
        _sistema?.ExplotarMolotov(transform.position);
        Destroy(gameObject);
    }

    void Update() { if (_explotado) Destroy(gameObject); }
}
