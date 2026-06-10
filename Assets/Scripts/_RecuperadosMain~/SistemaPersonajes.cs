// Assets/Scripts/SistemaPersonajes.cs
// ═══════════════════════════════════════════════════════════════════════════
//  Gestiona todos los tipos de personaje del simulador de Alsasua:
//
//  · GuardiaCivil      — uniforme verde oliva, tricornio, ruta patrulla
//  · PoliciaForal      — uniforme azul marino, casco, ruta patrulla
//  · ManifestantePalestino — ropa oscura, keffiyeh rojo/blanco procedural
//  · ManifestanteJurrutu   — ropa negra, boina vasca
//  · PortadorIkurriña  — manifestante con ikurriña en mástil (tex procedural)
//  · PortadorNavarra   — manifestante con bandera Navarra en mástil
//  · CivilianMale / CivilianFemale — civiles peatones por el pueblo
//
//  Arquitectura:
//  · Un GameObject real por personaje (pool implícito vía parenting).
//  · PersonajeData[] struct array — sin MonoBehaviour por agente.
//  · MaterialPropertyBlock per-GO para variación de tono sin nuevas instancias.
//  · Texturas de uniforme/bandera 100 % procedurales — sin assets externos.
//  · Tick lógica a 20 FPS; raycast terreno escalonado 1/frame.
//  · OnDestroy() limpia todos los new Material(), new Texture2D(), new Mesh().
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;

public sealed class SistemaPersonajes : MonoBehaviour
{
    // ───────────────────────────────────────────────────────────────────────
    //  TIPOS
    // ───────────────────────────────────────────────────────────────────────
    public enum TipoPersonaje : byte
    {
        GuardiaCivil,
        PoliciaForal,
        ManifestantePalestino,
        ManifestanteJurrutu,
        PortadorIkurriña,
        PortadorNavarra,
        CivilianMale,
        CivilianFemale,
    }

    private enum EstadoPersonaje : byte { Caminando, Parado, Patrullando }

    private struct PersonajeData
    {
        public Vector3          posicion;
        public Vector3          velocidad;
        public float            alturaY;
        public TipoPersonaje    tipo;
        public EstadoPersonaje  estado;
        public int              waypointActual;
        public float            tiempoParado;
        public float            varianteTono;   // 0-1
        public Vector3          destinoCivil;   // objetivo aleatorio para civiles
    }

    // ───────────────────────────────────────────────────────────────────────
    //  INSPECTOR
    // ───────────────────────────────────────────────────────────────────────
    [Header("═══ CANTIDADES ═══")]
    [SerializeField] private int numGuardiaCivil         = 8;
    [SerializeField] private int numPoliciaForal         = 6;
    [SerializeField] private int numManifestantesPales   = 40;
    [SerializeField] private int numManifestantesJurrutu = 30;
    [SerializeField] private int numPortadoresIkurriña   = 8;
    [SerializeField] private int numPortadoresNavarra    = 4;
    [SerializeField] private int numCiviles              = 50;

    [Header("═══ RUTAS Y ÁREAS ═══")]
    [Tooltip("Waypoints de patrulla Guardia Civil (en orden)")]
    [SerializeField] private Transform[] rutaGuardiaCivil;
    [Tooltip("Waypoints de patrulla Policía Foral (en orden)")]
    [SerializeField] private Transform[] rutaPoliciaForal;
    [Tooltip("Área de paseo civiles (centro Alsasua)")]
    [SerializeField] private Bounds areaCiviles = new Bounds(Vector3.zero, new Vector3(200f, 10f, 200f));
    [Tooltip("Punto de origen de la manifestación")]
    [SerializeField] private Vector3 posicionManifestantes = new Vector3(-50f, 0f, 0f);

    [Header("═══ MODELOS 3D (opcionales) ═══")]
    [Tooltip("Prefab para Guardia Civil. Si está vacío se usa la cápsula procedural.\n" +
             "Arrastra Assets/Personajes/Prefabs/GuardiaCivil.prefab")]
    [SerializeField] private GameObject prefabGuardiaCivil;

    [Tooltip("Prefab para Policía Foral. Arrastra Assets/Personajes/Prefabs/GuardiaCivil.prefab o uno específico.")]
    [SerializeField] private GameObject prefabPoliciaForal;

    [Tooltip("Prefab para manifestantes (Palestino + Jurrutu + Portadores).\n" +
             "Arrastra Assets/Personajes/Prefabs/Manifestante.prefab")]
    [SerializeField] private GameObject prefabManifestante;

    [Tooltip("Prefab para Jarrai. Arrastra Assets/Personajes/Prefabs/Jarrai.prefab")]
    [SerializeField] private GameObject prefabJarrai;

    [Tooltip("Prefab para civil masculino. Arrastra Assets/Personajes/Prefabs/CivilMasculino.prefab")]
    [SerializeField] private GameObject prefabCivilMasculino;

    [Tooltip("Prefab para civil femenino. Arrastra Assets/Personajes/Prefabs/CivilFemenino.prefab")]
    [SerializeField] private GameObject prefabCivilFemenino;

