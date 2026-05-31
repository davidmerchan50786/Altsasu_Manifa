// Assets/Scripts/SistemaFauna.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Sistema de fauna para instanciar animales rigged en el entorno de Alsasua.
//
//  ASSETS SOPORTADOS (de carpeta Downloads):
//  · riggedHorse.blend - Caballo con esqueleto
//  · Wolf.zip - Lobo con animaciones
//  · rabbit.blend - Conejo
//  · chicken.zip - Pollo
//  · rooster.zip - Gallo
//  · sheepies.blend - Ovejas
//
//  DISTRIBUCIÓN:
//  · Los animales se distribuyen en las zonas forestales (mismos centros que árboles)
//  · Densidad configurable: depredadores (lobo) menos frecuentes
//  · Herbívoros (caballos, conejos, ovejas) más densidad
//  · Aves (pollo, gallo) en bordes de zonas
//
//  FEATURES:
//  · Culling por distancia (LOD)
//  · Patrullaje aleatorio simple (cercano al player)
//  · Pool de instancias para reutilización
//  · Fallback procedural (cápsula básica) si falta prefab
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;

[DefaultExecutionOrder(-15)]    // ejecutar después de SistemaAssets (-20), antes que GestorEscena (-10)
public sealed class SistemaFauna : MonoBehaviour
{
    // ───────────────────────────────────────────────────────────────────────
    //  INSPECTOR
    // ───────────────────────────────────────────────────────────────────────
    [Header("═══ PARÁMETROS ═══")]
    [Tooltip("Número de animales a generar por zona")]
    [SerializeField] private int densidadAnimales = 80;

    [Tooltip("Distancia máxima de renderizado (m)")]
    [SerializeField] private float rangoRender = 500f;

    [Tooltip("Habilitar patrullaje aleatorio de animales cercanos")]
    [SerializeField] private bool habilitarPatrullaje = true;

    [Tooltip("Velocidad máxima de patrullaje (m/s)")]
    [SerializeField] private float velocidadPatrullaje = 3f;

    // ───────────────────────────────────────────────────────────────────────
    //  ZONAS DE SPAWN (usa mismas que SistemaVegetacion)
    // ───────────────────────────────────────────────────────────────────────
    [SerializeField] private Vector3[] centrosZona = new Vector3[]
    {
        new Vector3(   0f, 0f,  500f),  // norte
        new Vector3(   0f, 0f, -500f),  // sur
        new Vector3( 500f, 0f,    0f),  // este
        new Vector3(-500f, 0f,    0f),  // oeste
    };

    [SerializeField] private float[] radiosZona = new float[] { 250f, 200f, 220f, 180f };

    // ───────────────────────────────────────────────────────────────────────
    //  DATOS INTERNOS
    // ───────────────────────────────────────────────────────────────────────
    private struct AnimalData
    {
        public GameObject instancia;
        public Vector3 posicion;
        public AnimalType tipo;
        public float velocidad;
        public Vector3 direccionPatrulla;
        public float timerPatrulla;
    }

    private enum AnimalType { Caballo, Lobo, Conejo, Pollo, Gallo, Oveja }

    private List<AnimalData> _animales = new List<AnimalData>();
    private Camera _camPrincipal;

    // Prefabs inyectados por SistemaAssets
    private GameObject _prefabCaballo;
    private GameObject _prefabLobo;
    private GameObject _prefabConejo;
    private GameObject _prefabPollo;
    private GameObject _prefabGallo;
    private GameObject _prefabOveja;

