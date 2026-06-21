// Assets/Scripts/Runtime/SistemaMinimapa.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SISTEMA MINIMAPA + MAPA COMPLETO
//
//  Minimapa (esquina inferior-derecha, siempre visible):
//    • Circular con máscara procedural (sin sprites externos)
//    • Player-up: la cámara gira con el jugador (norte arriba a 0°)
//    • Flecha del jugador (blanca, apunta hacia adelante)
//    • Indicador de norte ("N")
//    • 3 niveles de zoom: Z → 60 m / 120 m / 240 m
//    • Blips: policía (rojo), vehículo jugador (amarillo), objetivo misión (cian)
//
//  Mapa completo (tecla M):
//    • Panel 85% pantalla con fondo oscuro
//    • Cámara ortográfica a Y=3000 cubre 14.4 km (radio 8000 m)
//    • Punto del jugador con dirección
//    • [M] o [ESC] para cerrar
//
//  Auto-arranca con [RuntimeInitializeOnLoadMethod].
//  Deshabilita la MinimapCam legacy de HUDCanvas para evitar render doble.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DefaultExecutionOrder(70)]
public sealed class SistemaMinimapa : MonoBehaviour
{
    // ── Constantes ────────────────────────────────────────────────────────────
    const int   RT_MINI    = 256;
    const int   RT_MAPA    = 512;
    const float MINI_PX    = 164f;    // tamaño del panel en píxeles
    const float MINI_Y_CAM = 120f;    // altura de la cámara sobre el jugador
    const float MAPA_Y_CAM = 3000f;   // altura de la cámara del mapa completo
    const float MAPA_ORTHO = 8000f;   // radio visible del mapa (8 km → cubre 14.4 km de lado)

    static readonly float[] ZOOMS = { 60f, 120f, 240f };  // radio en metros
    static readonly string[] ZOOM_LABELS = { "60m", "120m", "240m" };

    // ── Minimapa ──────────────────────────────────────────────────────────────
    Canvas        _canvas;
    Camera        _miniCam;
    RenderTexture _miniRT;
    RectTransform _miniPanel;
    RawImage      _miniImg;
    RectTransform _playerArrowRT;  // flecha del jugador (Image rotada)
    Text          _txtZoom;
    Text          _txtNorte;
    Text          _txtCoords;
    int           _zoomIdx   = 0;
    float         _miniTimer;

    // Blips
    const int POOL_SIZE = 20;
    readonly Image[] _blipImg   = new Image[POOL_SIZE];
    readonly Color[] _blipColor = new Color[POOL_SIZE];
    float _blipTimer;
    struct BlipTarget { public Transform t; public Color c; }
    readonly List<BlipTarget> _blipTargets = new(POOL_SIZE);

    // ── Blip de misión (API estática para SistemaMisiones) ───────────────────
    static Transform _blipMision;

    /// <summary>
    /// Actualiza el blip cian de objetivo en el minimapa.
    /// Pasa null para ocultarlo (misión completada).
    /// </summary>
    public static void ActualizarBlipMision(Transform objetivo)
        => _blipMision = objetivo;

    // ── Mapa completo ─────────────────────────────────────────────────────────
    GameObject    _mapaPanel;
    Camera        _mapaCam;
    RenderTexture _mapaRT;
    RawImage      _mapaImg;
    Image         _mapPlayerArrow;
    Text          _mapHint;
    bool          _mapaVisible;

    // ── Bootstrap ─────────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<SistemaMinimapa>() != null) return;
        var go = new GameObject("SistemaMinimapa");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaMinimapa>();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ═══════════════════════════════════════════════════════════════════════════

    void Start()
    {
        // Deshabilitar la MinimapCam legacy de HUDCanvas (si existe) para no
        // duplicar el render. La nuestra la reemplaza.
        var camVieja = GameObject.Find("MinimapCam");
        if (camVieja != null) camVieja.SetActive(false);

        CrearCanvas();
        CrearMinimap();
        CrearMapaCompleto();
        CrearCamaras();
    }

