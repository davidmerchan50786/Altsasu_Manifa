// Assets/Scripts/PropsDestruccionManifestacion.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PROPS DE DESTRUCCION — coloca escombros, barricadas y fuego en zonas de
//  manifestacion segun la intensidad de la protesta (0-5).
//
//  Nivel 0: calles limpias
//  Nivel 1: basura dispersa, papeleras volcadas
//  Nivel 2: papeleras y contenedores volcados
//  Nivel 3: barricadas (contenedores + mesas)
//  Nivel 4: muros destruidos, fuego
//  Nivel 5: caos total + explosiones
//
//  Arquitectura:
//    · Pool de objetos reutilizables por categoria
//    · Suscribe a SistemaApoyoPopular.OnApoyoCambia para derivar intensidad
//    · Alinea props a altura de terreno
//    · Coloca muros destruidos cerca de edificios existentes
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(-30)]
public class PropsDestruccionManifestacion : SingletonMono<PropsDestruccionManifestacion>
{
    // ── Configuracion ────────────────────────────────────────────────────
    [Header("Intensidad de protesta (0-5)")]
    [Range(0, 5)] public int intensidadActual = 0;

    [Header("Zonas de manifestacion")]
    [Tooltip("Centros de zonas donde aparecen props. Si vacio, usa zonas por defecto.")]
    public List<Vector3> zonasManifestacion = new();

    [Header("Radios")]
    [Range(40f, 150f)] public float radioMinimo = 80f;
    [Range(80f, 200f)] public float radioMaximo = 120f;

    [Header("Prefabs — Nivel 1-2: basura")]
    public GameObject prefabPapelera;        // Papelera volcada
    public GameObject prefabContenedor;      // Contenedor de basura

    [Header("Prefabs — Nivel 3: barricadas")]
    public GameObject prefabContenedorMetal; // Metal container
    public GameObject prefabMesaPicnic;      // Mesa como barricada

    [Header("Prefabs — Nivel 4: destruccion")]
    public GameObject prefabMuroDestruido;   // destroyedWalls3.fbx
    public GameObject prefabFuego;           // LiteFireEffect

    [Header("Prefabs — Nivel 5: explosiones")]
    public GameObject prefabExplosionVFX;    // HQ Explosions

    [Header("Cantidades por zona")]
    public int maxBasuraPorZona       = 12;
    public int maxBarricadasPorZona   = 6;
    public int maxMurosDestruidosPorZona = 4;
    public int maxFuegosPorZona       = 5;
    public int maxExplosionesPorZona  = 3;

    // ── Evento estatico para que otros sistemas cambien la intensidad ─────
    /// <summary>Invocado cuando la intensidad cambia. Parametro: nueva intensidad (0-5).</summary>
    public static event Action<int> OnIntensidadCambia;

    // ── Pools por categoria ──────────────────────────────────────────────
    enum CategoriaProps { Basura, Barricada, MuroDestruido, Fuego, Explosion }

    readonly Dictionary<CategoriaProps, Queue<GameObject>> _pool = new()
    {
        { CategoriaProps.Basura,         new Queue<GameObject>(32) },
        { CategoriaProps.Barricada,      new Queue<GameObject>(16) },
        { CategoriaProps.MuroDestruido,  new Queue<GameObject>(8)  },
        { CategoriaProps.Fuego,          new Queue<GameObject>(12) },
        { CategoriaProps.Explosion,      new Queue<GameObject>(8)  },
    };

    // Props activos por zona (indice de zona en zonasManifestacion)
    readonly Dictionary<int, List<GameObject>> _activosPorZona = new();

    // Buffers reutilizables — cero allocs en Update
    readonly List<GameObject> _bufferDesactivar = new(32);

    // Padres de organizacion
    Transform _parentProps;

    // Corrutinas guardadas
    Coroutine _crActualizar;

    int _intensidadAnterior = -1;

    // ── Zonas por defecto (coords Unity fusion) ──────────────────────────
    static readonly Vector3[] ZONAS_DEFECTO =
    {
        new(GeoDataAlsasua.OX - 110f, 0f, GeoDataAlsasua.OZ),       // Herriko Plaza
        new(GeoDataAlsasua.OX - 25f,  0f, GeoDataAlsasua.OZ + 50f), // Casco Viejo
        new(GeoDataAlsasua.OX + 76f,  0f, GeoDataAlsasua.OZ + 106f),// Plaza Fueros
        new(GeoDataAlsasua.OX - 195f, 0f, GeoDataAlsasua.OZ - 117f),// Gaztetxe
    };

    // ── Singleton ────────────────────────────────────────────────────────
    protected override void OnAwake()
    {
        _parentProps = new GameObject("_PropsDestruccion").transform;
    }