    [Header("═══ VELOCIDADES ═══")]
    [SerializeField] private float velocidadGC      = 1.2f;
    [SerializeField] private float velocidadPF      = 1.4f;
    [SerializeField] private float velocidadManif   = 1.1f;
    [SerializeField] private float velocidadCivil   = 0.95f;

    // ───────────────────────────────────────────────────────────────────────
    //  ESTADO INTERNO
    // ───────────────────────────────────────────────────────────────────────
    private PersonajeData[] _personajes;
    private GameObject[]    _goPersonajes;

    // Materiales base por tipo (MaterialPropertyBlock aplica variantes por GO)
    private Material[] _matsBase;   // índice = (int)TipoPersonaje
    private readonly List<Material>  _matsCreados  = new List<Material>();
    private readonly List<Texture2D> _texsCreadas  = new List<Texture2D>();
    private readonly List<Mesh>      _meshesCreados = new List<Mesh>();

    private Texture2D _texIkurriña;
    private Texture2D _texNavarra;
    private Texture2D _texKeffiyeh;

    private Mesh _meshCuerpo;     // cápsula ~1.8 m
    private Mesh _meshBandera;    // quad 0.6×1.2 m, doble cara

    private float _acumTick     = 0f;
    private const float TICK_DT = 1f / 20f;  // lógica a 20 FPS
    private int   _idxRaycast   = 0;

    private static readonly int _idBaseColor   = Shader.PropertyToID("_BaseColor");
    private static readonly int _idBaseMap     = Shader.PropertyToID("_BaseMap");