    void OnDestroy()
    {
        if (_miniRT != null) { _miniRT.Release(); Destroy(_miniRT); }
        if (_mapaRT != null) { _mapaRT.Release(); Destroy(_mapaRT); }
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.mKey.wasPressedThisFrame) ToggleMapa();
            if (kb.zKey.wasPressedThisFrame) CiclarZoom();
            if (_mapaVisible && kb.escapeKey.wasPressedThisFrame) CerrarMapa();
        }

        ActualizarMinimap();
        ActualizarBlips();
        if (_mapaVisible) ActualizarMapaCompleto();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  CONSTRUCCIÓN UI
    // ═══════════════════════════════════════════════════════════════════════════

    void CrearCanvas()
    {
        var go = new GameObject("MinimapCanvas");
        go.transform.SetParent(transform);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 50;   // por encima del HUD normal (que suele estar en 0-10)
        var cs = go.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        go.AddComponent<GraphicRaycaster>();
    }

    void CrearMinimap()
    {
        _miniRT = new RenderTexture(RT_MINI, RT_MINI, 16) { name = "MinimapRT" };
        _miniRT.Create();

        // ── Panel raíz (esquina inferior-derecha) ────────────────────────────
        _miniPanel = MkRect("PanelMinimap", _canvas.transform,
            anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 0f),
            pivot:     new Vector2(1f, 0f),
            anchPos:   new Vector2(-12f, 12f),
            size:      new Vector2(MINI_PX, MINI_PX));

        // ── Máscara circular (Mask + Image con sprite circular) ──────────────
        var maskGO = MkGO("MaskCircle", _miniPanel);
        var maskRT = maskGO.AddComponent<RectTransform>();
        maskRT.anchorMin = Vector2.zero; maskRT.anchorMax = Vector2.one;
        maskRT.offsetMin = maskRT.offsetMax = Vector2.zero;
        var maskImg = maskGO.AddComponent<Image>();
        maskImg.sprite = CrearCirculoSprite(128);
        maskImg.color  = Color.white;
        var mask = maskGO.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        // ── RenderTexture dentro de la máscara ───────────────────────────────
        var imgGO = MkGO("MinimapImg", maskRT);
        var imgRT = imgGO.AddComponent<RectTransform>();
        imgRT.anchorMin = Vector2.zero; imgRT.anchorMax = Vector2.one;
        imgRT.offsetMin = imgRT.offsetMax = Vector2.zero;
        _miniImg = imgGO.AddComponent<RawImage>();
        _miniImg.texture = _miniRT;

        // ── Blips (pool de imágenes) ─────────────────────────────────────────
        var blipRoot = MkGO("Blips", maskRT);
        var blipRootRT = blipRoot.AddComponent<RectTransform>();
        blipRootRT.anchorMin = Vector2.zero; blipRootRT.anchorMax = Vector2.one;
        blipRootRT.offsetMin = blipRootRT.offsetMax = Vector2.zero;

        for (int i = 0; i < POOL_SIZE; i++)
        {
            var b = MkGO($"Blip{i}", blipRootRT);
            var bRT = b.AddComponent<RectTransform>();
            bRT.sizeDelta = new Vector2(7f, 7f);
            bRT.anchorMin = bRT.anchorMax = new Vector2(0.5f, 0.5f);
            _blipImg[i] = b.AddComponent<Image>();
            _blipImg[i].sprite = CrearCirculoSprite(16);
            b.SetActive(false);
        }

        // ── Flecha del jugador (siempre en el centro, rota con el heading) ───
        var arrowGO = MkGO("PlayerArrow", _miniPanel);
        _playerArrowRT = arrowGO.AddComponent<RectTransform>();
        _playerArrowRT.sizeDelta = new Vector2(12f, 14f);
        _playerArrowRT.anchorMin = _playerArrowRT.anchorMax = new Vector2(0.5f, 0.5f);
        _playerArrowRT.anchoredPosition = Vector2.zero;
        // Triángulo direccional via Image (cuadrado rotado 45° como diamante → o directamente)
        var arrowImg = arrowGO.AddComponent<Image>();
        arrowImg.color = new Color(0.3f, 1f, 0.4f, 0.95f); // verde lima

        // ── Norte ("N") ──────────────────────────────────────────────────────
        var nGO = MkGO("Norte", _miniPanel);
        var nRT = nGO.AddComponent<RectTransform>();
        nRT.sizeDelta = new Vector2(18f, 18f);
        nRT.anchorMin = nRT.anchorMax = new Vector2(0.5f, 1f);
        nRT.anchoredPosition = new Vector2(0f, -14f);
        _txtNorte = nGO.AddComponent<Text>();
        _txtNorte.text      = "N";
        _txtNorte.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _txtNorte.fontSize  = 12;
        _txtNorte.fontStyle = FontStyle.Bold;
        _txtNorte.alignment = TextAnchor.MiddleCenter;
        _txtNorte.color     = new Color(1f, 0.6f, 0.6f, 0.95f);

        // ── Borde circular decorativo ────────────────────────────────────────
        var bordeGO = MkGO("Borde", _miniPanel);
        var bordeRT = bordeGO.AddComponent<RectTransform>();
        bordeRT.anchorMin = Vector2.zero; bordeRT.anchorMax = Vector2.one;
        bordeRT.offsetMin = new Vector2(-2f, -2f); bordeRT.offsetMax = new Vector2(2f, 2f);
        var bordeImg = bordeGO.AddComponent<Image>();
        bordeImg.color  = new Color(0.18f, 0.45f, 0.90f, 0.80f);
        bordeImg.sprite = CrearAnilloSprite(128, 4);

        // ── Zoom label ───────────────────────────────────────────────────────
        var zGO = MkGO("Zoom", _miniPanel);
        var zRT = zGO.AddComponent<RectTransform>();
        zRT.sizeDelta = new Vector2(50f, 16f);
        zRT.anchorMin = zRT.anchorMax = new Vector2(0f, 0f);
        zRT.anchoredPosition = new Vector2(4f, -18f);
        _txtZoom = zGO.AddComponent<Text>();
        _txtZoom.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _txtZoom.fontSize  = 11;
        _txtZoom.alignment = TextAnchor.MiddleLeft;
        _txtZoom.color     = new Color(0.7f, 0.9f, 1f, 0.85f);
        ActualizarLabelZoom();

        // ── Coordenadas (debajo del minimapa) ────────────────────────────────
        var cGO = MkGO("Coords", _miniPanel);
        var cRT = cGO.AddComponent<RectTransform>();
        cRT.sizeDelta = new Vector2(MINI_PX, 14f);
        cRT.anchorMin = cRT.anchorMax = new Vector2(0.5f, 0f);
        cRT.anchoredPosition = new Vector2(0f, -16f);
        _txtCoords = cGO.AddComponent<Text>();
        _txtCoords.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _txtCoords.fontSize  = 10;
        _txtCoords.alignment = TextAnchor.MiddleCenter;
        _txtCoords.color     = new Color(0.6f, 0.8f, 0.6f, 0.75f);
    }

    void CrearMapaCompleto()
    {
        _mapaRT = new RenderTexture(RT_MAPA, RT_MAPA, 16) { name = "MapaCompletoRT" };
        _mapaRT.Create();

        // Panel oscuro fullscreen
        _mapaPanel = MkGO("PanelMapaCompleto", _canvas.transform);
        var panelRT = _mapaPanel.AddComponent<RectTransform>();
        panelRT.anchorMin = Vector2.zero; panelRT.anchorMax = Vector2.one;
        panelRT.offsetMin = new Vector2(40f, 40f); panelRT.offsetMax = new Vector2(-40f, -40f);
        var bg = _mapaPanel.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.05f, 0.08f, 0.94f);

        // Imagen del mapa
        var imgGO = MkGO("MapaImg", panelRT);
        var imgRT = imgGO.AddComponent<RectTransform>();
        imgRT.anchorMin = new Vector2(0.1f, 0.1f); imgRT.anchorMax = new Vector2(0.9f, 0.9f);
        imgRT.offsetMin = imgRT.offsetMax = Vector2.zero;
        _mapaImg = imgGO.AddComponent<RawImage>();
        _mapaImg.texture = _mapaRT;

        // Flecha del jugador en el mapa
        var arGO = MkGO("MapPlayerArrow", panelRT);
        var arRT = arGO.AddComponent<RectTransform>();
        arRT.sizeDelta = new Vector2(14f, 16f);
        arRT.anchorMin = arRT.anchorMax = new Vector2(0.5f, 0.5f);
        _mapPlayerArrow = arGO.AddComponent<Image>();
        _mapPlayerArrow.color = new Color(0.3f, 1f, 0.4f, 1f);

        // Título
        var tGO = MkGO("TituloMapa", panelRT);
        var tRT = tGO.AddComponent<RectTransform>();
        tRT.sizeDelta = new Vector2(400f, 30f);
        tRT.anchorMin = tRT.anchorMax = new Vector2(0.5f, 1f);
        tRT.anchoredPosition = new Vector2(0f, -20f);
        var tTxt = tGO.AddComponent<Text>();
        tTxt.text      = "ALTSASU — MAPA";
        tTxt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tTxt.fontSize  = 22;
        tTxt.fontStyle = FontStyle.Bold;
        tTxt.alignment = TextAnchor.MiddleCenter;
        tTxt.color     = new Color(0.7f, 0.85f, 1f, 0.90f);

        // Hint cierre
        var hGO = MkGO("HintCerrar", panelRT);
        var hRT = hGO.AddComponent<RectTransform>();
        hRT.sizeDelta = new Vector2(200f, 22f);
        hRT.anchorMin = hRT.anchorMax = new Vector2(0.5f, 0f);
        hRT.anchoredPosition = new Vector2(0f, 18f);
        _mapHint = hGO.AddComponent<Text>();
        _mapHint.text      = "[M] / [ESC] Cerrar";
        _mapHint.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _mapHint.fontSize  = 13;
        _mapHint.alignment = TextAnchor.MiddleCenter;
        _mapHint.color     = new Color(0.5f, 0.6f, 0.7f, 0.80f);

        _mapaPanel.SetActive(false);
    }

    void CrearCamaras()
    {
        // Minimapa camera
        var miniGO = new GameObject("MinimapCamera_SistemaMinimapa");
        DontDestroyOnLoad(miniGO);
        _miniCam = miniGO.AddComponent<Camera>();
        _miniCam.orthographic     = true;
        _miniCam.orthographicSize = ZOOMS[_zoomIdx];
        _miniCam.farClipPlane     = MINI_Y_CAM + 200f;
        _miniCam.cullingMask      = ~LayerMask.GetMask("UI", "Ignore Raycast");
        _miniCam.targetTexture    = _miniRT;
        _miniCam.clearFlags       = CameraClearFlags.SolidColor;
        _miniCam.backgroundColor  = new Color(0.05f, 0.08f, 0.10f);
        _miniCam.enabled          = false; // render manual

        // Mapa completo camera (centrada en Herriko Plaza, estática)
        var mapaGO = new GameObject("MapaCompletoCamera");
        DontDestroyOnLoad(mapaGO);
        _mapaCam = mapaGO.AddComponent<Camera>();
        _mapaCam.orthographic     = true;
        _mapaCam.orthographicSize = MAPA_ORTHO;
        _mapaCam.farClipPlane     = MAPA_Y_CAM + 500f;
        _mapaCam.cullingMask      = ~LayerMask.GetMask("UI", "Ignore Raycast");
        _mapaCam.targetTexture    = _mapaRT;
        _mapaCam.clearFlags       = CameraClearFlags.SolidColor;
        _mapaCam.backgroundColor  = new Color(0.04f, 0.05f, 0.08f);
        _mapaCam.enabled          = false;
        // Posición fija: sobre Herriko Plaza (GeoDataAlsasua.OX, OZ)
        mapaGO.transform.position    = new Vector3(1918f, MAPA_Y_CAM, 8570f);
        mapaGO.transform.eulerAngles = new Vector3(90f, 0f, 0f);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  UPDATE MINIMAP
    // ═══════════════════════════════════════════════════════════════════════════

    void ActualizarMinimap()
    {
        var jugador = AltsasuCore.Jugador;
        if (jugador == null || _miniCam == null) return;

        // Posicionar cámara sobre el jugador, rotada con su heading (player-up)
        float yaw = jugador.eulerAngles.y;
        _miniCam.transform.position    = jugador.position + Vector3.up * MINI_Y_CAM;
        _miniCam.transform.eulerAngles = new Vector3(90f, yaw, 0f);
        _miniCam.orthographicSize      = ZOOMS[_zoomIdx];

        // Rotar la "N" inversamente para que siempre apunte al norte real
        if (_txtNorte != null)
        {
            var nRT = _txtNorte.GetComponent<RectTransform>();
            if (nRT != null) nRT.localEulerAngles = new Vector3(0f, 0f, yaw);
        }

        // La flecha del jugador SIEMPRE apunta hacia arriba en pantalla (player-up)
        // No necesita rotar; la cámara ya gira con el jugador.
        // Actualizar coordenadas UTM
        if (_txtCoords != null)
        {
            GeoDataAlsasua.UnityAUTM(jugador.position.x, jugador.position.z,
                out double eUtm, out double nUtm);
            _txtCoords.text = $"E{eUtm:F0}  N{nUtm:F0}";
        }

        // Throttle de render (~8 fps para el minimapa)
        _miniTimer -= Time.deltaTime;
        if (_miniTimer <= 0f)
        {
            _miniTimer = 0.12f;
            _miniCam.Render();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  UPDATE BLIPS
    // ═══════════════════════════════════════════════════════════════════════════

    void ActualizarBlips()
    {
        _blipTimer -= Time.deltaTime;
        if (_blipTimer > 0f) return;
        _blipTimer = 0.5f;

        _blipTargets.Clear();
        var jugador = AltsasuCore.Jugador;
        if (jugador == null) goto ApplyBlips;

        float radio = ZOOMS[_zoomIdx] * 1.5f;

        // ── Policía (rojo) ───────────────────────────────────────────────────
        foreach (var p in FindObjectsByType<PoliciaForalIA>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (_blipTargets.Count >= POOL_SIZE - 2) break;
            if (Vector3.Distance(p.transform.position, jugador.position) > radio) continue;
            _blipTargets.Add(new BlipTarget { t = p.transform, c = new Color(1f, 0.15f, 0.15f) });
        }

        // ── Vehículo del jugador (amarillo) ──────────────────────────────────
        foreach (var v in FindObjectsByType<ControladorVehiculoJugador>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (_blipTargets.Count >= POOL_SIZE - 2) break;
            if (Vector3.Distance(v.transform.position, jugador.position) > radio) continue;
            _blipTargets.Add(new BlipTarget { t = v.transform, c = new Color(1f, 0.85f, 0.1f) });
        }

        // ── Objetivo de misión activa (cian, sin límite de distancia) ─────────
        if (_blipMision != null && _blipTargets.Count < POOL_SIZE)
            _blipTargets.Add(new BlipTarget { t = _blipMision, c = new Color(0.15f, 0.95f, 1f) });

        ApplyBlips:
        // Aplicar al pool de blips
        if (_miniCam == null) return;

        for (int i = 0; i < POOL_SIZE; i++)
        {
            if (i < _blipTargets.Count)
            {
                var bt  = _blipTargets[i];
                var blip = _blipImg[i];
                blip.gameObject.SetActive(true);
                blip.color = bt.c;

                // Posición en viewport de la cámara → anchoredPosition en el panel
                Vector3 vp = _miniCam.WorldToViewportPoint(bt.t.position);
                if (vp.z < 0f) { blip.gameObject.SetActive(false); continue; }
                // Clamp al círculo del minimapa
                var bRT = blip.rectTransform;
                float ux = (vp.x - 0.5f);
                float uy = (vp.y - 0.5f);
                float r  = Mathf.Sqrt(ux*ux + uy*uy);
                if (r > 0.48f) { ux *= 0.48f / r; uy *= 0.48f / r; } // clamp al borde
                bRT.anchoredPosition = new Vector2(ux * MINI_PX, uy * MINI_PX);
            }
            else
            {
                _blipImg[i].gameObject.SetActive(false);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  MAPA COMPLETO
    // ═══════════════════════════════════════════════════════════════════════════

    void ToggleMapa()
    {
        if (_mapaVisible) CerrarMapa(); else AbrirMapa();
    }

    void AbrirMapa()
    {
        _mapaVisible = true;
        _mapaPanel.SetActive(true);
        // Render único al abrir (el mapa es estático en su ortho)
        if (_mapaCam != null) _mapaCam.Render();
    }

    void CerrarMapa()
    {
        _mapaVisible = false;
        _mapaPanel.SetActive(false);
    }

    void ActualizarMapaCompleto()
    {
        var jugador = AltsasuCore.Jugador;
        if (jugador == null || _mapaCam == null || _mapPlayerArrow == null) return;

        // Posición del jugador en el mapa → viewport de la cámara del mapa
        Vector3 vp = _mapaCam.WorldToViewportPoint(jugador.position);

        // Calcular anchoredPosition dentro del panel de la imagen del mapa
        var imgRT = _mapaImg.rectTransform;
        float panelW = imgRT.rect.width;
        float panelH = imgRT.rect.height;
        if (panelW == 0f || panelH == 0f) return; // panel aún sin layout

        // Convertir viewport a posición local del panel
        Vector2 localPos = new Vector2(
            (vp.x - 0.5f) * panelW,
            (vp.y - 0.5f) * panelH);

        _mapPlayerArrow.rectTransform.anchoredPosition = localPos
            + imgRT.anchoredPosition; // relativo al panel raíz

        // Rotar flecha del mapa con el heading del jugador
        float yaw = jugador.eulerAngles.y;
        _mapPlayerArrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, -yaw);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  ZOOM
    // ═══════════════════════════════════════════════════════════════════════════

    void CiclarZoom()
    {
        _zoomIdx = (_zoomIdx + 1) % ZOOMS.Length;
        if (_miniCam != null) _miniCam.orthographicSize = ZOOMS[_zoomIdx];
        ActualizarLabelZoom();
    }

    void ActualizarLabelZoom()
    {
        if (_txtZoom != null) _txtZoom.text = $"[Z] {ZOOM_LABELS[_zoomIdx]}";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  HELPERS UI
    // ═══════════════════════════════════════════════════════════════════════════

    static GameObject MkGO(string nombre, Transform padre)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        return go;
    }

    static RectTransform MkRect(string nombre, Transform padre,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchPos, Vector2 size,
        Vector2 pivot = default)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot     = pivot == default ? new Vector2(0.5f, 0.5f) : pivot;
        rt.anchoredPosition = anchPos;
        rt.sizeDelta = size;
        return rt;
    }

    // ── Sprites procedurales ──────────────────────────────────────────────────

    static Sprite CrearCirculoSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = size * 0.5f, r = c - 1f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float dist = Mathf.Sqrt(dx*dx + dy*dy);
                float alpha = Mathf.Clamp01(1f - (dist - r + 1.5f) / 1.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
    }

    static Sprite CrearAnilloSprite(int size, int grosor)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = size * 0.5f, rExt = c - 1f, rInt = c - grosor;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float dist = Mathf.Sqrt(dx*dx + dy*dy);
                bool   enAnillo = dist <= rExt && dist >= rInt;
                float  edgeFade = Mathf.Clamp01(1f - (dist - rExt + 1.5f) / 1.5f)
                                * Mathf.Clamp01((dist - rInt + 1.5f) / 1.5f);
                tex.SetPixel(x, y, enAnillo ? new Color(1f, 1f, 1f, edgeFade) : Color.clear);
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
    }
}
