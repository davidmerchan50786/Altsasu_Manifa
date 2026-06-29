// Assets/Scripts/Editor/VisorGIS.cs
// ═══════════════════════════════════════════════════════════════════════════
//  VISOR GIS — mapa 2D interactivo de datos geoespaciales de Alsasua
//
//  Menú: Tools / Alsasua / 🗺 Visor GIS
//
//  Capas visualizables (toggle individual):
//    • Tiles mosaico V2  — cuadrícula 14.4×14.4 km coloreada por anillo
//    • Edificios OSM     — footprints buildings_unity.json (1.030 polígonos)
//    • Árboles LIDAR     — puntos trees_unity.json (2.956 árboles)
//    • Ríos              — polilíneas rios_ejes.geojson (UTM → Unity)
//    • Zonas gameplay    — círculos SistemaZonas en escena (solo en Play)
//    • Puntos de interés — Herriko Plaza, carreteras N-1 y cruces clave
//
//  Interacción:
//    • Rueda/drag: zoom y paneo sobre el canvas IMGUI
//    • Clic derecho → coordenadas UTM + Unity del punto seleccionado
//    • Doble clic    → mueve SceneView a ese punto (si hay escena abierta)
//    • Botón "Snap al jugador" — centra el mapa en posición runtime del jugador
//
//  Implementación: IMGUI puro (sin UIElements) para máxima compatibilidad
//  con el pipeline de proyecto (Unity 2022+, HDRP).
//  Los JSON se cargan una vez y se cachean; el botón ↺ los recarga.
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public sealed class VisorGIS : EditorWindow
{
    // ── Menú ─────────────────────────────────────────────────────────────────
    [MenuItem("Tools/Alsasua/🗺 Visor GIS")]
    static void Abrir() => GetWindow<VisorGIS>("Visor GIS");

    // ── Rutas de datos ────────────────────────────────────────────────────────
    const string RUTA_MANIFEST   = "Assets/AlsasuaData/terrain_tiles_v2/manifest_v2.json";
    const string RUTA_EDIFICIOS  = "Assets/AlsasuaData/buildings_unity.json";
    const string RUTA_ARBOLES    = "Assets/AlsasuaData/trees_unity.json";
    const string RUTA_RIOS       = "Assets/AlsasuaData/rios_ejes.geojson";
    const string RUTA_AUDIT      = "Assets/AlsasuaData/terrain_v2_audit_report.json";

    // ── Mundo (coordenadas Unity) ─────────────────────────────────────────────
    // El mosaico abarca 14.4 km en Z y ~14.4 km en X (comprimido 0.93687).
    const float MUNDO_ANCHO  = 14400f * (76400f / 81548f); // ~13490 u
    const float MUNDO_ALTO   = 14400f;
    const float MUNDO_OX     = 1918f - 14400f * (76400f / 81548f) * 0.5f;
    const float MUNDO_OZ     = 8570f - 14400f * 0.5f;

    // ── Estado de la ventana ──────────────────────────────────────────────────
    Vector2 _pan       = Vector2.zero;   // desplazamiento canvas en px
    float   _zoom      = 1f;
    Vector2 _dragStart;
    bool    _dragging;

    // ── Capas ────────────────────────────────────────────────────────────────
    bool _mostrarTiles     = true;
    bool _mostrarEdificios = true;
    bool _mostrarArboles   = true;
    bool _mostrarRios      = true;
    bool _mostrarZonas     = true;
    bool _mostrarPOI       = true;

    // ── Datos cacheados ───────────────────────────────────────────────────────
    List<TileDato>     _tiles;
    List<EdificioDato> _edificios;
    List<Vector2>      _arboles;
    List<List<Vector2>> _rios;
    bool               _cargado;

    // ── Coordenadas del clic derecho ─────────────────────────────────────────
    Vector2? _puntoClic;
    Vector2  _clicUnity;

    // ── Structs ligeros ───────────────────────────────────────────────────────
    struct TileDato
    {
        public Rect   bounds;   // coords Unity
        public int    anillo;
        public string id;
        public bool   auditOk;
    }

    struct EdificioDato
    {
        public Vector2 centro;
        public float   ancho, alto;   // bbox aprox.
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Ciclo de vida
    // ─────────────────────────────────────────────────────────────────────────

    void OnEnable()
    {
        _cargado = false;
        wantsMouseMove = true;
        titleContent = new GUIContent("Visor GIS", EditorGUIUtility.IconContent("TerrainInspector.TerrainToolSplat").image);
    }

    void OnGUI()
    {
        if (!_cargado) CargarDatos();

        DibujarBarra();

        Rect canvas = new Rect(0, 44, position.width, position.height - 44);
        GUI.BeginClip(canvas);
        DibujarCanvas(new Rect(0, 0, canvas.width, canvas.height));
        GUI.EndClip();

        ManejarEventos(canvas);
        DibujarTooltipClic();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Barra de herramientas
    // ─────────────────────────────────────────────────────────────────────────

    void DibujarBarra()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        _mostrarTiles     = GUILayout.Toggle(_mostrarTiles,     "Tiles",      EditorStyles.toolbarButton, GUILayout.Width(46));
        _mostrarEdificios = GUILayout.Toggle(_mostrarEdificios, "Edificios",  EditorStyles.toolbarButton, GUILayout.Width(62));
        _mostrarArboles   = GUILayout.Toggle(_mostrarArboles,   "Árboles",   EditorStyles.toolbarButton, GUILayout.Width(56));
        _mostrarRios      = GUILayout.Toggle(_mostrarRios,      "Ríos",       EditorStyles.toolbarButton, GUILayout.Width(40));
        _mostrarZonas     = GUILayout.Toggle(_mostrarZonas,     "Zonas",     EditorStyles.toolbarButton, GUILayout.Width(48));
        _mostrarPOI       = GUILayout.Toggle(_mostrarPOI,       "POI",        EditorStyles.toolbarButton, GUILayout.Width(36));

        GUILayout.Space(8);
        if (GUILayout.Button("↺", EditorStyles.toolbarButton, GUILayout.Width(24)))
        {
            _cargado = false;
            Repaint();
        }
        if (GUILayout.Button("⌂ Jugador", EditorStyles.toolbarButton, GUILayout.Width(70)))
            CentrarEnJugador();
        if (GUILayout.Button("Reset", EditorStyles.toolbarButton, GUILayout.Width(42)))
        {
            _pan  = Vector2.zero;
            _zoom = 1f;
            Repaint();
        }

        GUILayout.FlexibleSpace();

        // info zoom
        GUILayout.Label($"Zoom {_zoom:F2}×", EditorStyles.miniLabel, GUILayout.Width(68));

        EditorGUILayout.EndHorizontal();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Canvas principal (IMGUI)
    // ─────────────────────────────────────────────────────────────────────────

    void DibujarCanvas(Rect r)
    {
        // fondo
        EditorGUI.DrawRect(r, new Color(0.12f, 0.14f, 0.16f));

        // Cuadrícula de referencia
        DibujarCuadricula(r);

        if (_mostrarTiles)     DibujarTiles(r);
        if (_mostrarRios)      DibujarRios(r);
        if (_mostrarEdificios) DibujarEdificios(r);
        if (_mostrarArboles)   DibujarArboles(r);
        if (_mostrarZonas)     DibujarZonasPlay(r);
        if (_mostrarPOI)       DibujarPOI(r);

        // Cruz del punto clicado
        if (_puntoClic.HasValue)
        {
            var p = MundoACanvas(_puntoClic.Value, r);
            Handles.color = Color.yellow;
            Handles.DrawLine(new Vector3(p.x - 8, p.y), new Vector3(p.x + 8, p.y));
            Handles.DrawLine(new Vector3(p.x, p.y - 8), new Vector3(p.x, p.y + 8));
        }
    }

    void DibujarCuadricula(Rect r)
    {
        // líneas cada 1000 m Unity
        Handles.color = new Color(1, 1, 1, 0.05f);
        for (float wx = Mathf.Floor(MUNDO_OX / 1000f) * 1000f; wx < MUNDO_OX + MUNDO_ANCHO + 1000; wx += 1000f)
        {
            var a = MundoACanvas(new Vector2(wx, MUNDO_OZ), r);
            var b = MundoACanvas(new Vector2(wx, MUNDO_OZ + MUNDO_ALTO), r);
            Handles.DrawLine(new Vector3(a.x, a.y), new Vector3(b.x, b.y));
        }
        for (float wz = Mathf.Floor(MUNDO_OZ / 1000f) * 1000f; wz < MUNDO_OZ + MUNDO_ALTO + 1000; wz += 1000f)
        {
            var a = MundoACanvas(new Vector2(MUNDO_OX, wz), r);
            var b = MundoACanvas(new Vector2(MUNDO_OX + MUNDO_ANCHO, wz), r);
            Handles.DrawLine(new Vector3(a.x, a.y), new Vector3(b.x, b.y));
        }
    }

    void DibujarTiles(Rect r)
    {
        if (_tiles == null) return;

        Color[] coloresAnillo =
        {
            new Color(0.2f, 0.5f, 0.9f, 0.35f),   // anillo 0 — azul
            new Color(0.2f, 0.75f, 0.35f, 0.22f),  // anillo 1 — verde
            new Color(0.75f, 0.55f, 0.2f, 0.18f),  // anillo 2 — naranja
        };
        Color colAuditFail = new Color(0.9f, 0.15f, 0.1f, 0.35f);

        foreach (var t in _tiles)
        {
            var min = MundoACanvas(new Vector2(t.bounds.xMin, t.bounds.yMin), r);
            var max = MundoACanvas(new Vector2(t.bounds.xMax, t.bounds.yMax), r);
            var rect = Rect.MinMaxRect(min.x, max.y, max.x, min.y); // Y invertida

            int ai = Mathf.Clamp(t.anillo, 0, 2);
            EditorGUI.DrawRect(rect, t.auditOk ? coloresAnillo[ai] : colAuditFail);

            // borde
            Handles.color = new Color(1, 1, 1, 0.18f);
            Handles.DrawWireDisc(Vector3.zero, Vector3.forward, 0); // forzar contexto
            DrawRect2D(rect, new Color(1, 1, 1, 0.18f));

            // label si hay zoom suficiente
            if (_zoom > 3f && rect.width > 30)
            {
                GUI.Label(rect, t.id, new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = new Color(1, 1, 1, 0.6f) }, fontSize = 8 });
            }
        }

        // leyenda inline
        DibujarLeyendaTiles(r);
    }

    void DibujarLeyendaTiles(Rect r)
    {
        float px = r.width - 140, py = 8;
        GUIStyle st = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.white } };

        EditorGUI.DrawRect(new Rect(px - 4, py - 2, 138, 58), new Color(0, 0, 0, 0.55f));
        EditorGUI.DrawRect(new Rect(px, py + 2,  12, 10), new Color(0.2f, 0.5f, 0.9f, 0.7f));
        GUI.Label(new Rect(px + 16, py, 120, 14), "Anillo 0 urbano (0.59m/px)", st);
        EditorGUI.DrawRect(new Rect(px, py + 18, 12, 10), new Color(0.2f, 0.75f, 0.35f, 0.7f));
        GUI.Label(new Rect(px + 16, py + 16, 120, 14), "Anillo 1 valle (1.17m/px)", st);
        EditorGUI.DrawRect(new Rect(px, py + 34, 12, 10), new Color(0.75f, 0.55f, 0.2f, 0.7f));
        GUI.Label(new Rect(px + 16, py + 32, 120, 14), "Anillo 2 sierras (3.5m/px)", st);
    }

    void DibujarEdificios(Rect r)
    {
        if (_edificios == null) return;
        Handles.color = new Color(0.95f, 0.75f, 0.3f, 0.6f);
        foreach (var e in _edificios)
        {
            var c = MundoACanvas(e.centro, r);
            float px = Mathf.Max(1.5f, e.ancho * _zoom * 0.5f);
            float pz = Mathf.Max(1.5f, e.alto  * _zoom * 0.5f);
            DrawRect2D(new Rect(c.x - px, c.y - pz, px * 2, pz * 2), new Color(0.95f, 0.75f, 0.3f, 0.45f));
        }
    }

    void DibujarArboles(Rect r)
    {
        if (_arboles == null) return;
        float radio = Mathf.Max(1f, 2f * _zoom);
        Handles.color = new Color(0.25f, 0.85f, 0.3f, 0.55f);
        foreach (var a in _arboles)
        {
            var c = MundoACanvas(a, r);
            if (c.x < -2 || c.x > r.width + 2 || c.y < -2 || c.y > r.height + 2) continue;
            Handles.DrawSolidDisc(new Vector3(c.x, c.y, 0), Vector3.forward, radio);
        }
    }

    void DibujarRios(Rect r)
    {
        if (_rios == null) return;
        Handles.color = new Color(0.3f, 0.6f, 0.95f, 0.85f);
        foreach (var polilínea in _rios)
        {
            for (int i = 1; i < polilínea.Count; i++)
            {
                var a = MundoACanvas(polilínea[i - 1], r);
                var b = MundoACanvas(polilínea[i], r);
                Handles.DrawAAPolyLine(2.5f, new Vector3(a.x, a.y), new Vector3(b.x, b.y));
            }
        }
    }

    void DibujarZonasPlay(Rect r)
    {
        if (!Application.isPlaying) return;
        var zonas = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        foreach (var z in zonas)
        {
            if (z.GetType().Name != "SistemaZonas") continue;
            // Dibujar posición de la zona como círculo
            var c = MundoACanvas(new Vector2(z.transform.position.x, z.transform.position.z), r);
            Handles.color = new Color(1f, 0.4f, 0.4f, 0.4f);
            Handles.DrawWireDisc(new Vector3(c.x, c.y, 0), Vector3.forward, 20f * _zoom);
        }
    }

    void DibujarPOI(Rect r)
    {
        // Herriko Plaza
        DibujarPunto(r, new Vector2(1918f, 8570f), new Color(1f, 0.9f, 0.1f), "Herriko Plaza", 5f);

        // Carretera N-1 Norte/Sur (valores aproximados del proyecto)
        DibujarPunto(r, new Vector2(1918f, 8570f + 2000f), new Color(0.9f, 0.4f, 0.1f), "N-1 Norte", 4f);
        DibujarPunto(r, new Vector2(1918f, 8570f - 1500f), new Color(0.9f, 0.4f, 0.1f), "N-1 Sur",   4f);

        // Origen mundo (0,0) Unity para referencia
        DibujarPunto(r, new Vector2(0f, 0f), new Color(0.5f, 0.5f, 1f, 0.7f), "(0,0)", 3f);
    }

    void DibujarPunto(Rect r, Vector2 mundoXZ, Color col, string etiqueta, float radio)
    {
        var c = MundoACanvas(mundoXZ, r);
        Handles.color = col;
        Handles.DrawSolidDisc(new Vector3(c.x, c.y, 0), Vector3.forward, radio);
        Handles.color = Color.white;
        Handles.DrawWireDisc(new Vector3(c.x, c.y, 0), Vector3.forward, radio + 1f);
        if (_zoom > 0.5f)
        {
            GUI.Label(new Rect(c.x + radio + 2, c.y - 8, 120, 16),
                etiqueta,
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = col } });
        }
    }

    void DibujarTooltipClic()
    {
        if (!_puntoClic.HasValue) return;

        GeoDataAlsasua.UnityAUTM(_clicUnity.x, _clicUnity.y, out double e, out double n);
        string txt = $"Unity ({_clicUnity.x:F1}, {_clicUnity.y:F1})\nUTM E={e:F1}  N={n:F1}";

        float w = 220, h = 36;
        var mousePos = Event.current.mousePosition;
        var rect = new Rect(mousePos.x + 12, mousePos.y - h - 4, w, h);
        if (rect.xMax > position.width)  rect.x = mousePos.x - w - 4;
        if (rect.yMin < 44)              rect.y = mousePos.y + 4;

        EditorGUI.DrawRect(rect, new Color(0.1f, 0.1f, 0.1f, 0.88f));
        GUI.Label(rect, txt, new GUIStyle(EditorStyles.miniLabel)
            { normal = { textColor = Color.white }, wordWrap = true });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Eventos de ratón
    // ─────────────────────────────────────────────────────────────────────────

    void ManejarEventos(Rect canvas)
    {
        var e = Event.current;
        if (e == null) return;

        Vector2 mouseLocal = e.mousePosition - new Vector2(0, 44);
        bool enCanvas = canvas.Contains(e.mousePosition);

        switch (e.type)
        {
            case EventType.ScrollWheel when enCanvas:
                float factor = e.delta.y > 0 ? 0.87f : 1.15f;
                // zoom centrado en el ratón
                var antesUnity = CanvasAMundo(mouseLocal, canvas);
                _zoom = Mathf.Clamp(_zoom * factor, 0.05f, 50f);
                var despuesCanvas = MundoACanvas(antesUnity, canvas);
                _pan += mouseLocal - despuesCanvas;
                e.Use();
                Repaint();
                break;

            case EventType.MouseDown when e.button == 0 && enCanvas:
                _dragging  = true;
                _dragStart = e.mousePosition - _pan;
                _puntoClic = null;
                e.Use();
                break;

            case EventType.MouseDown when e.button == 1 && enCanvas:
                _clicUnity = CanvasAMundo(mouseLocal, canvas);
                _puntoClic = _clicUnity;
                e.Use();
                Repaint();
                break;

            case EventType.MouseDown when e.button == 0 && e.clickCount == 2 && enCanvas:
                IrASceneView(CanvasAMundo(mouseLocal, canvas));
                e.Use();
                break;

            case EventType.MouseDrag when _dragging:
                _pan = e.mousePosition - _dragStart;
                e.Use();
                Repaint();
                break;

            case EventType.MouseUp when e.button == 0:
                _dragging = false;
                e.Use();
                break;

            case EventType.MouseMove:
                Repaint();
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Coordenadas
    // ─────────────────────────────────────────────────────────────────────────

    Vector2 MundoACanvas(Vector2 mundoXZ, Rect canvas)
    {
        float cx = (mundoXZ.x - MUNDO_OX) / MUNDO_ANCHO * canvas.width  * _zoom + _pan.x;
        float cy = (1f - (mundoXZ.y - MUNDO_OZ) / MUNDO_ALTO) * canvas.height * _zoom + _pan.y;
        return new Vector2(cx, cy);
    }

    Vector2 CanvasAMundo(Vector2 canvasXY, Rect canvas)
    {
        float wx = (canvasXY.x - _pan.x) / (_zoom * canvas.width)  * MUNDO_ANCHO + MUNDO_OX;
        float wz = (1f - (canvasXY.y - _pan.y) / (_zoom * canvas.height)) * MUNDO_ALTO + MUNDO_OZ;
        return new Vector2(wx, wz);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Acciones
    // ─────────────────────────────────────────────────────────────────────────

    void CentrarEnJugador()
    {
        var jugador = FindAnyObjectByType<ControladorJugador>();
        if (jugador == null) { Debug.Log("[VisorGIS] Jugador no encontrado en escena."); return; }
        Vector3 pos = jugador.transform.position;
        _puntoClic = new Vector2(pos.x, pos.z);
        _clicUnity = _puntoClic.Value;
        Repaint();
    }

    void IrASceneView(Vector2 mundoXZ)
    {
        var sv = SceneView.lastActiveSceneView;
        if (sv == null) return;
        float y = Terrain.activeTerrain != null
            ? Terrain.activeTerrain.SampleHeight(new Vector3(mundoXZ.x, 0, mundoXZ.y)) + 10f
            : 30f;
        sv.LookAt(new Vector3(mundoXZ.x, y, mundoXZ.y), sv.rotation, 200f);
        sv.Repaint();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Carga de datos
    // ─────────────────────────────────────────────────────────────────────────

    void CargarDatos()
    {
        _cargado   = true;
        _tiles     = CargarTiles();
        _edificios = CargarEdificios();
        _arboles   = CargarArboles();
        _rios      = CargarRios();
    }

    List<TileDato> CargarTiles()
    {
        string ruta = Path.Combine(Application.dataPath, "..", RUTA_MANIFEST);
        if (!File.Exists(ruta)) return null;

        HashSet<string> okSet = CargarAuditOk();

        var lista = new List<TileDato>();
        try
        {
            var root = JObject.Parse(File.ReadAllText(ruta));
            var tiles = root["tiles"] as JArray;
            if (tiles == null) return lista;
            foreach (var t in tiles)
            {
                string id  = t["id"]?.ToString() ?? "";
                int anillo = t["ring"]?.Value<int>() ?? 0;
                float ux   = t["unity_x"]?.Value<float>() ?? 0f;
                float uz   = t["unity_z"]?.Value<float>() ?? 0f;
                float sz   = t["size"]?.Value<float>() ?? 1200f;
                float sx   = sz * (76400f / 81548f);
                lista.Add(new TileDato
                {
                    id     = id,
                    anillo = anillo,
                    bounds = new Rect(ux, uz, sx, sz),
                    auditOk = okSet.Contains(id)
                });
            }
        }
        catch (Exception ex) { Debug.LogWarning($"[VisorGIS] manifest: {ex.Message}"); }
        return lista;
    }

    HashSet<string> CargarAuditOk()
    {
        var set  = new HashSet<string>();
        string r = Path.Combine(Application.dataPath, "..", RUTA_AUDIT);
        if (!File.Exists(r)) return set;
        try
        {
            var root = JObject.Parse(File.ReadAllText(r));
            var tiles = root["tiles"] as JArray;
            if (tiles == null) return set;
            foreach (var t in tiles)
                if (t["ok"]?.Value<bool>() == true)
                    set.Add(t["id"]?.ToString() ?? "");
        }
        catch { /* sin auditoría, todos sin marca */ }
        return set;
    }

    List<EdificioDato> CargarEdificios()
    {
        string ruta = Path.Combine(Application.dataPath, "..", RUTA_EDIFICIOS);
        if (!File.Exists(ruta)) return null;
        var lista = new List<EdificioDato>();
        try
        {
            var arr = JArray.Parse(File.ReadAllText(ruta));
            foreach (var e in arr)
            {
                float ux = e["ux"]?.Value<float>() ?? e["x"]?.Value<float>() ?? 0f;
                float uz = e["uz"]?.Value<float>() ?? e["z"]?.Value<float>() ?? 0f;
                float w  = e["width"]?.Value<float>()  ?? e["ancho"]?.Value<float>() ?? 8f;
                float h  = e["depth"]?.Value<float>()  ?? e["largo"]?.Value<float>() ?? 8f;
                lista.Add(new EdificioDato { centro = new Vector2(ux, uz), ancho = w, alto = h });
            }
        }
        catch (Exception ex) { Debug.LogWarning($"[VisorGIS] edificios: {ex.Message}"); }
        return lista;
    }

    List<Vector2> CargarArboles()
    {
        string ruta = Path.Combine(Application.dataPath, "..", RUTA_ARBOLES);
        if (!File.Exists(ruta)) return null;
        var lista = new List<Vector2>();
        try
        {
            // Soporta tanto array de objetos como array de arrays
            var tok = JToken.Parse(File.ReadAllText(ruta));
            var arr = tok.Type == JTokenType.Object ? tok["trees"] as JArray : tok as JArray;
            if (arr == null) return lista;
            foreach (var a in arr)
            {
                float ux = a["ux"]?.Value<float>() ?? a["x"]?.Value<float>() ?? 0f;
                float uz = a["uz"]?.Value<float>() ?? a["z"]?.Value<float>() ?? 0f;
                lista.Add(new Vector2(ux, uz));
            }
        }
        catch (Exception ex) { Debug.LogWarning($"[VisorGIS] árboles: {ex.Message}"); }
        return lista;
    }

    List<List<Vector2>> CargarRios()
    {
        string ruta = Path.Combine(Application.dataPath, "..", RUTA_RIOS);
        if (!File.Exists(ruta)) return null;
        var lista = new List<List<Vector2>>();
        try
        {
            var root     = JObject.Parse(File.ReadAllText(ruta));
            var features = root["features"] as JArray;
            if (features == null) return lista;
            foreach (var feat in features)
            {
                var geo  = feat["geometry"];
                var tipo = geo?["type"]?.ToString();
                if (tipo == "LineString")
                {
                    lista.Add(GeoJSONLineAUnity(geo["coordinates"] as JArray));
                }
                else if (tipo == "MultiLineString")
                {
                    var coords = geo["coordinates"] as JArray;
                    if (coords != null)
                        foreach (var seg in coords)
                            lista.Add(GeoJSONLineAUnity(seg as JArray));
                }
            }
        }
        catch (Exception ex) { Debug.LogWarning($"[VisorGIS] ríos: {ex.Message}"); }
        return lista;
    }

    static List<Vector2> GeoJSONLineAUnity(JArray coords)
    {
        var pts = new List<Vector2>();
        if (coords == null) return pts;
        foreach (var c in coords)
        {
            // GeoJSON [lon, lat] WGS84 → UTM 30N ETRS89 (aprox. lineal, error <2m en zona)
            double lon = c[0]?.Value<double>() ?? 0;
            double lat = c[1]?.Value<double>() ?? 0;
            double e = GeoDataAlsasua.UTM_E_ORIGIN + (lon - GeoDataAlsasua.LONGITUD_CENTRO) * GeoDataAlsasua.M_POR_GRADO_LON;
            double n = GeoDataAlsasua.UTM_N_ORIGIN + (lat - GeoDataAlsasua.LATITUD_CENTRO)  * GeoDataAlsasua.M_POR_GRADO_LAT;
            pts.Add(GeoDataAlsasua.UTMaUnity(e, n));
        }
        return pts;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers de dibujo
    // ─────────────────────────────────────────────────────────────────────────

    static void DrawRect2D(Rect r, Color col)
    {
        Handles.color = col;
        var tl = new Vector3(r.xMin, r.yMin);
        var tr = new Vector3(r.xMax, r.yMin);
        var br = new Vector3(r.xMax, r.yMax);
        var bl = new Vector3(r.xMin, r.yMax);
        Handles.DrawLine(tl, tr);
        Handles.DrawLine(tr, br);
        Handles.DrawLine(br, bl);
        Handles.DrawLine(bl, tl);
    }
}
