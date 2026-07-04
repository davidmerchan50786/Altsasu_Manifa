// Assets/Scripts/SistemaNavMesh.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA DE NAVMESH EN RUNTIME
//
//  Por qué runtime y no Editor-bake:
//    El terreno de Alsasua se genera en Start() desde un DEM .raw de 1025×1025.
//    No existe en el Editor, por lo que Window → AI → Navigation no puede
//    hornearlo. Hay que hacerlo después de que el Terrain esté creado.
//
//  Funcionamiento:
//    1. Espera a que Terrain.activeTerrain exista.
//    2. Crea un NavMeshSurface centrado en Herriko Plaza (1918, y, 8570).
//    3. Hornea de forma asíncrona — no congela el juego.
//    4. Lanza el evento OnNavMeshListo cuando termina.
//    5. PoliciaForalIA y EnemigoPatrulla suscriben al evento antes de
//       activar su NavMeshAgent.
//    6. Rehorneado incremental cuando el jugador cruza a una zona nueva.
//
//  Setup: añade este componente al GameObject "GameManager" o crear uno
//  vacío "NavMeshManager". SceneBootstrapper lo busca por FindFirstObjectByType.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_AI_NAVIGATION_PACKAGE
using Unity.AI.Navigation;
#endif

[DefaultExecutionOrder(-50)]   // antes que PoliciaForalIA (0) pero después de SceneBootstrapper (-200)
public class SistemaNavMesh : SingletonMono<SistemaNavMesh>
{
    protected override bool DestroyGameObjectOnDuplicate => true;

    // ── Evento que avisa cuando el NavMesh está disponible ─────────────────
    public static event System.Action OnNavMeshListo;
    public static bool EstaListo { get; private set; } = false;

    // ── Configuración ──────────────────────────────────────────────────────
    [Header("═══ ZONA INICIAL (Herriko Plaza) ═══")]
    [Tooltip("Centro del primer horneado en coordenadas mundo.\n" +
             "SceneBootstrapper.centroX/centroZ = 1918 / 8570 → Herriko Plaza.")]
    [SerializeField] private Vector3 centroInicial = new Vector3(GeoDataAlsasua.OX, 240f, GeoDataAlsasua.OZ);

    [Tooltip("Radio de la zona inicial a hornear (m). 350 m cubre la plaza y calles adyacentes.")]
    [SerializeField] private float   radioInicial  = 350f;

    [Tooltip("Altura del volumen de horneado (m). Debe cubrir el desnivel del terreno + edificios.")]
    [SerializeField] private float   alturaVolumen = 60f;

    [Header("═══ AGENTE ═══")]
    [Tooltip("Radio del agente NavMesh (m). 0.4 = persona adulta.")]
    [SerializeField] private float   radioAgente   = 0.40f;
    [Tooltip("Altura del agente NavMesh (m).")]
    [SerializeField] private float   alturaAgente  = 1.80f;
    [Tooltip("Pendiente máxima transitable (°).")]
    [SerializeField] private float   pendienteMax  = 45f;
    [Tooltip("Escalón máximo superable (m).")]
    [SerializeField] private float   escalonMax    = 0.50f;

    [Header("═══ REHORNEADO ═══")]
    [Tooltip("Si está activo, rehorneará la zona cuando el jugador se aleje más de distanciaRehornear metros del centro actual.")]
    [SerializeField] private bool    rehornearAlMover    = true;
    [Tooltip("Distancia del jugador al borde de la zona horneada que dispara un rehorneado.")]
    [SerializeField] private float   distanciaRehornear  = 200f;

    [Header("═══ DEBUG ═══")]
    [SerializeField] private bool    mostrarGizmos = true;

    // ── Estado interno ─────────────────────────────────────────────────────
#if UNITY_AI_NAVIGATION_PACKAGE
    private NavMeshSurface _surface;
#endif
    private Transform      _jugador;
    private Vector3        _centroActual;
    private bool           _horneando = false;
    private float          _timerCheck = 0f;
    private NavMeshDataInstance _instLegacy;   // horneado legacy activo (se reemplaza sin acumular)
#pragma warning disable CS0414
    private bool           _usaLegacy  = false;   // fallback sin paquete AI Navigation
#pragma warning restore CS0414

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    protected override void OnAwake() => EstaListo = false;

    private IEnumerator Start()
    {
        // 1. Esperar a que el Terrain exista (SceneBootstrapper lo crea en Start())
        AlsasuaLogger.Info("NavMesh", "Esperando a que el Terrain se genere...");
        yield return new WaitUntil(() => Terrain.activeTerrain != null);

        // 2. Esperar 2 frames para que el TerrainCollider esté registrado en física
        yield return null;
        yield return null;

        // 3. Ajustar Y del centro al terreno real
        float y = Terrain.activeTerrain.SampleHeight(centroInicial) + 1f;
        _centroActual = new Vector3(centroInicial.x, y, centroInicial.z);

        // 4. Hornear
        yield return StartCoroutine(HornearZona(_centroActual, radioInicial));
    }