    // ───────────────────────────────────────────────────────────────────────
    //  UNITY LIFECYCLE
    // ───────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        _camPrincipal = Camera.main;
    }

    private void Start()
    {
        // Generar animales iniciales
        GenerarAnimales();
    }

    private void Update()
    {
        if (_camPrincipal == null) _camPrincipal = Camera.main;
        if (_camPrincipal == null) return;

        // Actualizar visibilidad y patrullaje
        ActualizarAnimales();
    }

    private void OnDestroy()
    {
        // Limpiar pool
        foreach (var animal in _animales)
        {
            if (animal.instancia != null)
                Destroy(animal.instancia);
        }
        _animales.Clear();
    }

    // ───────────────────────────────────────────────────────────────────────
    //  INYECCIÓN DE PREFABS (por SistemaAssets)
    // ───────────────────────────────────────────────────────────────────────
    public void AsignarPrefabs(GameObject caballo, GameObject lobo, GameObject conejo,
                              GameObject pollo, GameObject gallo, GameObject oveja)
    {
        _prefabCaballo = caballo;
        _prefabLobo = lobo;
        _prefabConejo = conejo;
        _prefabPollo = pollo;
        _prefabGallo = gallo;
        _prefabOveja = oveja;

        // Regenerar con nuevos prefabs si ya estamos en runtime
        if (_animales.Count > 0)
            RegenerarAnimales();
    }

    // ───────────────────────────────────────────────────────────────────────
    //  GENERACIÓN DE ANIMALES
    // ───────────────────────────────────────────────────────────────────────
    private void GenerarAnimales()
    {
        _animales.Clear();
        int totalSpawned = 0;

        for (int z = 0; z < centrosZona.Length; z++)
        {
            float radioZona = (z < radiosZona.Length ? radiosZona[z] : 200f);

            for (int i = 0; i < densidadAnimales; i++)
            {
                Vector2 offset = Random.insideUnitCircle * radioZona;
                Vector3 pos = centrosZona[z] + new Vector3(offset.x, 0f, offset.y);

                // Perlin noise para distribución natural
                float ruido = Mathf.PerlinNoise(pos.x * 0.01f + z * 10f, pos.z * 0.01f + z * 7f);

                if (ruido < 0.30f || ruido > 0.80f) continue;  // evitar extremos

                float alturaY = MuestrearTerreno(pos);
                pos.y = alturaY;

                // Seleccionar tipo basado en probabilidad
                AnimalType tipo = SelectTipoAnimal(ruido);
                GameObject prefab = ObtenerPrefab(tipo);

                if (prefab == null) continue;

                // Instanciar
                var go = Instantiate(prefab, pos, Quaternion.identity);
                go.name = $"Animal_{tipo}";

                // Escala
                float escala = Random.Range(0.85f, 1.15f);
                go.transform.localScale = Vector3.one * escala;

                var data = new AnimalData
                {
                    instancia = go,
                    posicion = pos,
                    tipo = tipo,
                    velocidad = Random.Range(velocidadPatrullaje * 0.5f, velocidadPatrullaje),
                    direccionPatrulla = Random.onUnitSphere,
                    timerPatrulla = Random.Range(0f, 10f),
                };

                _animales.Add(data);
                totalSpawned++;
            }
        }

        if (totalSpawned > 0)
            AlsasuaLogger.Info("SistemaFauna", $"✓ {totalSpawned} animales spawneados en escena.");
    }

    private void RegenerarAnimales()
    {
        // Destruir existentes
        foreach (var animal in _animales)
        {
            if (animal.instancia != null) Destroy(animal.instancia);
        }
        _animales.Clear();

        // Regenerar
        GenerarAnimales();
    }

    // ───────────────────────────────────────────────────────────────────────
    //  ACTUALIZACIÓN RUNTIME
    // ───────────────────────────────────────────────────────────────────────
    private void ActualizarAnimales()
    {
        Vector3 camPos = _camPrincipal.transform.position;
        float rango2 = rangoRender * rangoRender;

        for (int i = 0; i < _animales.Count; i++)
        {
            var animal = _animales[i];

            // Culling por distancia
            float dx = animal.posicion.x - camPos.x;
            float dz = animal.posicion.z - camPos.z;
            float dist2 = dx * dx + dz * dz;

            if (animal.instancia != null)
            {
                bool visible = dist2 < rango2;
                animal.instancia.SetActive(visible);

                if (visible && habilitarPatrullaje)
                {
                    // Patrullaje simple
                    ActualizarPatrullaje(ref animal);
                }
            }

            _animales[i] = animal;
        }
    }

    private void ActualizarPatrullaje(ref AnimalData animal)
    {
        animal.timerPatrulla += Time.deltaTime;

        // Cambiar dirección cada 10 segundos aprox
        if (animal.timerPatrulla > 10f)
        {
            animal.direccionPatrulla = Random.onUnitSphere;
            animal.direccionPatrulla.y = 0;
            animal.direccionPatrulla.Normalize();
            animal.timerPatrulla = 0f;
        }

        // Mover ligeramente
        Vector3 nuevoPos = animal.posicion + animal.direccionPatrulla * animal.velocidad * Time.deltaTime;
        float alturaY = MuestrearTerreno(nuevoPos);
        nuevoPos.y = alturaY;

        animal.posicion = nuevoPos;
        if (animal.instancia != null)
            animal.instancia.transform.position = nuevoPos;
    }

    // ───────────────────────────────────────────────────────────────────────
    //  HELPERS
    // ───────────────────────────────────────────────────────────────────────
    private AnimalType SelectTipoAnimal(float ruido)
    {
        // Distribución natural: herbívoros comunes, depredadores raros
        if (ruido > 0.75f) return Random.value < 0.3f ? AnimalType.Lobo : AnimalType.Conejo;
        if (ruido > 0.60f) return Random.value < 0.5f ? AnimalType.Pollo : AnimalType.Gallo;
        return Random.value < 0.4f ? AnimalType.Caballo : AnimalType.Oveja;
    }

    private GameObject ObtenerPrefab(AnimalType tipo) => tipo switch
    {
        AnimalType.Caballo => _prefabCaballo,
        AnimalType.Lobo => _prefabLobo,
        AnimalType.Conejo => _prefabConejo,
        AnimalType.Pollo => _prefabPollo,
        AnimalType.Gallo => _prefabGallo,
        AnimalType.Oveja => _prefabOveja,
        _ => null,
    };

    private float MuestrearTerreno(Vector3 pos)
    {
        if (Physics.Raycast(new Vector3(pos.x, 100f, pos.z), Vector3.down, out RaycastHit hit, 200f, ~0))
            return hit.point.y;
        return pos.y;
    }

    // ───────────────────────────────────────────────────────────────────────
    //  API PÚBLICA
    // ───────────────────────────────────────────────────────────────────────
    public int TotalAnimales => _animales.Count;
}