    void Start()
    {
        // Si no se asignaron zonas en inspector, usar las por defecto
        if (zonasManifestacion.Count == 0)
        {
            for (int i = 0; i < ZONAS_DEFECTO.Length; i++)
                zonasManifestacion.Add(ZONAS_DEFECTO[i]);
        }

        // Ajustar alturas de zonas al terreno
        for (int i = 0; i < zonasManifestacion.Count; i++)
        {
            var z = zonasManifestacion[i];
            z.y = AlturaTerreno(z.x, z.z) + 0.05f;
            zonasManifestacion[i] = z;
        }

        // Suscribir a apoyo popular para derivar intensidad
        SistemaApoyoPopular.OnApoyoCambia += OnApoyoCambiaHandler;

        // Aplicar intensidad inicial
        AplicarIntensidad(intensidadActual);
    }

    protected override void OnDestroyed()
    {
        SistemaApoyoPopular.OnApoyoCambia -= OnApoyoCambiaHandler;

        if (_crActualizar != null)
            StopCoroutine(_crActualizar);
    }

    // ── Derivar intensidad desde apoyo popular ───────────────────────────
    // Apoyo alto (>80) = protesta pacifica (nivel bajo)
    // Apoyo bajo (<20) = disturbios graves (nivel alto)
    void OnApoyoCambiaHandler(float apoyo)
    {
        int nueva;
        if      (apoyo >= 80f) nueva = 0;
        else if (apoyo >= 65f) nueva = 1;
        else if (apoyo >= 50f) nueva = 2;
        else if (apoyo >= 35f) nueva = 3;
        else if (apoyo >= 20f) nueva = 4;
        else                   nueva = 5;

        SetIntensidad(nueva);
    }

    /// <summary>Establecer intensidad de protesta manualmente (0-5).</summary>
    public void SetIntensidad(int nivel)
    {
        nivel = Mathf.Clamp(nivel, 0, 5);
        if (nivel == intensidadActual) return;

        intensidadActual = nivel;
        OnIntensidadCambia?.Invoke(nivel);
        AplicarIntensidad(nivel);
    }

    // ── Aplicar nivel de intensidad ──────────────────────────────────────
    void AplicarIntensidad(int nivel)
    {
        if (nivel == _intensidadAnterior) return;
        _intensidadAnterior = nivel;

        if (_crActualizar != null) StopCoroutine(_crActualizar);
        _crActualizar = StartCoroutine(CoActualizarProps(nivel));
    }

    IEnumerator CoActualizarProps(int nivel)
    {
        // Primero recoger todo lo activo
        RecogerTodosLosProps();
        yield return null;

        // Spawning gradual para no congelar
        for (int z = 0; z < zonasManifestacion.Count; z++)
        {
            Vector3 centro = zonasManifestacion[z];

            if (!_activosPorZona.ContainsKey(z))
                _activosPorZona[z] = new List<GameObject>(32);

            // Nivel 1+: basura y papeleras volcadas
            if (nivel >= 1)
            {
                int cantidad = Mathf.CeilToInt(maxBasuraPorZona * (nivel / 5f));
                for (int i = 0; i < cantidad; i++)
                {
                    var go = ObtenerProp(CategoriaProps.Basura, ObtenerPrefabBasura(i));
                    ColocarEnZona(go, centro, radioMinimo * 0.5f, radioMaximo);
                    // Nivel 2+: voltear papeleras
                    if (nivel >= 2)
                        go.transform.rotation = Quaternion.Euler(UnityEngine.Random.Range(60f, 120f), UnityEngine.Random.Range(0f, 360f), 0f);
                    _activosPorZona[z].Add(go);
                }
                yield return null;
            }

            // Nivel 3+: barricadas
            if (nivel >= 3)
            {
                int cantidad = Mathf.CeilToInt(maxBarricadasPorZona * ((nivel - 2f) / 3f));
                for (int i = 0; i < cantidad; i++)
                {
                    var prefab = (i % 2 == 0) ? prefabContenedorMetal : prefabMesaPicnic;
                    var go = ObtenerProp(CategoriaProps.Barricada, prefab);
                    ColocarEnZona(go, centro, radioMinimo * 0.3f, radioMinimo);
                    // Barricadas bloqueando — rotacion hacia el centro
                    Vector3 dirCentro = (centro - go.transform.position);
                    dirCentro.y = 0;
                    if (dirCentro.sqrMagnitude > 0.01f)
                        go.transform.rotation = Quaternion.LookRotation(dirCentro) * Quaternion.Euler(0f, UnityEngine.Random.Range(-30f, 30f), 0f);
                    _activosPorZona[z].Add(go);
                }
                yield return null;
            }

            // Nivel 4+: muros destruidos y fuego
            if (nivel >= 4)
            {
                // Muros destruidos
                int muros = Mathf.CeilToInt(maxMurosDestruidosPorZona * ((nivel - 3f) / 2f));
                for (int i = 0; i < muros; i++)
                {
                    var go = ObtenerProp(CategoriaProps.MuroDestruido, prefabMuroDestruido);
                    ColocarEnZona(go, centro, radioMinimo * 0.4f, radioMaximo * 0.8f);
                    // Rotacion aleatoria realista
                    go.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(-5f, 5f));
                    _activosPorZona[z].Add(go);
                }

                // Fuego
                int fuegos = Mathf.CeilToInt(maxFuegosPorZona * ((nivel - 3f) / 2f));
                for (int i = 0; i < fuegos; i++)
                {
                    var go = ObtenerProp(CategoriaProps.Fuego, prefabFuego);
                    if (go == null)
                    {
                        // Fallback: crear fuego simple con luz naranja + particulas
                        go = CrearFuegoFallback();
                        _pool[CategoriaProps.Fuego].Enqueue(go);
                        go = ObtenerProp(CategoriaProps.Fuego, null);
                    }
                    ColocarEnZona(go, centro, radioMinimo * 0.2f, radioMinimo * 0.8f);
                    _activosPorZona[z].Add(go);
                }
                yield return null;
            }

            // Nivel 5: explosiones VFX
            if (nivel >= 5)
            {
                for (int i = 0; i < maxExplosionesPorZona; i++)
                {
                    var go = ObtenerProp(CategoriaProps.Explosion, prefabExplosionVFX);
                    ColocarEnZona(go, centro, radioMinimo * 0.3f, radioMaximo * 0.6f);
                    _activosPorZona[z].Add(go);
                }
                yield return null;
            }
        }

