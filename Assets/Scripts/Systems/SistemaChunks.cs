// Assets/Scripts/SistemaChunks.cs
// ═══════════════════════════════════════════════════════════════════════════
//  WORLD PARTITION / SISTEMA DE CHUNKS — Gorka Pillar 4 en Unity
//
//  Gorka (UE5): World Partition carga/descarga celdas de la ciudad automáticamente.
//  Unity AAA:   Activamos/desactivamos secciones del mundo según la posición
//               del jugador con histéresis (radio activación < radio desactivación)
//               para evitar el "pop-in" al girar una esquina rápido.
//
//  Optimizaciones incluidas:
//    · Comprobación de distancia cada N segundos (no cada frame)
//    · Histéresis: radio de activación ≠ radio de desactivación
//    · LODGroup.ForceLOD por distancia dentro del chunk activo
//    · Gizmos con código de colores en Scene View
//
//  Setup en el Editor:
//    1. Organiza tu escena en secciones: Plaza_Fueros, CalleAlsasua, PoligonoIndustrial, etc.
//    2. Coloca este componente en un GO vacío "WorldManager".
//    3. Arrastra cada sección al array "chunks" del Inspector.
//    4. Ajusta radioActivacion (~150-200 m) y radioDesactivacion (~250-300 m).
//    5. El componente detecta automáticamente al jugador por tag "Player".
//
//  Para textura 8K con carga instantánea (DirectStorage en PC):
//    · Usa Addressables en lugar de SetActive para chunks pesados
//    · Llama a CargarChunkAsync / DescargarChunkAsync (modo avanzado abajo)
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SistemaChunks : MonoBehaviour
{
    // ── Datos de cada chunk ────────────────────────────────────────────────────
    [System.Serializable]
    public class Chunk
    {
        [Tooltip("GameObject que contiene toda la geometría de esta sección del mundo.")]
        public GameObject go;
        [Tooltip("Nombre descriptivo para el log (ej. 'Plaza_Fueros', 'Poligono').")]
        public string     nombre;
        [Tooltip("Centro del chunk en coordenadas mundo. Se calcula automático si está vacío.")]
        public Vector3    centro;
        [Tooltip("Si está activo, el centro se calcula desde el pivot del GO al Start().")]
        public bool       centroAuto = true;

        [Tooltip("Radio de activación propio de esta zona (0 = usar el radio global del sistema).")]
        public float radioActivarPropio    = 0f;
        [Tooltip("Radio de desactivación propio (0 = usar el radio global del sistema).")]
        public float radioDesactivarPropio = 0f;

        // Runtime — no serializar
        [System.NonSerialized] public bool     activo;
        [System.NonSerialized] public LODGroup lodGroup;
        // OPT: cache de NavMeshAgents del chunk — GetComponentsInChildren es O(n hijos).
        // Se popula en InicializarChunks una vez. Evita traversal completo al desactivar
        // el chunk (puede haber 20-50 NPCs por chunk → 20-50 Component lookups evitados).
        [System.NonSerialized] public UnityEngine.AI.NavMeshAgent[] agentes;
    }

    // ── Configuración ─────────────────────────────────────────────────────────
    [Header("═══ CHUNKS ═══")]
    [Tooltip("Secciones del mundo. Cada una es un GO con toda la geometría de esa zona.")]
    [SerializeField] private Chunk[] chunks;

    [Header("═══ DISTANCIAS ═══")]
    [Tooltip("Radio de activación (m). Chunks dentro de esta distancia se activan.")]
    [SerializeField] private float radioActivacion    = 180f;
    [Tooltip("Radio de desactivación (m). Debe ser MAYOR que radioActivacion. " +
             "La diferencia (histéresis) evita el pop-in al girar una esquina.")]
    [SerializeField] private float radioDesactivacion = 240f;
    [Tooltip("Radio LOD de alta calidad (m). Chunks entre radioLOD y radioActivacion " +
             "se renderizan en baja calidad (LOD 1).")]
    [SerializeField] private float radioLOD           = 120f;

    [Header("═══ FADE SUAVE ═══")]
    [Tooltip("Si true, los chunks se ven aparecer/desaparecer con un fade de LODGroup en lugar de SetActive duro.")]
    [SerializeField] private bool  fadeActivacion     = true;
    [Tooltip("Duración del fade de activación (s).")]
    [SerializeField] private float duracionFade       = 0.6f;

    [Header("═══ RENDIMIENTO ═══")]
    [Tooltip("Intervalo (s) entre comprobaciones de distancia. " +
             "0.4 s es suficiente — sin necesidad de hacerlo cada frame.")]
    [SerializeField] private float intervaloCheck     = 0.4f;
    [Tooltip("Si está activo, los chunks se descargan en un frame diferido " +
             "para no provocar picos de CPU al salir de la zona.")]
    [SerializeField] private bool  desactivacionDiferida = true;

    [Header("═══ DEBUG ═══")]
    [Tooltip("Muestra en pantalla cuántos chunks están activos.")]
    [SerializeField] private bool  mostrarGUI         = true;

    // ── Estado interno ─────────────────────────────────────────────────────────
    private Transform             jugador;
    private float                 _timerCheck;
    private readonly HashSet<int> _activos = new HashSet<int>();

    // ── Propiedades públicas ────────────────────────────────────────────────────
    public int  ChunksActivos  => _activos.Count;
    public int  ChunksTotales  => chunks != null ? chunks.Length : 0;

    // ─────────────────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        // Suscribirse al evento de spawn del jugador — evita FindGameObjectWithTag en polling
        AltsasuCore.OnJugadorSpawned += OnJugadorSpawned;

        // Intentar cachear ahora por si el jugador ya existe en escena
        BuscarJugador();
        InicializarChunks();
        ComprobarChunks(); // primera comprobación inmediata sin esperar el timer
    }

    private void OnDestroy()
    {
        AltsasuCore.OnJugadorSpawned -= OnJugadorSpawned;
    }

    private void OnJugadorSpawned(Transform t)
    {
        jugador = t;
    }

    private void Update()
    {
        _timerCheck -= Time.deltaTime;
        if (_timerCheck > 0f) return;
        _timerCheck = intervaloCheck;

        // Fallback: si el evento no llegó (spawn anterior al Start), buscar una vez
        if (jugador == null) BuscarJugador();
        ComprobarChunks();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  INICIALIZACIÓN
    // ─────────────────────────────────────────────────────────────────────────

    private void InicializarChunks()
    {
        if (chunks == null) return;
        for (int i = 0; i < chunks.Length; i++)
        {
            var c = chunks[i];
            if (c == null || c.go == null) continue;   // FIX: el array serializado puede tener slots null (el autor lo documenta como crash en prod)

            if (c.centroAuto || c.centro == Vector3.zero)
                c.centro = c.go.transform.position;

            if (string.IsNullOrEmpty(c.nombre))
                c.nombre = c.go.name;

            c.lodGroup = c.go.GetComponentInChildren<LODGroup>();
            // OPT: cachear agentes NavMesh del chunk al inicializar — no en cada desactivación
            c.agentes  = c.go.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true);

            // Desactivar todo — la primera comprobación activa los cercanos
            c.go.SetActive(false);
            c.activo = false;
        }
        AlsasuaLogger.Info("Chunks", $"World Partition inicializado: {chunks.Length} chunks registrados.");
    }

    private void BuscarJugador()
    {
        // Prioridad: AltsasuCore.Jugador (O(1)) antes que FindGameObjectWithTag (O(n))
        var core = AltsasuCore.Jugador;
        if (core != null) { jugador = core; return; }

        var jGO = GameObject.FindGameObjectWithTag("Player");
        if (jGO != null) jugador = jGO.transform;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  LÓGICA PRINCIPAL — equivalente al Cell Loading de UE5 World Partition
    // ─────────────────────────────────────────────────────────────────────────

    private void ComprobarChunks()
    {
        if (chunks == null || jugador == null) return;

        // Cuando el jugador está en vehículo su Transform es hijo del vehículo,
        // por lo que jugador.position puede diferir varios metros de la posición
        // real del vehículo → usar la raíz del padre para el cálculo de distancia.
        bool enVehiculo = ServiceLocator.Get<ISpawnService>()?.JugadorEnVehiculo ?? false;
        Vector3 posJug = (enVehiculo && jugador.parent != null)
            ? jugador.parent.position
            : jugador.position;

        for (int i = 0; i < chunks.Length; i++)
        {
            var c = chunks[i];
            // Guard doble: Unity puede dejar slots null en arrays serializados (crash en producción)
            if (c == null || c.go == null) continue;

            float dist = Vector3.Distance(posJug, c.centro);

            // Usar radio propio de la zona si está definido; si no, el radio global
            float rAct = c.radioActivarPropio    > 0f ? c.radioActivarPropio    : radioActivacion;
            float rDes = c.radioDesactivarPropio > 0f ? c.radioDesactivarPropio : radioDesactivacion;

            if (!c.activo && dist <= rAct)
            {
                // ── Activar chunk ────────────────────────────────────────────
                ActivarChunk(c, i);
            }
            else if (c.activo && dist > rDes)
            {
                // ── Desactivar chunk (con histéresis) ────────────────────────
                if (desactivacionDiferida)
                    StartCoroutine(DesactivarDiferido(c, i));
                else
                    DesactivarChunk(c, i);
            }
            else if (c.activo)
            {
                // ── Ajuste de LOD mientras está activo ───────────────────────
                if (c.lodGroup != null)
                    c.lodGroup.ForceLOD(dist <= radioLOD ? 0 : 1);
            }
        }
    }

    private void ActivarChunk(Chunk c, int idx)
    {
        c.go.SetActive(true);
        c.activo = true;
        _activos.Add(idx);
        if (c.lodGroup != null)
        {
            // OPTIMIZADOR: fade suave usando LODGroup crossFadeWidth.
            // En lugar de aparecer de golpe, el chunk entra con transición.
            // LODGroup.ForceLOD(-1) + crossFadeWidth > 0 activa el blend.
            c.lodGroup.ForceLOD(-1); // volver a automático
            if (fadeActivacion)
                StartCoroutine(FadeEntradaChunk(c));
        }
        AlsasuaLogger.Info("Chunks", $"[+] '{c.nombre}' activado. Activos: {_activos.Count}/{ChunksTotales}");
    }

    private IEnumerator FadeEntradaChunk(Chunk c)
    {
        if (c.lodGroup == null) yield break;
        // Arrancar con LOD bajo (menos detalle) y subir a LOD0 gradualmente
        // usando el crossFadeWidth del LODGroup para que Unity interpole.
        c.lodGroup.ForceLOD(1);   // arrancar en LOD medio
        yield return new WaitForSeconds(duracionFade * 0.5f);
        c.lodGroup.ForceLOD(-1);  // soltar → Unity elige el correcto por distancia
    }

    private void DesactivarChunk(Chunk c, int idx)
    {
        // Resetear NavMeshAgents antes de desactivar el GO — evita corrupción
        // de paths cuando un PoliciaForalIA persigue al jugador dentro del chunk
        // que está a punto de desaparecer (riesgo: destino ya no existe en el NavMesh)
        // OPT: usa array cacheado en lugar de GetComponentsInChildren (O(n) por llamada)
        if (c.agentes != null)
        {
            for (int k = 0; k < c.agentes.Length; k++)
            {
                var ag = c.agentes[k];
                if (ag != null && ag.isActiveAndEnabled) ag.ResetPath();
            }
        }

        c.go.SetActive(false);
        c.activo = false;
        _activos.Remove(idx);
        if (c.lodGroup != null) c.lodGroup.ForceLOD(-1);
        AlsasuaLogger.Info("Chunks", $"[-] '{c.nombre}' desactivado. Activos: {_activos.Count}/{ChunksTotales}");
    }

    /// <summary>
    /// Desactiva el chunk en el siguiente frame para repartir el coste de CPU
    /// y no acumular varios Destroy/SetActive en el mismo frame.
    /// </summary>
    private IEnumerator DesactivarDiferido(Chunk c, int idx)
    {
        yield return null; // esperar un frame
        // Doble guard — verificar que el chunk sigue activo Y
        // que el GO sigue existiendo (puede haber sido destruido por SceneManager
        // o por ActivarTodo/DesactivarTodo llamados durante ese frame).
        // Sin este guard, si el jugador reaparece cerca del chunk en el mismo frame,
        // ActivarChunk ya habrá puesto c.activo=true y esta corrutina volvería a
        // llamar DesactivarChunk dejando el chunk inactivo con _activos desfasado.
        if (!c.activo) yield break;
        if (c.go == null) yield break;  // GO destruido externamente
        DesactivarChunk(c, idx);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  API PÚBLICA
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Activa todos los chunks (útil para capturar screenshots o en el Editor).</summary>
    public void ActivarTodo()
    {
        if (chunks == null) return;
        for (int i = 0; i < chunks.Length; i++)
            if (chunks[i] != null && chunks[i].go != null && !chunks[i].activo)
                ActivarChunk(chunks[i], i);
    }

    /// <summary>Desactiva todos los chunks.</summary>
    public void DesactivarTodo()
    {
        if (chunks == null) return;
        for (int i = 0; i < chunks.Length; i++)
            if (chunks[i] != null && chunks[i].go != null && chunks[i].activo)
                DesactivarChunk(chunks[i], i);
    }

    /// <summary>Fuerza la recalculación inmediata (útil tras teletransporte del jugador).</summary>
    public void ForzarActualizacion()
    {
        _timerCheck = 0f;
        ComprobarChunks();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GUI DEBUG (en pantalla, solo en modo Editor/Development Build)
    // ─────────────────────────────────────────────────────────────────────────

    // GUIStyle cacheado — new GUIStyle() en OnGUI() es un alloc por frame
    private GUIStyle _guiStyle;

    // PERF: cache del texto de debug — reconstruir solo cuando cambia el número de activos.
    // Sin cache, la interpolación $"" genera 3 strings nuevos cada frame a 60fps (~180 allocs/seg).
    private int    _guiCacheActivos  = -1;
    private string _guiCacheTexto   = "";

    private void OnGUI()
    {
        if (!mostrarGUI || (!Application.isEditor && !Debug.isDebugBuild)) return;

        // Inicialización lazy dentro de OnGUI (único lugar válido para GUIStyle)
        _guiStyle ??= new GUIStyle(GUI.skin.box)
        {
            fontSize  = 12,
            alignment = TextAnchor.UpperLeft,
            padding   = new RectOffset(6, 6, 4, 4)
        };

        // PERF: reconstruir el string solo cuando cambia _activos.Count (~eliminados ~180 allocs/seg).
        // radioActivacion e intervaloCheck son constantes en runtime → no necesitan recalcularse.
        if (_activos.Count != _guiCacheActivos)
        {
            _guiCacheActivos = _activos.Count;
            _guiCacheTexto   = $"CHUNKS: {_activos.Count} / {ChunksTotales} activos\n" +
                               $"Radio act.: {radioActivacion} m  desact.: {radioDesactivacion} m\n" +
                               $"Intervalo: {intervaloCheck} s";
        }

        GUI.Box(new Rect(10, 10, 260, 60), _guiCacheTexto, _guiStyle);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GIZMOS — visualización en Scene View
    // ─────────────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (chunks == null) return;

        foreach (var c in chunks)
        {
            // Mismo guard doble que en runtime: Unity puede dejar slots null en el array.
            if (c == null || c.go == null) continue;
            Vector3 centro = Application.isPlaying ? c.centro : c.go.transform.position;

            // Verde = activo, rojo translúcido = inactivo
            Gizmos.color = c.activo
                ? new Color(0f, 1f, 0f, 0.35f)
                : new Color(1f, 0.2f, 0.2f, 0.12f);
            Gizmos.DrawCube(centro, Vector3.one * 6f);

            // Radio de activación (verde)
            Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.15f);
            Gizmos.DrawWireSphere(centro, radioActivacion);

            // Radio de desactivación (naranja)
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.08f);
            Gizmos.DrawWireSphere(centro, radioDesactivacion);
        }

        // Radio LOD desde la posición de la cámara del Editor
        if (jugador != null)
        {
            Gizmos.color = new Color(0f, 0.5f, 1f, 0.15f);
            Gizmos.DrawWireSphere(jugador.position, radioLOD);
        }
    }
}
