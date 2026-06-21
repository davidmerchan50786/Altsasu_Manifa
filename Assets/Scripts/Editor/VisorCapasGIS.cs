// Assets/Scripts/Editor/VisorCapasGIS.cs
// ─────────────────────────────────────────────────────────────────────────
//  Visor GIS — superpone en la Scene View todas las capas de referencia
//  (parcelas, calles, edificios, ferrocarril, patrimonio…) como Handles
//  para usarlas de guía al construir el mundo.
//
//  Abre con: Tools → Alsasua → 🗺 Visor Capas GIS
//  Requiere ejecutar primero: Tools/DescargarCapasGIS.py
// ─────────────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class VisorCapasGIS : EditorWindow
{
    // ── Modelo JSON (JsonUtility-compatible) ─────────────────────────────
    [Serializable] class CapaJson
    {
        public string source, typename, title;
        public int n;
        public FeatureJson[] features;
    }
    [Serializable] class FeatureJson
    {
        public string cat, nom, geom;
        public float[] coords;   // plano: E1,N1,E2,N2,… en UTM30N EPSG:25830
    }

    // ── Capa lista para dibujar ───────────────────────────────────────────
    class Capa
    {
        public string   archivo;
        public string   titulo;
        public string   source = "?";
        public bool     activa = true;
        public Color    color;
        public bool     cargada;
        public List<FeatRender> feats = new List<FeatRender>();
    }
    class FeatRender
    {
        public string   tipo;       // "Point" | "LineString" | "Polygon"
        public Vector3  punto;      // sólo si tipo == "Point"
        public Vector3[] linea;     // sólo si tipo != "Point"
    }

    // ── Colores por categoría ─────────────────────────────────────────────
    static readonly string[] _catKeys = {
        "parcela_urb","parcela_rur","parcela_mix",
        "carretera_principal","carretera","peatonal","plaza",
        "camino","sendero","pecuaria","ciclovia","ferrocarril",
        "aparcamiento","edificio","patrimonio_bic","patrimonio",
        "religioso","cementerio","hosteleria","comercio",
        "educacion","sanidad","equipamiento","deporte",
        "parque","bosque","agricola","verde","agua",
        "limite_admin","nucleo_urbano","uso_suelo",
    };
    static readonly Color[] _catValues = {
        new Color(1f,  0.80f,0f,   0.55f),  // parcela_urb
        new Color(0.55f,0.85f,0.25f,0.45f), // parcela_rur
        new Color(0.80f,0.90f,0.30f,0.45f), // parcela_mix
        new Color(1f,  0.40f,0.10f,0.90f),  // carretera_principal
        new Color(1f,  0.65f,0.20f,0.80f),  // carretera
        new Color(1f,  1f,  0.35f, 0.90f),  // peatonal
        new Color(1f,  1f,  0.65f, 0.70f),  // plaza
        new Color(0.75f,0.50f,0.20f,0.70f), // camino
        new Color(0.55f,0.35f,0.15f,0.70f), // sendero
        new Color(0.40f,0.60f,0.20f,0.70f), // pecuaria
        new Color(0.10f,0.90f,0.30f,0.80f), // ciclovia
        new Color(0.20f,0.40f,1f,   0.95f), // ferrocarril
        new Color(0.10f,0.85f,1f,   0.70f), // aparcamiento
        new Color(0.90f,0.25f,0.25f,0.55f), // edificio
        new Color(1f,  0.85f,0.10f,0.90f),  // patrimonio_bic
        new Color(1f,  0.80f,0.20f,0.80f),  // patrimonio
        new Color(0.95f,0.85f,1f,   0.80f), // religioso
        new Color(0.55f,0.55f,0.55f,0.70f), // cementerio
        new Color(1f,  0.50f,0.75f,0.75f),  // hosteleria
        new Color(0.80f,0.20f,0.80f,0.70f), // comercio
        new Color(0.10f,0.90f,0.90f,0.70f), // educacion
        new Color(0.95f,0.10f,0.15f,0.80f), // sanidad
        new Color(0.50f,0.50f,1f,   0.70f), // equipamiento
        new Color(0.10f,0.70f,0.30f,0.70f), // deporte
        new Color(0.20f,0.80f,0.20f,0.60f), // parque
        new Color(0.10f,0.50f,0.10f,0.60f), // bosque
        new Color(0.80f,0.90f,0.40f,0.50f), // agricola
        new Color(0.40f,0.90f,0.40f,0.50f), // verde
        new Color(0.20f,0.60f,1f,   0.70f), // agua
        new Color(1f,  0.10f,0.50f,0.60f),  // limite_admin
        new Color(1f,  0.70f,0.70f,0.50f),  // nucleo_urbano
        new Color(0.70f,0.70f,0.70f,0.50f), // uso_suelo
    };

    static Color ColorParaCat(string cat)
    {
        if (cat == null) cat = "";
        for (int i = 0; i < _catKeys.Length; i++)
            if (_catKeys[i] == cat) return _catValues[i];
        
        // fallback: color por hash
        int hash = cat.GetHashCode() & 0xFF;
        Color fc = Color.HSVToRGB(hash / 255f, 0.65f, 0.85f);
        fc.a = 0.65f;
        return fc;
    }

    static Color ColorDefault(string filename)
    {
        string fn = (filename ?? "").ToLower();
        if (fn.Contains("parcel")) return ColorParaCat("parcela_urb");
        if (fn.Contains("calle") || fn.Contains("ctra")) return ColorParaCat("carretera");
        if (fn.Contains("peat"))  return ColorParaCat("peatonal");
        if (fn.Contains("ferr"))  return ColorParaCat("ferrocarril");
        if (fn.Contains("edif"))  return ColorParaCat("edificio");
        if (fn.Contains("agua") || fn.Contains("rio")) return ColorParaCat("agua");
        if (fn.Contains("parque") || fn.Contains("verde")) return ColorParaCat("parque");
        return new Color(0.7f, 0.7f, 0.7f, 0.6f);
    }

    // ── Conversión UTM30N → Unity via fuente canónica ────────────────────
    const float Y_DEF = 22f;   // altura Unity aproximada de Alsasua (531.94 - 511.33 + 1.5)
    static Vector2 UTMaUnity(float e, float n) => GeoDataAlsasua.UTMaUnity(e, n);

    // ── Estado de la ventana ──────────────────────────────────────────────
    readonly List<Capa> _capas = new List<Capa>();
    Vector2 _scroll;
    float _grosor   = 1.8f;
    float _radioFar = 6000f;
    bool  _proyectar = false;
    bool  _puntos    = true;
    bool  _lineas    = true;
    bool  _poligs    = true;

    [MenuItem("Tools/Alsasua/🗺 Visor Capas GIS", priority = -19)]
    static void Abrir()
    {
        var w = GetWindow<VisorCapasGIS>("🗺 Visor GIS");
        w.minSize = new Vector2(320f, 400f);
    }

    void OnEnable()
    {
        SceneView.duringSceneGui += DibujarScene;
        Escanear();
    }
    void OnDisable() => SceneView.duringSceneGui -= DibujarScene;

    // ── GUI ───────────────────────────────────────────────────────────────
    void OnGUI()
    {
        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("🔍 Escanear capas", GUILayout.Height(26))) Escanear();
        if (GUILayout.Button("♻ Recargar todo",   GUILayout.Height(26)))
        {
            foreach (var c in _capas) { c.cargada = false; c.feats.Clear(); }
            SceneView.RepaintAll();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        
        // ── Controles de Visualización ──
        EditorGUILayout.BeginHorizontal();
        _puntos = GUILayout.Toggle(_puntos, "📍 Puntos", "Button");
        _lineas = GUILayout.Toggle(_lineas, "〰 Líneas", "Button");
        _poligs = GUILayout.Toggle(_poligs, "🟩 Polígonos", "Button");
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        
        // ── Ajustes de Renderizado ──
        EditorGUI.BeginChangeCheck();
        _proyectar = EditorGUILayout.ToggleLeft("Proyectar sobre el terreno 3D (más lento)", _proyectar);
        if (EditorGUI.EndChangeCheck())
        {
            // Si cambiamos la proyección, forzamos la recarga para recalcular alturas
            foreach (var c in _capas) c.cargada = false; 
            SceneView.RepaintAll();
        }

        _grosor = EditorGUILayout.Slider("Grosor de líneas", _grosor, 1f, 10f);
        _radioFar = EditorGUILayout.Slider("Distancia de dibujado", _radioFar, 100f, 15000f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Capas Disponibles", EditorStyles.boldLabel);

        // ── Lista de Capas ──
        _scroll = EditorGUILayout.BeginScrollView(_scroll, "box");
        foreach (var c in _capas)
        {
            EditorGUILayout.BeginHorizontal();
            
            // Checkbox para activar/desactivar
            c.activa = EditorGUILayout.Toggle(c.activa, GUILayout.Width(20));
            
            // Muestra de color
            GUI.color = new Color(c.color.r, c.color.g, c.color.b, 1f); 
            GUILayout.Label("■", EditorStyles.boldLabel, GUILayout.Width(20));
            GUI.color = Color.white;
            
            // Nombre y fuente
            GUILayout.Label(c.titulo, GUILayout.Width(180));
            GUILayout.Label($"({c.source})", EditorStyles.miniLabel);
            
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        if (GUI.changed) SceneView.RepaintAll();
    }

    // ── Lógica de Archivos y Carga ────────────────────────────────────────
    void Escanear()
    {
        _capas.Clear();
        string[] archivos = Directory.GetFiles(Application.dataPath, "*.json", SearchOption.AllDirectories);
        
        foreach (string ruta in archivos)
        {
            // Evitar procesar JSONs que no sean del sistema GIS
            if (!ruta.Contains("GIS") && !ruta.Contains("Alsasua")) continue;

            string nombreArchivo = Path.GetFileNameWithoutExtension(ruta);
            _capas.Add(new Capa
            {
                archivo = ruta,
                titulo = nombreArchivo,
                color = ColorDefault(nombreArchivo),
                activa = true,
                cargada = false
            });
        }
    }

    void CargarCapa(Capa c)
    {
        if (c.cargada) return;
        c.feats.Clear();

        try
        {
            string json = File.ReadAllText(c.archivo);
            var data = JsonUtility.FromJson<CapaJson>(json);

            if (data != null && data.features != null)
            {
                c.source = !string.IsNullOrEmpty(data.source) ? data.source : "Local";
                c.titulo = !string.IsNullOrEmpty(data.title) ? data.title : c.titulo;

                foreach (var feat in data.features)
                {
                    if (feat.coords == null || feat.coords.Length == 0) continue;

                    FeatRender fr = new FeatRender { tipo = feat.geom };

                    if (fr.tipo == "Point")
                    {
                        Vector2 u = UTMaUnity(feat.coords[0], feat.coords[1]);
                        float y = _proyectar ? GeoDataAlsasua.AlturaTerreno(u.x, u.y) : Y_DEF;
                        fr.punto = new Vector3(u.x, y, u.y);
                    }
                    else // LineString o Polygon
                    {
                        int numPuntos = feat.coords.Length / 2;
                        fr.linea = new Vector3[numPuntos];
                        for (int i = 0; i < numPuntos; i++)
                        {
                            Vector2 u = UTMaUnity(feat.coords[i * 2], feat.coords[i * 2 + 1]);
                            float y = _proyectar ? GeoDataAlsasua.AlturaTerreno(u.x, u.y) : Y_DEF;
                            fr.linea[i] = new Vector3(u.x, y, u.y);
                        }
                    }
                    c.feats.Add(fr);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Visor GIS] Error al cargar la capa {c.titulo}: {e.Message}");
        }
        
        c.cargada = true;
    }

    // ── Renderizado en Scene View ─────────────────────────────────────────
    void DibujarScene(SceneView sceneView)
    {
        if (Event.current.type != EventType.Repaint) return;

        Vector3 posCamara = sceneView.camera.transform.position;

        foreach (var c in _capas)
        {
            if (!c.activa) continue;
            if (!c.cargada) CargarCapa(c); // Carga diferida (Lazy loading)

            Handles.color = c.color;

            foreach (var f in c.feats)
            {
                // Renderizar Puntos
                if (f.tipo == "Point" && _puntos)
                {
                    if (Vector3.Distance(posCamara, f.punto) < _radioFar)
                    {
                        Handles.DrawSolidDisc(f.punto, Vector3.up, _grosor);
                    }
                }
                // Renderizar Líneas o Polígonos
                else if ((f.tipo == "LineString" && _lineas) || (f.tipo == "Polygon" && _poligs))
                {
                    if (f.linea != null && f.linea.Length > 1)
                    {
                        if (Vector3.Distance(posCamara, f.linea[0]) < _radioFar)
                        {
                            Handles.DrawAAPolyLine(_grosor, f.linea);
                        }
                    }
                }
            }
        }
    }
}
#endif