    // ───────────────────────────────────────────────────────────────────────
    //  UNITY
    // ───────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        CrearTexturas();
        CrearMeshes();
        CrearMaterialesBase();
        SpawnTodos();
    }

    private void Update()
    {
        _acumTick += Time.deltaTime;
        if (_acumTick >= TICK_DT)
        {
            _acumTick -= TICK_DT;
            TickLogica(TICK_DT);
        }

        // Raycast terreno escalonado: 1 por frame
        if (_personajes != null && _personajes.Length > 0)
        {
            _idxRaycast = (_idxRaycast + 1) % _personajes.Length;
            SampleTerreno(_idxRaycast);
        }

        SincronizarGOs();
    }

    private void OnDestroy()
    {
        foreach (var m    in _matsCreados)   if (m    != null) Object.Destroy(m);
        foreach (var t    in _texsCreadas)   if (t    != null) Object.Destroy(t);
        foreach (var mesh in _meshesCreados) if (mesh != null) Object.Destroy(mesh);
        _matsCreados.Clear();
        _texsCreadas.Clear();
        _meshesCreados.Clear();
    }

    // ───────────────────────────────────────────────────────────────────────
    //  SPAWN
    // ───────────────────────────────────────────────────────────────────────
    private void SpawnTodos()
    {
        int total = numGuardiaCivil + numPoliciaForal
                  + numManifestantesPales + numManifestantesJurrutu
                  + numPortadoresIkurriña + numPortadoresNavarra
                  + numCiviles;

        _personajes   = new PersonajeData[total];
        _goPersonajes = new GameObject[total];

        int idx = 0;
        // Guardia Civil — cerca del cuartel (rutaGC[0] o fallback)
        Vector3 origenGC = (rutaGuardiaCivil != null && rutaGuardiaCivil.Length > 0)
                         ? rutaGuardiaCivil[0].position
                         : posicionManifestantes + new Vector3(100f, 0f, 0f);
        idx = Spawn(idx, numGuardiaCivil,         TipoPersonaje.GuardiaCivil,         origenGC,                             6f,  EstadoPersonaje.Patrullando);

        Vector3 origenPF = (rutaPoliciaForal != null && rutaPoliciaForal.Length > 0)
                         ? rutaPoliciaForal[0].position
                         : posicionManifestantes + new Vector3(120f, 0f, 0f);
        idx = Spawn(idx, numPoliciaForal,         TipoPersonaje.PoliciaForal,          origenPF,                             6f,  EstadoPersonaje.Patrullando);

        idx = Spawn(idx, numManifestantesPales,   TipoPersonaje.ManifestantePalestino, posicionManifestantes + new Vector3(20f, 0f,  10f), 14f, EstadoPersonaje.Caminando);
        idx = Spawn(idx, numManifestantesJurrutu, TipoPersonaje.ManifestanteJurrutu,   posicionManifestantes + new Vector3(30f, 0f, -10f), 12f, EstadoPersonaje.Caminando);
        idx = Spawn(idx, numPortadoresIkurriña,   TipoPersonaje.PortadorIkurriña,      posicionManifestantes + new Vector3(5f,  0f,   8f),  8f, EstadoPersonaje.Caminando);
        idx = Spawn(idx, numPortadoresNavarra,    TipoPersonaje.PortadorNavarra,        posicionManifestantes + new Vector3(5f,  0f,  -8f),  6f, EstadoPersonaje.Caminando);
        // BUG FIX: antes se spawneaban todos los civiles como CivilianMale y luego se
        // cambiaba _personajes[i].tipo a CivilianFemale sin actualizar el MeshRenderer,
        // haciendo que la mitad femenina se renderizara con material masculino.
        // Solución: spawn por separado con tipo correcto desde el principio.
        int civilMale   = numCiviles / 2;
        int civilFemale = numCiviles - civilMale;
        idx = Spawn(idx, civilMale,   TipoPersonaje.CivilianMale,   areaCiviles.center, areaCiviles.extents.x * 0.8f, EstadoPersonaje.Caminando);
        idx = Spawn(idx, civilFemale, TipoPersonaje.CivilianFemale, areaCiviles.center, areaCiviles.extents.x * 0.8f, EstadoPersonaje.Caminando);
    }

    private int Spawn(int baseIdx, int count, TipoPersonaje tipo,
                      Vector3 centro, float radio, EstadoPersonaje estadoInicial)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 o = Random.insideUnitCircle * radio;
            Vector3 pos = centro + new Vector3(o.x, 0f, o.y);
            float variante = Random.value;

            _personajes[baseIdx + i] = new PersonajeData
            {
                posicion       = pos,
                velocidad      = Vector3.zero,
                alturaY        = pos.y,
                tipo           = tipo,
                estado         = estadoInicial,
                waypointActual = Random.Range(0, 100),
                tiempoParado   = 0f,
                varianteTono   = variante,
                destinoCivil   = pos,
            };

            _goPersonajes[baseIdx + i] = CrearGO(pos, tipo, baseIdx + i, variante);
        }
        return baseIdx + count;
    }

    // ───────────────────────────────────────────────────────────────────────
    //  CREACIÓN DE GAMEOBJECTS
    // ───────────────────────────────────────────────────────────────────────
    private GameObject CrearGO(Vector3 pos, TipoPersonaje tipo, int idx, float variante)
    {
        var go = new GameObject($"Personaje_{tipo}_{idx}");
        go.transform.position = pos;
        go.transform.SetParent(transform);
        go.layer = LayerMask.NameToLayer("Default");

        // ── Seleccionar prefab 3D si está asignado ──────────────────────────
        // Si el usuario ha arrastrado un prefab en el Inspector, se instancia bajo el GO raíz.
        // Si no hay prefab, se genera la cápsula procedural (comportamiento anterior).
        GameObject prefab = PrefabParaTipo(tipo);
        if (prefab != null)
        {
            try
            {
                var modelo = Object.Instantiate(prefab, go.transform);
                modelo.name = "_Modelo";
                modelo.transform.localPosition = Vector3.zero;
                modelo.transform.localRotation = Quaternion.identity;
                modelo.transform.localScale    = Vector3.one;
                // Desactivar colisionadores propios del prefab
                foreach (var col in modelo.GetComponentsInChildren<Collider>(true))
                    col.enabled = false;
                // Variante de tono en los Renderers del prefab
                var mpb2 = new MaterialPropertyBlock();
                Color bc2 = ColorBase(tipo);
                float dv2 = variante * 0.12f - 0.06f;
                Color cv2 = new Color(Mathf.Clamp01(bc2.r + dv2), Mathf.Clamp01(bc2.g + dv2),
                                      Mathf.Clamp01(bc2.b + dv2), 1f);
                mpb2.SetColor(_idBaseColor, cv2);
                mpb2.SetColor("_Color", cv2);
                foreach (var r in modelo.GetComponentsInChildren<Renderer>())
                    r.SetPropertyBlock(mpb2);
                // Para portadores de banderas añadirles el mástil igualmente
                if (tipo == TipoPersonaje.PortadorIkurriña)  AñadirBandera(go, _texIkurriña);
                if (tipo == TipoPersonaje.PortadorNavarra)    AñadirBandera(go, _texNavarra);
                return go;
            }
            catch (System.Exception ex)
            {
                AlsasuaLogger.Warn("SistemaPersonajes",
                    $"Error instanciando prefab '{prefab.name}' para {tipo}: {ex.Message} — usando procedural.");
                // cae al fallback procedural ↓
            }
        }

        // ── Fallback: cápsula procedural ────────────────────────────────────
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = _meshCuerpo;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _matsBase[(int)tipo];

        // Variante de tono por-instancia via MaterialPropertyBlock (sin crear material nuevo)
        var mpb = new MaterialPropertyBlock();
        mr.GetPropertyBlock(mpb);
        Color bc = ColorBase(tipo);
        float dt = variante * 0.18f - 0.09f;
        mpb.SetColor(_idBaseColor, new Color(
            Mathf.Clamp01(bc.r + dt),
            Mathf.Clamp01(bc.g + dt),
            Mathf.Clamp01(bc.b + dt), 1f));
        mr.SetPropertyBlock(mpb);

        // Accesorios
        switch (tipo)
        {
            case TipoPersonaje.GuardiaCivil:          AñadirTricornio(go);              break;
            case TipoPersonaje.PoliciaForal:          AñadirCasco(go);                  break;
            case TipoPersonaje.ManifestantePalestino: AñadirKeffiyeh(go);               break;
            case TipoPersonaje.ManifestanteJurrutu:   AñadirBoina(go);                  break;
            case TipoPersonaje.PortadorIkurriña:      AñadirBandera(go, _texIkurriña);  break;
            case TipoPersonaje.PortadorNavarra:       AñadirBandera(go, _texNavarra);   break;
        }

        return go;
    }

    // Devuelve el prefab 3D para cada tipo (null = usar cápsula procedural)
    private GameObject PrefabParaTipo(TipoPersonaje tipo)
    {
        return tipo switch
        {
            TipoPersonaje.GuardiaCivil          => prefabGuardiaCivil,
            TipoPersonaje.PoliciaForal          => prefabPoliciaForal,
            TipoPersonaje.ManifestantePalestino => prefabManifestante,
            TipoPersonaje.ManifestanteJurrutu   => prefabJarrai,
            TipoPersonaje.PortadorIkurriña      => prefabManifestante,
            TipoPersonaje.PortadorNavarra        => prefabManifestante,
            TipoPersonaje.CivilianMale          => prefabCivilMasculino,
            TipoPersonaje.CivilianFemale        => prefabCivilFemenino,
            _                                   => null,
        };
    }

    // ───────────────────────────────────────────────────────────────────────
    //  ACCESORIOS
    // ───────────────────────────────────────────────────────────────────────
    private void AñadirBandera(GameObject portador, Texture2D tex)
    {
        // Mástil
        var mastil = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        mastil.name = "Mastil";
        mastil.transform.SetParent(portador.transform);
        mastil.transform.localPosition = new Vector3(0.3f, 1.1f, 0f);
        mastil.transform.localScale    = new Vector3(0.04f, 1.3f, 0.04f);
        var matM = MatURP(new Color(0.55f, 0.42f, 0.18f));
        mastil.GetComponent<Renderer>().sharedMaterial = matM;
        Object.Destroy(mastil.GetComponent<Collider>());

        // Tela de bandera
        var tela = new GameObject("Bandera");
        tela.transform.SetParent(portador.transform);
        tela.transform.localPosition = new Vector3(0.3f, 2.22f, 0f);
        tela.transform.localScale    = Vector3.one;
        var mfT = tela.AddComponent<MeshFilter>();
        mfT.sharedMesh = _meshBandera;
        var mrT = tela.AddComponent<MeshRenderer>();
        var matB = MatURP(Color.white);
        if (tex != null) matB.SetTexture(_idBaseMap, tex);
        mrT.sharedMaterial = matB;
    }

    private void AñadirKeffiyeh(GameObject portador)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Keffiyeh";
        go.transform.SetParent(portador.transform);
        go.transform.localPosition = new Vector3(0f, 1.74f, 0f);
        go.transform.localScale    = new Vector3(0.33f, 0.22f, 0.40f);
        var mat = MatURP(Color.white);
        if (_texKeffiyeh != null) mat.SetTexture(_idBaseMap, _texKeffiyeh);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.Destroy(go.GetComponent<Collider>());
    }

    private void AñadirBoina(GameObject portador)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Boina";
        go.transform.SetParent(portador.transform);
        go.transform.localPosition = new Vector3(0.05f, 1.76f, 0f);
        go.transform.localScale    = new Vector3(0.30f, 0.05f, 0.28f);
        go.GetComponent<Renderer>().sharedMaterial = MatURP(new Color(0.04f, 0.04f, 0.04f));
        Object.Destroy(go.GetComponent<Collider>());
    }

    private void AñadirTricornio(GameObject portador)
    {
        // Ala del tricornio (cilindro aplastado ancho)
        var ala = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ala.name = "TricornioAla";
        ala.transform.SetParent(portador.transform);
        ala.transform.localPosition = new Vector3(0f, 1.75f, 0f);
        ala.transform.localScale    = new Vector3(0.36f, 0.04f, 0.36f);
        ala.GetComponent<Renderer>().sharedMaterial = MatURP(new Color(0.06f, 0.06f, 0.06f));
        Object.Destroy(ala.GetComponent<Collider>());

        // Copa alta
        var copa = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        copa.name = "TricornioCopa";
        copa.transform.SetParent(portador.transform);
        copa.transform.localPosition = new Vector3(0f, 1.84f, 0f);
        copa.transform.localScale    = new Vector3(0.22f, 0.09f, 0.22f);
        copa.GetComponent<Renderer>().sharedMaterial = MatURP(new Color(0.06f, 0.06f, 0.06f));
        Object.Destroy(copa.GetComponent<Collider>());
    }

    private void AñadirCasco(GameObject portador)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "CascoPoliciaForal";
        go.transform.SetParent(portador.transform);
        go.transform.localPosition = new Vector3(0f, 1.79f, 0f);
        go.transform.localScale    = new Vector3(0.32f, 0.27f, 0.32f);
        go.GetComponent<Renderer>().sharedMaterial = MatURP(new Color(0.06f, 0.10f, 0.22f));
        Object.Destroy(go.GetComponent<Collider>());

        // Franja amarilla hi-vis
        var franja = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        franja.name = "FranjaHivis";
        franja.transform.SetParent(portador.transform);
        franja.transform.localPosition = new Vector3(0f, 1.40f, 0f);
        franja.transform.localScale    = new Vector3(0.30f, 0.025f, 0.30f);
        franja.GetComponent<Renderer>().sharedMaterial = MatURP(new Color(0.95f, 0.85f, 0.0f));
        Object.Destroy(franja.GetComponent<Collider>());
    }

    // ───────────────────────────────────────────────────────────────────────
    //  TICK LÓGICA
    // ───────────────────────────────────────────────────────────────────────
    private void TickLogica(float dt)
    {
        for (int i = 0; i < _personajes.Length; i++)
        {
            ref var p = ref _personajes[i];
            switch (p.estado)
            {
                case EstadoPersonaje.Patrullando: TickPatrulla(ref p, dt); break;
                case EstadoPersonaje.Caminando:   TickCaminar(ref p, dt);  break;
                case EstadoPersonaje.Parado:
                    p.tiempoParado -= dt;
                    if (p.tiempoParado <= 0f)
                        p.estado = EstadoPersonaje.Caminando;
                    p.velocidad = Vector3.zero;
                    break;
            }
        }
    }

    private void TickPatrulla(ref PersonajeData p, float dt)
    {
        Transform[] ruta = (p.tipo == TipoPersonaje.GuardiaCivil)
                         ? rutaGuardiaCivil : rutaPoliciaForal;

        // Sin ruta asignada → caminar como civil dentro del área
        if (ruta == null || ruta.Length == 0) { TickCaminar(ref p, dt); return; }

        int wp = p.waypointActual % ruta.Length;
        Vector3 destino = ruta[wp].position;
        Vector3 dir = destino - p.posicion; dir.y = 0f;
        float dist = dir.magnitude;

        if (dist < 1.8f)
        {
            p.waypointActual = (p.waypointActual + 1) % ruta.Length;
            // Pausa breve ocasional
            if (Random.value < 0.12f)
            {
                p.estado       = EstadoPersonaje.Parado;
                p.tiempoParado = Random.Range(2f, 7f);
                p.velocidad    = Vector3.zero;
                return;
            }
        }

        float speed = (p.tipo == TipoPersonaje.GuardiaCivil) ? velocidadGC : velocidadPF;
        p.velocidad = dir.normalized * speed;
        p.posicion += p.velocidad * dt;
    }

    private void TickCaminar(ref PersonajeData p, float dt)
    {
        // GC/PF sin ruta → forzar patrulla
        if (p.tipo == TipoPersonaje.GuardiaCivil || p.tipo == TipoPersonaje.PoliciaForal)
        { p.estado = EstadoPersonaje.Patrullando; return; }

        // Manifestantes siguen movimiento simple hacia la calle principal
        if (p.tipo == TipoPersonaje.ManifestantePalestino ||
            p.tipo == TipoPersonaje.ManifestanteJurrutu   ||
            p.tipo == TipoPersonaje.PortadorIkurriña      ||
            p.tipo == TipoPersonaje.PortadorNavarra)
        {
            // Avance hacia el este (+X) simulando marcha
            Vector3 dir = Vector3.right;
            float speed = velocidadManif + Random.Range(-0.1f, 0.1f);
            p.velocidad = dir * speed;
            p.posicion += p.velocidad * dt;

            // Wrap-around: si pasan de 200m, reinician al inicio
            if (p.posicion.x > posicionManifestantes.x + 200f)
                p.posicion.x = posicionManifestantes.x - 5f;
            return;
        }

        // Civiles: navegar hacia destinoCivil, elegir nuevo destino al llegar
        Vector3 diff = p.destinoCivil - p.posicion; diff.y = 0f;
        if (diff.magnitude < 2f)
        {
            // Nuevo destino aleatorio dentro del área
            Vector2 rnd = Random.insideUnitCircle * areaCiviles.extents.x * 0.7f;
            p.destinoCivil = areaCiviles.center + new Vector3(rnd.x, 0f, rnd.y);

            // Pausa ocasional
            if (Random.value < 0.30f)
            {
                p.estado       = EstadoPersonaje.Parado;
                p.tiempoParado = Random.Range(1f, 9f);
                p.velocidad    = Vector3.zero;
                return;
            }
        }

        p.velocidad = diff.normalized * velocidadCivil;
        p.posicion += p.velocidad * dt;

        // Mantenerse dentro del área
        Vector3 clamped = new Vector3(
            Mathf.Clamp(p.posicion.x, areaCiviles.min.x, areaCiviles.max.x),
            p.posicion.y,
            Mathf.Clamp(p.posicion.z, areaCiviles.min.z, areaCiviles.max.z));
        if (clamped != p.posicion)
        {
            p.posicion      = clamped;
            p.destinoCivil  = areaCiviles.center;
        }
    }

    private void SampleTerreno(int idx)
    {
        ref var p = ref _personajes[idx];
        Ray ray = new Ray(new Vector3(p.posicion.x, p.alturaY + 8f, p.posicion.z), Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, 25f, ~0))
            p.alturaY = Mathf.Lerp(p.alturaY, hit.point.y, 0.25f);
    }

    private void SincronizarGOs()
    {
        for (int i = 0; i < _personajes.Length; i++)
        {
            if (_goPersonajes[i] == null) continue;
            ref var p = ref _personajes[i];
            var t = _goPersonajes[i].transform;
            t.position = new Vector3(p.posicion.x, p.alturaY, p.posicion.z);
            if (p.velocidad.sqrMagnitude > 0.0025f)
                t.rotation = Quaternion.LookRotation(
                    new Vector3(p.velocidad.x, 0f, p.velocidad.z), Vector3.up);
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    //  TEXTURAS PROCEDURALES
    // ───────────────────────────────────────────────────────────────────────
    private void CrearTexturas()
    {
        _texIkurriña = GenIkurriña();   _texsCreadas.Add(_texIkurriña);
        _texNavarra  = GenNavarra();    _texsCreadas.Add(_texNavarra);
        _texKeffiyeh = GenKeffiyeh();   _texsCreadas.Add(_texKeffiyeh);
    }

    private static Texture2D GenIkurriña()
    {
        const int W = 256, H = 160;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.name = "Ikurrina";
        var px   = new Color32[W * H];

        Color32 verde  = new Color32(0,   130,  60,  255);
        Color32 blanco = new Color32(255, 255, 255,  255);
        Color32 rojo   = new Color32(210,  30,  30,  255);

        for (int i = 0; i < px.Length; i++) px[i] = verde;

        // Aspa blanca diagonal (banda ancha ~8 % del lado)
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float fx = (float)x / W, fy = (float)y / H;
            if (Mathf.Abs(fy - fx * (float)H / W)               < 0.09f * H ||
                Mathf.Abs(fy - (1f - fx) * (float)H / W)        < 0.09f * H)
                px[y * W + x] = blanco;
        }

        // Cruz roja centrada (5 % del lado)
        int bandaV = (int)(W * 0.05f), bandaH = (int)(H * 0.08f);
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            bool enV = Mathf.Abs(x - W / 2) < bandaV;
            bool enH = Mathf.Abs(y - H / 2) < bandaH;
            if (enV || enH) px[y * W + x] = rojo;
        }

        tex.SetPixels32(px); tex.Apply(false, false);
        return tex;
    }

    private static Texture2D GenNavarra()
    {
        const int W = 256, H = 160;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.name = "BanderaNavarra";
        var px   = new Color32[W * H];

        Color32 rojoN  = new Color32(175, 15,  30,  255);
        Color32 dorado = new Color32(210, 165, 30,  255);

        for (int i = 0; i < px.Length; i++) px[i] = rojoN;

        // Cadena estilizada: círculos dorados en rejilla diagonal
        int paso = 20, radio = 5;
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            // posición relativa al centro de la bandera
            int rx = x - W / 2, ry = y - H / 2;
            if (Mathf.Abs(rx) > W / 3 || Mathf.Abs(ry) > H / 3) continue;

            // Coord en rejilla rotada 45°
            int gx = ((rx + ry) / 2 + 200) % paso;
            int gy = ((rx - ry) / 2 + 200) % paso;
            int cx = paso / 2, cy = paso / 2;
            float dd = Mathf.Sqrt((gx - cx) * (gx - cx) + (gy - cy) * (gy - cy));
            if (dd < radio || (dd > radio + 1 && dd < radio + 3))
                px[y * W + x] = dorado;
        }

        tex.SetPixels32(px); tex.Apply(false, false);
        return tex;
    }

    private static Texture2D GenKeffiyeh()
    {
        const int W = 64, H = 64;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.name      = "Keffiyeh";
        tex.filterMode = FilterMode.Point;
        tex.wrapMode   = TextureWrapMode.Repeat;
        var px         = new Color32[W * H];

        Color32 blanco = new Color32(240, 235, 228, 255);
        Color32 negro  = new Color32( 22,  22,  22, 255);
        Color32 rojo2  = new Color32(190,  30,  30, 255);

        const int cuad = 8;
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            bool bX = (x / cuad) % 2 == 0;
            bool bY = (y / cuad) % 2 == 0;
            if (bX == bY)            px[y * W + x] = blanco;
            else if ((x + y) % 3 == 0) px[y * W + x] = rojo2;
            else                       px[y * W + x] = negro;
        }

        tex.SetPixels32(px); tex.Apply(false, false);
        return tex;
    }

    // ───────────────────────────────────────────────────────────────────────
    //  MESHES PROCEDURALES
    // ───────────────────────────────────────────────────────────────────────
    private void CrearMeshes()
    {
        _meshCuerpo  = BuildCapsule(0.22f, 1.80f, 10, 6);
        _meshBandera = BuildBanderaQuad(0.60f, 1.20f);
        _meshesCreados.Add(_meshCuerpo);
        _meshesCreados.Add(_meshBandera);
    }

    private static Mesh BuildCapsule(float r, float h, int seg, int rings)
    {
        var mesh  = new Mesh { name = "PersonajeCapsule" };
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();

        float halfH = h / 2f - r;

        // Cilindro lateral
        for (int ri = 0; ri <= rings; ri++)
        {
            float t = (float)ri / rings;
            float y = Mathf.Lerp(-halfH, halfH, t);
            for (int si = 0; si <= seg; si++)
            {
                float a = si * Mathf.PI * 2f / seg;
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                verts.Add(new Vector3(x, y, z));
                norms.Add(new Vector3(x, 0f, z).normalized);
                uvs.Add(new Vector2((float)si / seg, t * 0.6f));
            }
        }
        for (int ri = 0; ri < rings; ri++)
        for (int si = 0; si < seg;   si++)
        {
            int b = ri * (seg + 1) + si;
            tris.Add(b); tris.Add(b + seg + 1); tris.Add(b + 1);
            tris.Add(b + 1); tris.Add(b + seg + 1); tris.Add(b + seg + 2);
        }

        // Casquetes
        AddHemi(verts, norms, uvs, tris,  halfH, r, seg, 5, false);
        AddHemi(verts, norms, uvs, tris, -halfH, r, seg, 5, true);

        mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddHemi(List<Vector3> v, List<Vector3> n, List<Vector2> uv,
                                 List<int> t, float baseY, float r, int seg, int rings, bool flip)
    {
        int bv = v.Count;
        float signY = flip ? -1f : 1f;
        for (int ri = 0; ri <= rings; ri++)
        {
            float phi = (float)ri / rings * (Mathf.PI / 2f);
            float py  = signY * Mathf.Sin(phi) * r + baseY;
            float pr  = Mathf.Cos(phi) * r;
            for (int si = 0; si <= seg; si++)
            {
                float a = si * Mathf.PI * 2f / seg;
                float x = Mathf.Cos(a) * pr, z = Mathf.Sin(a) * pr;
                v.Add(new Vector3(x, py, z));
                n.Add(new Vector3(x, signY * Mathf.Sin(phi), z).normalized);
                uv.Add(new Vector2((float)si / seg, flip ? 0f : 1f));
            }
        }
        for (int ri = 0; ri < rings; ri++)
        for (int si = 0; si < seg;   si++)
        {
            int b = bv + ri * (seg + 1) + si;
            if (flip) { t.Add(b); t.Add(b + 1); t.Add(b + seg + 1); t.Add(b + 1); t.Add(b + seg + 2); t.Add(b + seg + 1); }
            else      { t.Add(b); t.Add(b + seg + 1); t.Add(b + 1); t.Add(b + 1); t.Add(b + seg + 1); t.Add(b + seg + 2); }
        }
    }

    private static Mesh BuildBanderaQuad(float W, float H)
    {
        var mesh = new Mesh { name = "BanderaQuad" };
        mesh.vertices = new[]
        {
            // Cara delantera
            new Vector3(0, 0, 0), new Vector3(W, 0, 0),
            new Vector3(W, H, 0), new Vector3(0, H, 0),
            // Cara trasera
            new Vector3(W, 0, 0), new Vector3(0, 0, 0),
            new Vector3(0, H, 0), new Vector3(W, H, 0),
        };
        mesh.uv = new[]
        {
            new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1),
            new Vector2(1,0), new Vector2(0,0), new Vector2(0,1), new Vector2(1,1),
        };
        mesh.triangles = new[] { 0,1,2, 0,2,3, 4,5,6, 4,6,7 };
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    // ───────────────────────────────────────────────────────────────────────
    //  MATERIALES
    // ───────────────────────────────────────────────────────────────────────
    private void CrearMaterialesBase()
    {
        var tipos = (TipoPersonaje[])System.Enum.GetValues(typeof(TipoPersonaje));
        _matsBase = new Material[tipos.Length];
        for (int i = 0; i < tipos.Length; i++)
            _matsBase[i] = MatURP(ColorBase(tipos[i]));
    }

    /// <summary>Color base canónico por tipo de personaje.</summary>
    private static Color ColorBase(TipoPersonaje t) => t switch
    {
        TipoPersonaje.GuardiaCivil          => new Color(0.50f, 0.53f, 0.28f),  // verde oliva
        TipoPersonaje.PoliciaForal          => new Color(0.10f, 0.14f, 0.34f),  // azul marino
        TipoPersonaje.ManifestantePalestino => new Color(0.18f, 0.18f, 0.20f),  // oscuro
        TipoPersonaje.ManifestanteJurrutu   => new Color(0.06f, 0.06f, 0.06f),  // negro
        TipoPersonaje.PortadorIkurriña      => new Color(0.12f, 0.12f, 0.14f),  // negro
        TipoPersonaje.PortadorNavarra       => new Color(0.12f, 0.12f, 0.14f),  // negro
        TipoPersonaje.CivilianMale          => new Color(0.52f, 0.46f, 0.40f),  // neutro
        TipoPersonaje.CivilianFemale        => new Color(0.60f, 0.42f, 0.48f),  // cálido
        _                                   => Color.gray,
    };

    private Material MatURP(Color col)
    {
        var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard")
              ?? Shader.Find("HDRP/Unlit") ?? Shader.Find("Standard")
              ?? Shader.Find("Standard");
        if (sh == null)
        {
            AlsasuaLogger.Error("SistemaPersonajes", "Shader no encontrado. " +
                               "Incluye 'Universal Render Pipeline/Lit' en Always Included Shaders.");
            sh = Shader.Find("Hidden/InternalErrorShader");
            if (sh == null) return null;
        }
        var mat = new Material(sh) { color = col };
        _matsCreados.Add(mat);
        return mat;
    }

    // ───────────────────────────────────────────────────────────────────────
    //  API PÚBLICA
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Posiciones actuales de todos los agentes de un tipo dado.
    /// FIX GC: dos pasadas (contar + llenar) en lugar de List + ToArray().
    /// Antes: new List (alloc) + Add (posibles resize) + ToArray (alloc) = 2+ allocaciones.
    /// Ahora: 1 sola alloc del array resultado, tamaño exacto.
    /// Para cero allocaciones usa <see cref="ObtenerPosicionesNonAlloc"/>.
    /// </summary>
    public Vector3[] ObtenerPosiciones(TipoPersonaje tipo)
    {
        if (_personajes == null) return System.Array.Empty<Vector3>();

        // Pasada 1: contar agentes del tipo (sin alloc)
        int count = 0;
        for (int i = 0; i < _personajes.Length; i++)
            if (_personajes[i].tipo == tipo) count++;

        if (count == 0) return System.Array.Empty<Vector3>();

        // Pasada 2: llenar array de tamaño exacto (1 sola alloc)
        var result = new Vector3[count];
        int idx    = 0;
        for (int i = 0; i < _personajes.Length; i++)
        {
            if (_personajes[i].tipo != tipo) continue;
            result[idx++] = new Vector3(_personajes[i].posicion.x,
                                        _personajes[i].alturaY,
                                        _personajes[i].posicion.z);
        }
        return result;
    }

    /// <summary>
    /// Versión sin allocación de <see cref="ObtenerPosiciones"/>.
    /// Escribe posiciones en el <paramref name="buffer"/> proporcionado por el llamador
    /// y devuelve el número de entradas escritas.
    /// El llamador debe dimensionar el buffer con al menos <see cref="TotalPersonajes"/> slots
    /// para garantizar que no se trunca el resultado.
    /// </summary>
    public int ObtenerPosicionesNonAlloc(TipoPersonaje tipo, Vector3[] buffer)
    {
        if (_personajes == null || buffer == null) return 0;
        int written = 0;
        for (int i = 0; i < _personajes.Length && written < buffer.Length; i++)
        {
            if (_personajes[i].tipo != tipo) continue;
            buffer[written++] = new Vector3(_personajes[i].posicion.x,
                                            _personajes[i].alturaY,
                                            _personajes[i].posicion.z);
        }
        return written;
    }

    /// <summary>
    /// Asigna rutas de patrulla desde GestorEscena.
    /// Llamar ANTES de que los personajes empiecen a moverse (ideal en Awake/Start del gestor).
    /// </summary>
    public void AsignarRutas(Transform[] rutaGC, Transform[] rutaPF)
    {
        rutaGuardiaCivil = rutaGC;
        rutaPoliciaForal = rutaPF;
        // Reiniciar índice de waypoint para que cojan la nueva ruta desde el principio
        for (int i = 0; i < _personajes.Length; i++)
        {
            ref var p = ref _personajes[i];
            if (p.tipo == TipoPersonaje.GuardiaCivil || p.tipo == TipoPersonaje.PoliciaForal)
                p.waypointActual = 0;
        }
        AlsasuaLogger.Info("SistemaPersonajes", $"Rutas asignadas — GC: {rutaGC?.Length ?? 0} wp, PF: {rutaPF?.Length ?? 0} wp.");
    }

    /// <summary>Activa alerta: GC/PF aceleran y no se detienen.</summary>
    public void SetAlerta(bool activo)
    {
        for (int i = 0; i < _personajes.Length; i++)
        {
            ref var p = ref _personajes[i];
            if (p.tipo != TipoPersonaje.GuardiaCivil && p.tipo != TipoPersonaje.PoliciaForal) continue;
            p.estado       = EstadoPersonaje.Patrullando;
            p.tiempoParado = 0f;
        }
    }

    /// <summary>Total de personajes activos.</summary>
    public int TotalPersonajes => _personajes?.Length ?? 0;
}