    private void Update()
    {
        if (!EstaListo || !rehornearAlMover || _horneando) return;

        // Buscar jugador si aún no lo tenemos
        if (_jugador == null)
        {
            var jGO = GameObject.FindGameObjectWithTag("Player");
            if (jGO != null) _jugador = jGO.transform;
            else return;
        }

        // Comprobar cada 2 segundos si el jugador se ha alejado del centro horneado
        _timerCheck -= Time.deltaTime;
        if (_timerCheck > 0f) return;
        _timerCheck = 2f;

        float dist = Vector3.Distance(
            new Vector3(_jugador.position.x, 0, _jugador.position.z),
            new Vector3(_centroActual.x,     0, _centroActual.z));

        if (dist > radioInicial - distanciaRehornear)
        {
            float y = Terrain.activeTerrain != null
                ? Terrain.activeTerrain.SampleHeight(_jugador.position) + 1f
                : _jugador.position.y;
            Vector3 nuevoCentro = new Vector3(_jugador.position.x, y, _jugador.position.z);
            StartCoroutine(HornearZona(nuevoCentro, radioInicial));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HORNEADO
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Hornea (o rehorneada) el NavMesh en un volumen centrado en <paramref name="centro"/>.
    /// Es la única función pública necesaria — SceneBootstrapper también puede llamarla.
    /// </summary>
    // Timestamp del último bake completado — evita que tiles del terreno disparen
    // 4 bakes seguidos durante la carga. Cooldown de 8 s entre bakes salvo el primero.
    float _ultimoBakeT = -99f;
    const float COOLDOWN_BAKE = 8f;

    public IEnumerator HornearZona(Vector3 centro, float radio)
    {
        if (_horneando) yield break;

        // Durante la carga inicial, esperar a que el terreno esté estable antes de bakear.
        // El primer bake (EstaListo=false) pasa siempre; los re-bakes tienen cooldown.
        if (EstaListo && Time.realtimeSinceStartup - _ultimoBakeT < COOLDOWN_BAKE)
        {
            AlsasuaLogger.Info("NavMesh", $"Re-bake ignorado (cooldown {COOLDOWN_BAKE}s). Próximo en {COOLDOWN_BAKE - (Time.realtimeSinceStartup - _ultimoBakeT):F1}s");
            yield break;
        }

        _horneando = true;
        EstaListo  = false;

        AlsasuaLogger.Info("NavMesh", $"Horneando zona ({radio}m) en {centro}...");
        float t0 = Time.realtimeSinceStartup;

        // Guardar centro para detección de rehorneado
        _centroActual = centro;

#if UNITY_AI_NAVIGATION_PACKAGE
        // ── NUEVO: NavMeshSurface (paquete AI Navigation instalado) ────────
        if (_surface == null)
        {
            var go = new GameObject("_NavMeshSurface");
            go.transform.SetParent(transform);
            _surface = go.AddComponent<NavMeshSurface>();
            _surface.agentTypeID    = 0;
            _surface.collectObjects = CollectObjects.Volume;
            _surface.useGeometry    = NavMeshCollectGeometry.PhysicsColliders;
            _surface.layerMask = LayerMask.GetMask("Default") | (1 << 8);
            var settings = NavMesh.GetSettingsByIndex(0);
            settings.agentRadius = radioAgente; settings.agentHeight = alturaAgente;
            settings.agentSlope  = pendienteMax; settings.agentClimb  = escalonMax;
            NavMesh.SetSettings(0, settings);
        }
        _surface.transform.position = centro;
        _surface.size   = new Vector3(radio * 2f, alturaVolumen, radio * 2f);
        _surface.center = Vector3.zero;

        // ASÍNCRONO también en el PRIMER bake: BuildNavMesh() es síncrono y congela el
        // hilo principal varios minutos sobre un volumen de 350 m con todos los colliders.
        // El patrón async = crear NavMeshData vacío + UpdateNavMesh (hornea en worker).
        if (_surface.navMeshData == null)
        {
            _surface.navMeshData = new NavMeshData(_surface.agentTypeID);
            _surface.AddData();
        }
        AlsasuaLogger.Info("NavMesh", $"⏳ Bake ASÍNCRONO (surface) zona {radio}m…");
        AsyncOperation op = _surface.UpdateNavMesh(_surface.navMeshData);
        while (!op.isDone) yield return null;

        float duracion = Time.realtimeSinceStartup - t0;
        bool  exito    = _surface.navMeshData != null;
#else
        // ── LEGACY: NavMeshBuilder sin paquete AI Navigation ───────────────
        // Usa NavMeshBuildSource para recopilar colliders y hornear sin NavMeshSurface.
        _usaLegacy = true;
        var sources = new List<NavMeshBuildSource>();
        var bounds  = new Bounds(centro, new Vector3(radio*2f, alturaVolumen, radio*2f));

        // Recopilar todo desde la escena (Terrain + colliders). Es síncrono → medimos
        // su coste por separado: si CollectSources fuese el cuello, el async del bake
        // no bastaría y habría que filtrar/limitar las fuentes.
        float tCollect0 = Time.realtimeSinceStartup;
        NavMeshBuilder.CollectSources(
            bounds, LayerMask.GetMask("Default") | (1 << 8),
            NavMeshCollectGeometry.PhysicsColliders, 0,
            new List<NavMeshBuildMarkup>(), sources);
        float msCollect = (Time.realtimeSinceStartup - tCollect0) * 1000f;

        var buildSettings = NavMesh.GetSettingsByID(0);
        buildSettings.agentRadius = radioAgente; buildSettings.agentHeight = alturaAgente;
        buildSettings.agentSlope  = pendienteMax; buildSettings.agentClimb  = escalonMax;

        // CAMBIO (jun 2026): BuildNavMeshData es SÍNCRONO y bloqueaba el hilo principal
        // varios MINUTOS (350 m × todos los colliders: terreno + OBJ + 1030 edificios) →
        // era EL cuelgue de arranque. Pasamos a UpdateNavMeshDataAsync (hornea en worker
        // thread, el juego sigue corriendo). El posicionamiento va por AddNavMeshData(pos),
        // con bounds LOCALES al origen — equivalente al BuildNavMeshData(localBounds, position)
        // anterior, así que NO reaparece el bug de los bounds al doble (0 vértices).
        if (_instLegacy.valid) _instLegacy.Remove();   // quita el horneado anterior (sin acumular por zona)
        NavMeshData data = new NavMeshData();
        _instLegacy = NavMesh.AddNavMeshData(data, centro, Quaternion.identity);

        AlsasuaLogger.Info("NavMesh",
            $"⏳ Bake ASÍNCRONO… (CollectSources {msCollect:F0} ms, {sources.Count} fuentes)");
        AsyncOperation op = NavMeshBuilder.UpdateNavMeshDataAsync(
            data, buildSettings, sources, new Bounds(Vector3.zero, bounds.size));
        while (!op.isDone) yield return null;

        float duracion = Time.realtimeSinceStartup - t0;
        bool  exito    = data != null;
#endif

        if (exito)
        {
            EstaListo    = true;
            _horneando   = false;
            _ultimoBakeT = Time.realtimeSinceStartup; // marcar timestamp para cooldown
            AlsasuaLogger.Info("NavMesh",
                $"✅ NavMesh listo en {duracion:F2}s. " +
                $"Zona: {radio}m × {alturaVolumen}m en {centro}.");
            OnNavMeshListo?.Invoke();
        }
        else
        {
            _horneando = false;
            AlsasuaLogger.Error("NavMesh",
                "❌ NavMesh falló. Comprueba que el Terrain tiene collider " +
                "y que la capa 'Terrain' (layer 8) está incluida en layerMask.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  API PÚBLICA
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fuerza un rehorneado inmediato alrededor de una posición.
    /// Llama desde GameManagerAltsasua cuando el jugador hace respawn.
    /// </summary>
    public void ForzarRehornear(Vector3 posicion)
    {
        if (_horneando) return;
        float y = Terrain.activeTerrain != null
            ? Terrain.activeTerrain.SampleHeight(posicion) + 1f
            : posicion.y;
        StartCoroutine(HornearZona(new Vector3(posicion.x, y, posicion.z), radioInicial));
    }

    /// <summary>
    /// Devuelve true si el punto está sobre el NavMesh (dentro de tolerancia).
    /// Útil para validar posiciones de spawn de enemigos antes de activar su agente.
    /// </summary>
    public static bool PuntoEnNavMesh(Vector3 punto, float tolerancia = 2f)
    {
        return NavMesh.SamplePosition(punto, out _, tolerancia, NavMesh.AllAreas);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GIZMOS
    // ─────────────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (!mostrarGizmos) return;

        // Zona de horneado actual
        Gizmos.color = EstaListo
            ? new Color(0f, 1f, 0f, 0.15f)
            : new Color(1f, 0.5f, 0f, 0.15f);
        Gizmos.DrawCube(_centroActual,
            new Vector3(radioInicial * 2f, alturaVolumen, radioInicial * 2f));

        Gizmos.color = EstaListo ? Color.green : Color.yellow;
        Gizmos.DrawWireCube(_centroActual,
            new Vector3(radioInicial * 2f, alturaVolumen, radioInicial * 2f));

        // Radio de rehorneado
        if (rehornearAlMover)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
            Gizmos.DrawWireSphere(_centroActual,
                radioInicial - distanciaRehornear);
        }
    }
}
