// Assets/Scripts/SistemaBombas.cs
// Colocar bombas (F), detonar en remoto (G) o por proximidad

using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class SistemaBombas : MonoBehaviour
{
    [Header("═══ BOMBA ═══")]
    [Range(1f, 30f)]
    [SerializeField] private float radioExplosion   = 12f;
    [SerializeField] private float fuerzaExplosion  = 700f;
    [SerializeField] private int   danoExplosion    = 130;
    [SerializeField] private int   bombasDisponibles = 5;
    [Range(0f, 60f)]
    [SerializeField] private float timerAutodetonacion = 0f;  // 0 = solo remoto

    [Header("═══ DETONACIÓN POR PROXIMIDAD ═══")]
    [SerializeField] private bool  proximidadActiva = true;
    [SerializeField] private float distanciaProximidad = 4f;

    // ═══════════════════════════════════════════════════════════════════════
    //  ESTADO INTERNO
    // ═══════════════════════════════════════════════════════════════════════

    private List<BombaColocada> bombas = new List<BombaColocada>();
    private Camera camara;

    // FIX RENDIMIENTO: caché de enemigos con dos timers:
    //   · INTERVALO_PROXIMIDAD (200ms) — comprobar distancia bomba↔enemigo, sin alloc.
    //   · INTERVALO_REFRESH_CACHE (5s)  — re-poblar el array; alloca, pero poco frecuente.
    // Los enemigos destruidos quedan como null → el null-check en ComprobarProximidad los filtra.
    private EnemigoPatrulla[] enemigosCache      = new EnemigoPatrulla[0];
    private float timerProximidad                = 0f;
    private float timerRefreshCache              = 0f;
    private const float INTERVALO_PROXIMIDAD     = 0.2f;   // comprobación de distancia
    private const float INTERVALO_REFRESH_CACHE  = 5f;     // re-búsqueda de nuevos enemigos

    // FIX HUD: sustitución del OnGUI() legacy por Canvas UGUI.
    // OnGUI() corre fuera del ciclo de render de URP y no respeta DPI scaling.
    private GameObject _hudCanvasGO;
    private Text       _labelBombas;
    private int        _bombasAnterior   = -1;
    private int        _colocadasAnterior = -1;

    // ═══════════════════════════════════════════════════════════════════════
    //  UNITY
    // ═══════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        camara = GetComponentInChildren<Camera>();
        if (camara == null) camara = Camera.main;
    }

    private void Start()
    {
        // Poblar la caché una vez al arrancar.
        // Los refresos posteriores ocurren cada INTERVALO_REFRESH_CACHE segundos.
        RefrescarCacheEnemigos();
        InicializarHUD();
    }

    private void Update()
    {
        // FIX RENDIMIENTO: separar el ciclo de detección de proximidad (200ms)
        // del ciclo de re-búsqueda de enemigos (5s, que alloca un nuevo array).
        // La mayoría de los frames solo hacen la comprobación de distancia → cero alloc.
        timerRefreshCache -= Time.deltaTime;
        if (timerRefreshCache <= 0f)
        {
            RefrescarCacheEnemigos();
            timerRefreshCache = INTERVALO_REFRESH_CACHE;
        }

        if (!proximidadActiva || bombas.Count == 0) return;

        timerProximidad -= Time.deltaTime;
        if (timerProximidad > 0f) return;

        timerProximidad = INTERVALO_PROXIMIDAD;
        ComprobarProximidad();

        ActualizarHUD();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ═══════════════════════════════════════════════════════════════════════

    public void ColocarBomba()
    {
        if (bombasDisponibles <= 0)
        {
            Debug.Log("[Bombas] Sin bombas disponibles.");
            return;
        }
        if (camara == null) { Debug.LogWarning("[Bombas] Cámara no disponible."); return; }

        // Raycast al suelo desde delante del jugador para colocar la bomba
        Vector3 posicion;
        Vector3 origenBomba = transform.position + transform.forward * 1.5f + Vector3.up * 1f;
        if (Physics.Raycast(origenBomba, Vector3.down, out RaycastHit hit, 5f))
            posicion = hit.point;
        else
            posicion = transform.position + transform.forward * 1.5f;

        var bombaGO = CrearObjetoBomba(posicion);
        var bomba   = new BombaColocada
        {
            objetoFisico = bombaGO,
            posicion     = posicion,
        };
        bombas.Add(bomba);
        bombasDisponibles--;

        Debug.Log($"[Bombas] Bomba colocada en {posicion:F1}. Quedan: {bombasDisponibles}");
        ActualizarHUD();

        // Si tiene timer, iniciar cuenta atrás y guardar referencia anti-fuga (V23)
        if (timerAutodetonacion > 0f)
            bomba.rutinaTimer = StartCoroutine(TimerBomba(bomba));
    }

    public void DetonarUltima()
    {
        if (bombas.Count == 0)
        {
            Debug.Log("[Bombas] No hay bombas colocadas.");
            return;
        }
        var ultima = bombas[bombas.Count - 1];
        Detonar(ultima);
    }

    public void DetonarTodas()
    {
        foreach (var b in new List<BombaColocada>(bombas))
            Detonar(b);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DETONACIÓN
    // ═══════════════════════════════════════════════════════════════════════

    private void Detonar(BombaColocada bomba)
    {
        if (bomba.explotada) return;
        bomba.explotada = true;
        
        // V23 AUDIT FIX: Destruir la Corrutina Timer explícitamente para evitar ejecución zombie (Memory Leak).
        if (bomba.rutinaTimer != null) StopCoroutine(bomba.rutinaTimer);
        
        bombas.Remove(bomba);

        if (bomba.objetoFisico != null)
            Destroy(bomba.objetoFisico);

        SistemaExplosion.Explotar(bomba.posicion, radioExplosion, fuerzaExplosion, danoExplosion);
        Debug.Log($"[Bombas] ¡BOOM! en {bomba.posicion:F1}");
        ActualizarHUD();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DETECCIÓN POR PROXIMIDAD
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Re-pobla la caché de enemigos. Alloca un nuevo array (FindObjectsByType no evitable),
    /// pero solo se llama cada INTERVALO_REFRESH_CACHE segundos, no cada frame.
    /// Los enemigos destruidos entre refrescos quedan como null y se filtran en ComprobarProximidad.
    /// </summary>
    private void RefrescarCacheEnemigos()
    {
        enemigosCache = Object.FindObjectsByType<EnemigoPatrulla>(FindObjectsSortMode.None);
    }

    private void ComprobarProximidad()
    {
        // Usa la caché refrescada en Update() — ya NO llama FindObjectsByType aquí
        // FIX OPT: iterar con índice descendente para evitar new List<> cada 200 ms.
        // Detonar() llama bombas.Remove() — el índice descendente garantiza que los
        // elementos anteriores al índice actual no se saltan tras el Remove.
        // FIX OPT: sqrMagnitude evita Sqrt de Vector3.Distance (1 Sqrt/par bomba×enemigo).
        float distSqr = distanciaProximidad * distanciaProximidad;
        for (int i = bombas.Count - 1; i >= 0; i--)
        {
            var bomba = bombas[i];
            if (bomba.explotada) continue;
            foreach (var enemigo in enemigosCache)
            {
                if (enemigo == null) continue;
                if ((bomba.posicion - enemigo.transform.position).sqrMagnitude <= distSqr)
                {
                    Debug.Log("[Bombas] Enemigo detectado cerca. ¡DETONACIÓN POR PROXIMIDAD!");
                    Detonar(bomba);
                    break;
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  OBJETO 3D DE BOMBA
    // ═══════════════════════════════════════════════════════════════════════

    // FIX STATIC: _matBombaCache era 'static' — si había N instancias de SistemaBombas,
    // la primera en OnDestroy() destruía el material compartido y lo nullificaba.
    // Las N-1 restantes tenían sharedMaterial apuntando a un objeto destruido → magenta.
    // Solución: campo de instancia. Cada SistemaBombas crea y destruye su propio material.
    // El overhead (N materiales idénticos vs 1) es despreciable — rara vez hay >1 instancia.
    private Material _matBomba;
    private Material ObtenerMatBomba()
    {
        if (_matBomba != null) return _matBomba;
        var shader = Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Universal Render Pipeline/Unlit")
                  ?? Shader.Find("Standard");
        _matBomba = new Material(shader) { color = new Color(0.1f, 0.1f, 0.1f) };
        return _matBomba;
    }

    private GameObject CrearObjetoBomba(Vector3 posicion)
    {
        // Cuerpo principal (cilindro negro)
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Bomba";
        go.transform.position   = posicion + Vector3.up * 0.12f;
        go.transform.localScale = new Vector3(0.2f, 0.12f, 0.2f);

        go.GetComponent<Renderer>().sharedMaterial = ObtenerMatBomba();

        // Luz parpadeante roja
        var luzGO = new GameObject("LuzBomba");
        luzGO.transform.SetParent(go.transform);
        luzGO.transform.localPosition = Vector3.up * 1.2f;
        var luz = luzGO.AddComponent<Light>();
        luz.type      = LightType.Point;
        luz.color     = Color.red;
        luz.intensity = 2f;
        luz.range     = 3f;

        // Parpadeo
        go.AddComponent<ParpadeoLuz>().luz = luz;

        return go;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CLEANUP
    // ═══════════════════════════════════════════════════════════════════════

    private void OnDestroy()
    {
        // _matBomba es ahora campo de instancia → destruirla aquí solo afecta a esta instancia.
        if (_matBomba != null) { Object.Destroy(_matBomba); _matBomba = null; }

        // Destruir el Canvas HUD que creamos por código.
        if (_hudCanvasGO != null) { Destroy(_hudCanvasGO); _hudCanvasGO = null; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TIMER AUTOMÁTICO
    // ═══════════════════════════════════════════════════════════════════════

    private System.Collections.IEnumerator TimerBomba(BombaColocada bomba)
    {
        yield return new WaitForSeconds(timerAutodetonacion);
        if (!bomba.explotada)
            Detonar(bomba);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HUD — Canvas UGUI (reemplaza OnGUI legacy)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Crea el Canvas HUD por código.
    /// OnGUI() (IMGUI legacy) corría fuera del pipeline URP y no respetaba DPI scaling.
    /// Canvas ScreenSpaceOverlay se compone correctamente sobre el render de URP.
    /// </summary>
    private void InicializarHUD()
    {
        // ── Canvas ──────────────────────────────────────────────────────
        _hudCanvasGO = new GameObject("HUD_Bombas");
        var canvas = _hudCanvasGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        _hudCanvasGO.AddComponent<CanvasScaler>();
        _hudCanvasGO.AddComponent<GraphicRaycaster>();

        // ── Panel de fondo semitransparente ─────────────────────────────
        var panelGO  = new GameObject("Panel");
        panelGO.transform.SetParent(_hudCanvasGO.transform, false);
        var panelImg = panelGO.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.55f);

        var rt = panelGO.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(1f, 0f);
        rt.anchorMax        = new Vector2(1f, 0f);
        rt.pivot            = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-12f, 12f);
        rt.sizeDelta        = new Vector2(200f, 30f);

        // ── Etiqueta de texto ───────────────────────────────────────────
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(panelGO.transform, false);
        _labelBombas = labelGO.AddComponent<Text>();

        // Fuente built-in de Unity como fallback seguro
        _labelBombas.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _labelBombas.fontSize  = 15;
        _labelBombas.color     = Color.red;
        _labelBombas.alignment = TextAnchor.MiddleCenter;

        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = new Vector2(4f, 2f);
        labelRT.offsetMax = new Vector2(-4f, -2f);

        // Escribir valores iniciales
        ActualizarHUD(force: true);
    }

    /// <summary>
    /// Actualiza el texto del HUD solo cuando los valores han cambiado (cero GC por frame).
    /// </summary>
    private void ActualizarHUD(bool force = false)
    {
        if (_labelBombas == null) return;

        if (!force && _bombasAnterior == bombasDisponibles && _colocadasAnterior == bombas.Count)
            return;  // nada cambió → sin alloc de string

        _bombasAnterior    = bombasDisponibles;
        _colocadasAnterior = bombas.Count;

        _labelBombas.color = bombasDisponibles > 0 ? Color.red : new Color(0.5f, 0.5f, 0.5f, 1f);
        _labelBombas.text  = $"Bombas: {bombasDisponibles}   [{bombas.Count} colocadas]";
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CLASE AUXILIAR
    // ═══════════════════════════════════════════════════════════════════════

    private class BombaColocada
    {
        public GameObject objetoFisico;
        public Vector3    posicion;
        public bool       explotada;
        public Coroutine  rutinaTimer; // V23 AUDIT
    }
}

// ─── Parpadeo de la luz de la bomba ──────────────────────────────────────────
public class ParpadeoLuz : MonoBehaviour
{
    public Light luz;
    private float timer;
    
    // API Exclusiva de TDD
    public float TestTimer => timer;
    public void AvanzarTimer(float dt) => UpdateLogic(dt);

    private void Update()
    {
        UpdateLogic(Time.deltaTime);
    }
    
    private void UpdateLogic(float dt)
    {
        timer += dt * 4f;
        // V23 AUDIT FIX: Previene desbordamiento en GPU e inestabilidad del Mathf.Sin
        if (timer > Mathf.PI * 2f) timer -= Mathf.PI * 2f;
        
        if (luz != null)
            luz.enabled = Mathf.Sin(timer) > 0f;
    }
}