        _crActualizar = null;
    }

    // ── Pool: obtener / devolver ─────────────────────────────────────────
    GameObject ObtenerProp(CategoriaProps cat, GameObject prefab)
    {
        var cola = _pool[cat];

        // Intentar reutilizar del pool
        while (cola.Count > 0)
        {
            var reciclado = cola.Dequeue();
            if (reciclado != null)
            {
                reciclado.SetActive(true);
                return reciclado;
            }
        }

        // Crear nuevo
        GameObject go;
        if (prefab != null)
        {
            go = Instantiate(prefab, _parentProps);
        }
        else
        {
            // Fallback: cubo primitivo con color de escombros
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(_parentProps);
            go.transform.localScale = new Vector3(
                UnityEngine.Random.Range(0.3f, 1.2f),
                UnityEngine.Random.Range(0.2f, 0.6f),
                UnityEngine.Random.Range(0.3f, 1.2f));
            var r = go.GetComponent<Renderer>();
            if (r != null)
                r.material.color = new Color(0.35f, 0.3f, 0.25f); // gris escombros
        }

        go.layer = LayerMask.NameToLayer("Default");
        return go;
    }

    void DevolverAlPool(CategoriaProps cat, GameObject go)
    {
        if (go == null) return;
        go.SetActive(false);
        _pool[cat].Enqueue(go);
    }

    void RecogerTodosLosProps()
    {
        foreach (var kvp in _activosPorZona)
        {
            var lista = kvp.Value;
            for (int i = 0; i < lista.Count; i++)
            {
                var go = lista[i];
                if (go == null) continue;
                go.SetActive(false);
                // Determinar categoria por tag o nombre
                var cat = ClasificarProp(go);
                _pool[cat].Enqueue(go);
            }
            lista.Clear();
        }
    }

    CategoriaProps ClasificarProp(GameObject go)
    {
        // Clasificar por nombre del GO (prefijo establecido al crear)
        var nombre = go.name;
        if (nombre.Contains("Fuego") || nombre.Contains("Fire"))   return CategoriaProps.Fuego;
        if (nombre.Contains("Explo"))                               return CategoriaProps.Explosion;
        if (nombre.Contains("Muro")  || nombre.Contains("destroy")) return CategoriaProps.MuroDestruido;
        if (nombre.Contains("Contenedor_Metal") || nombre.Contains("Mesa")) return CategoriaProps.Barricada;
        return CategoriaProps.Basura;
    }

    // ── Colocacion en zona ───────────────────────────────────────────────
    void ColocarEnZona(GameObject go, Vector3 centro, float rMin, float rMax)
    {
        if (go == null) return;

        float angulo = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float radio  = UnityEngine.Random.Range(rMin, rMax);
        float x = centro.x + Mathf.Cos(angulo) * radio;
        float z = centro.z + Mathf.Sin(angulo) * radio;
        float y = AlturaTerreno(x, z);

        go.transform.position = new Vector3(x, y, z);
    }

    // ── Terreno ──────────────────────────────────────────────────────────
    static float AlturaTerreno(float x, float z)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain != null)
            return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
        return GeoDataAlsasua.ALT_FALLBACK;
    }

    // ── Prefab selector basura (alterna papelera / contenedor) ───────────
    GameObject ObtenerPrefabBasura(int indice)
    {
        if (indice % 3 == 0 && prefabContenedor != null) return prefabContenedor;
        if (prefabPapelera != null) return prefabPapelera;
        return prefabContenedor; // puede ser null — ObtenerProp crea fallback
    }

    // ── Fuego fallback (sin prefab asignado) ─────────────────────────────
    GameObject CrearFuegoFallback()
    {
        var go = new GameObject("Fuego_Fallback");
        go.transform.SetParent(_parentProps);

        // Luz puntual naranja parpadeante — HDRP requiere HDAdditionalLightData
        var luz = go.AddComponent<Light>();
        luz.type = LightType.Point;
        luz.color = new Color(1f, 0.55f, 0.1f);
        luz.range = 8f;
        var hdLight = go.AddComponent<HDAdditionalLightData>();
        hdLight.intensity = 600f;
        hdLight.lightUnit = LightUnit.Lux;

        // Particulas de humo simples
        var humoGO = new GameObject("Humo");
        humoGO.transform.SetParent(go.transform);
        humoGO.transform.localPosition = Vector3.up * 0.5f;

        var ps = humoGO.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 3f;
        main.startSpeed = 1.5f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        main.startColor = new Color(0.3f, 0.3f, 0.3f, 0.6f);
        main.maxParticles = 30;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 8f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 15f;
        shape.radius = 0.3f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(0.4f, 0.4f, 0.4f), 0f), new GradientColorKey(new Color(0.2f, 0.2f, 0.2f), 1f) },
            new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = grad;

        // Particulas de fuego
        var fuegoGO = new GameObject("Llamas");
        fuegoGO.transform.SetParent(go.transform);
        fuegoGO.transform.localPosition = Vector3.zero;

        var psF = fuegoGO.AddComponent<ParticleSystem>();
        var mainF = psF.main;
        mainF.startLifetime = 0.8f;
        mainF.startSpeed = 2f;
        mainF.startSize = new ParticleSystem.MinMaxCurve(0.3f, 1f);
        mainF.startColor = new Color(1f, 0.6f, 0.1f, 0.9f);
        mainF.maxParticles = 40;
        mainF.simulationSpace = ParticleSystemSimulationSpace.World;

        var emF = psF.emission;
        emF.rateOverTime = 20f;

        var shF = psF.shape;
        shF.shapeType = ParticleSystemShapeType.Cone;
        shF.angle = 25f;
        shF.radius = 0.4f;

        var colF = psF.colorOverLifetime;
        colF.enabled = true;
        var gradF = new Gradient();
        gradF.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.8f, 0.2f), 0f), new GradientColorKey(new Color(1f, 0.2f, 0f), 0.6f), new GradientColorKey(new Color(0.3f, 0.1f, 0f), 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) }
        );
        colF.color = gradF;

        // Componente parpadeo luz
        go.AddComponent<ParpadeadorLuzFuego>();

        go.SetActive(false);
        return go;
    }

    // ── Debug — cambiar intensidad con teclado ───────────────────────────
    #if UNITY_EDITOR || DEVELOPMENT_BUILD
    void Update()
    {
        // Numpad +/- para test rapido en desarrollo
        if (UnityEngine.InputSystem.Keyboard.current?.numpadPlusKey.wasPressedThisFrame == true)
            SetIntensidad(intensidadActual + 1);
        if (UnityEngine.InputSystem.Keyboard.current?.numpadMinusKey.wasPressedThisFrame == true)
            SetIntensidad(intensidadActual - 1);
    }
    #endif
}

// ═══════════════════════════════════════════════════════════════════════════
//  Componente auxiliar: parpadeo de luz para fuego fallback
// ═══════════════════════════════════════════════════════════════════════════
public class ParpadeadorLuzFuego : MonoBehaviour
{
    Light                _luz;
    HDAdditionalLightData _hdLight;
    float _baseIntensidad;
    float _offset;

    void Awake()
    {
        _luz     = GetComponent<Light>();
        _hdLight = GetComponent<HDAdditionalLightData>();
        _baseIntensidad = _hdLight != null ? _hdLight.intensity : (_luz != null ? _luz.intensity : 600f);
        _offset  = UnityEngine.Random.Range(0f, 100f);
    }

    void Update()
    {
        if (_luz == null) return;
        float ruido      = Mathf.PerlinNoise(Time.time * 8f + _offset, _offset * 0.5f);
        float intensidad = _baseIntensidad * (0.6f + ruido * 0.8f);
        if (_hdLight != null) _hdLight.intensity = intensidad;
        else _luz.intensity = intensidad;
    }
}
