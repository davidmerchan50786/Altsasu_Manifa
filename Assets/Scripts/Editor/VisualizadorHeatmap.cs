// Assets/Scripts/Editor/VisualizadorHeatmap.cs
// ═══════════════════════════════════════════════════════════════════════════
//  VISUALIZADOR HEATMAP — auditoría de rendimiento en vivo (capa EDITOR)
//
//  Tres capas de telemetría sobre el SceneView, sin tocar el build de release:
//    1. CALOR DE RENDER  — rejilla XZ; cada celda acumula el nº de submalla·material
//       de los renderers VISIBLES que caen en ella (proxy espacial de draw calls).
//       Se pinta con Graphics.DrawMeshInstanced (1 draw / 1023 celdas) → coste nulo.
//    2. COSTURAS LATTICE — lee Telemetria.Costuras (las llena CargadorMosaicoTerreno):
//       cuánto tardó en "coser" (SetHeights del lattice) cada tile del mosaico V2.
//    3. TILES/CHUNKS      — colorea los tiles del mosaico por anillo y estado
//       (activo / descargado), leyendo MarcadorTerrenoAltsasua.
//
//  Diseño anti-impacto:
//    · El muestreo es POR INTERVALO (no por frame) y solo con la ventana abierta.
//    · Todo el dibujo vive en SceneView.duringSceneGui (editor-only) → 0 coste en
//      Play build. Es el equivalente "puro editor" a OnDrawGizmos, sin necesitar un
//      MonoBehaviour en la capa Runtime (que rompería el asmdef Core←Runtime).
//    · GPU (DrawMeshInstanced) por defecto; fallback Handles para HDRP si el SRP
//      ignora el pase unlit del shrader instanciado.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class VisualizadorHeatmap : EditorWindow
{
    // ── Preferencias persistentes ────────────────────────────────────────────
    const string P_ON      = "Alsasua.Heatmap.On";
    const string P_INTERV  = "Alsasua.Heatmap.Intervalo";
    const string P_CELDA   = "Alsasua.Heatmap.Celda";
    const string P_ALPHA   = "Alsasua.Heatmap.Alpha";
    const string P_GPU     = "Alsasua.Heatmap.GPU";
    const string P_VIS     = "Alsasua.Heatmap.SoloVisibles";
    const string P_EDIT    = "Alsasua.Heatmap.EnEdicion";
    const string P_LCALOR  = "Alsasua.Heatmap.Calor";
    const string P_LCOST   = "Alsasua.Heatmap.Costuras";
    const string P_LTILES  = "Alsasua.Heatmap.Tiles";

    bool  _on            = true;
    float _intervalo     = 0.75f;   // s entre muestreos
    float _tamCelda      = 24f;     // m de lado de cada celda
    float _alpha         = 0.55f;
    bool  _gpu           = true;
    bool  _soloVisibles  = true;
    bool  _enEdicion     = false;   // muestrear también fuera de Play
    bool  _capaCalor     = true;
    bool  _capaCosturas  = true;
    bool  _capaTiles     = true;

    // ── Estado de muestreo ────────────────────────────────────────────────────
    struct Celda { public float calor; public float y; }
    readonly Dictionary<long, Celda> _grid = new(2048);
    float _calorMax;
    int   _numRenderers;
    double _ultimoMuestreo;

    // ── Recursos GPU ──────────────────────────────────────────────────────────
    Mesh             _quad;
    Material         _mat;
    MaterialPropertyBlock _mpb;
    const int LOTE = 1023;
    readonly Matrix4x4[] _matrices = new Matrix4x4[LOTE];
    readonly Vector4[]   _colores  = new Vector4[LOTE];

    [MenuItem("Tools/Alsasua/📊 Heatmap de Rendimiento", priority = 30)]
    public static void Abrir()
    {
        var w = GetWindow<VisualizadorHeatmap>();
        w.titleContent = new GUIContent("Heatmap", EditorGUIUtility.IconContent("d_Profiler.GPU").image);
        w.minSize = new Vector2(300, 360);
    }

    void OnEnable()
    {
        _on           = EditorPrefs.GetBool(P_ON, true);
        _intervalo    = EditorPrefs.GetFloat(P_INTERV, 0.75f);
        _tamCelda     = EditorPrefs.GetFloat(P_CELDA, 24f);
        _alpha        = EditorPrefs.GetFloat(P_ALPHA, 0.55f);
        _gpu          = EditorPrefs.GetBool(P_GPU, true);
        _soloVisibles = EditorPrefs.GetBool(P_VIS, true);
        _enEdicion    = EditorPrefs.GetBool(P_EDIT, false);
        _capaCalor    = EditorPrefs.GetBool(P_LCALOR, true);
        _capaCosturas = EditorPrefs.GetBool(P_LCOST, true);
        _capaTiles    = EditorPrefs.GetBool(P_LTILES, true);

        CrearRecursos();
        SceneView.duringSceneGui += AlPintarEscena;
        EditorApplication.update += Tick;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= AlPintarEscena;
        EditorApplication.update -= Tick;
        DestruirRecursos();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  MUESTREO (por intervalo, no por frame)
    // ═════════════════════════════════════════════════════════════════════════
    void Tick()
    {
        if (!_on || !_capaCalor) return;
        if (!Application.isPlaying && !_enEdicion) return;
        // EditorApplication.timeSinceStartup es válido en editor (no es el Date.now prohibido en runtime).
        if (EditorApplication.timeSinceStartup - _ultimoMuestreo < _intervalo) return;
        _ultimoMuestreo = EditorApplication.timeSinceStartup;

        Muestrear();
        SceneView.RepaintAll();
        Repaint();
    }

    void Muestrear()
    {
        _grid.Clear();
        _calorMax = 0f;

        // Solo objetos activos; FindObjectsByType excluye inactivos por defecto.
        var rends = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        _numRenderers = rends.Length;

        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i];
            if (r == null || !r.enabled) continue;
            if (_soloVisibles && !r.isVisible) continue;        // lo que realmente alimenta draw calls
            if (r is ParticleSystemRenderer) continue;          // ruido; opcional

            Vector3 c = r.bounds.center;
            int cx = Mathf.FloorToInt(c.x / _tamCelda);
            int cz = Mathf.FloorToInt(c.z / _tamCelda);
            long k = ((long)cx << 32) ^ (uint)cz;

            // peso ≈ submallas·materiales = nº aprox. de draws que aporta el renderer
            float peso = Mathf.Max(1, r.sharedMaterials != null ? r.sharedMaterials.Length : 1);

            _grid.TryGetValue(k, out var cel);
            cel.calor += peso;
            cel.y = cel.y == 0f ? c.y : Mathf.Max(cel.y, c.y);
            _grid[k] = cel;
            if (cel.calor > _calorMax) _calorMax = cel.calor;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  DIBUJO EN SCENE VIEW (editor-only)
    // ═════════════════════════════════════════════════════════════════════════
    void AlPintarEscena(SceneView sv)
    {
        if (!_on) return;
        if (_capaCalor)    PintarCalor(sv);
        if (_capaCosturas) PintarCosturas();
        if (_capaTiles)    PintarTiles();
    }

    void PintarCalor(SceneView sv)
    {
        if (_grid.Count == 0 || _calorMax <= 0f) return;
        bool gpuOk = _gpu && _mat != null && _quad != null;

        if (gpuOk) _mat.SetFloat("_Alpha", _alpha);
        int n = 0;
        foreach (var kv in _grid)
        {
            int cx = (int)(kv.Key >> 32);
            int cz = (int)(kv.Key & 0xffffffff);
            var cel = kv.Value;
            float t = Mathf.Clamp01(cel.calor / _calorMax);
            Vector3 pos = new Vector3((cx + 0.5f) * _tamCelda, cel.y + 1.5f, (cz + 0.5f) * _tamCelda);

            if (gpuOk)
            {
                _matrices[n] = Matrix4x4.TRS(pos, Quaternion.identity,
                    new Vector3(_tamCelda * 0.92f, 1f, _tamCelda * 0.92f));
                Color col = ColorCalor(t); col.a = 1f;
                _colores[n] = col;
                if (++n == LOTE) { FlushGPU(n, sv.camera); n = 0; }
            }
            else
            {
                PintarCeldaHandles(pos, _tamCelda * 0.92f, ColorCalor(t));
            }
        }
        if (gpuOk && n > 0) FlushGPU(n, sv.camera);
    }

    void FlushGPU(int n, Camera cam)
    {
        _mpb.SetVectorArray("_Color", _colores);
        Graphics.DrawMeshInstanced(_quad, 0, _mat, _matrices, n, _mpb,
            ShadowCastingMode.Off, false, 0, cam, LightProbeUsage.Off);
    }

    void PintarCeldaHandles(Vector3 c, float lado, Color col)
    {
        float h = lado * 0.5f;
        var v = new[]
        {
            c + new Vector3(-h, 0, -h), c + new Vector3(-h, 0, h),
            c + new Vector3( h, 0,  h), c + new Vector3( h, 0, -h)
        };
        Color cara = col; cara.a = _alpha;
        Handles.DrawSolidRectangleWithOutline(v, cara, Color.clear);
    }

    void PintarCosturas()
    {
        var costuras = Telemetria.Costuras;
        if (costuras.Count == 0) return;
        float peor = Mathf.Max(0.001f, Telemetria.PeorCosturaMs);

        var estilo = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.white } };
        foreach (var co in costuras)
        {
            float t = Mathf.Clamp01(co.ms / peor);
            float h = co.lado * 0.5f;
            var v = new[]
            {
                co.centro + new Vector3(-h, 0, -h), co.centro + new Vector3(-h, 0, h),
                co.centro + new Vector3( h, 0,  h), co.centro + new Vector3( h, 0, -h)
            };
            Color cara = ColorCalor(t); cara.a = 0.10f + 0.30f * t;
            Color borde = ColorCalor(t); borde.a = 0.9f;
            Handles.DrawSolidRectangleWithOutline(v, cara, borde);
            Handles.Label(co.centro, $"{co.ms:F1} ms", estilo);
        }
    }

    void PintarTiles()
    {
        var marcas = Object.FindObjectsByType<MarcadorTerrenoAltsasua>(FindObjectsSortMode.None);
        foreach (var m in marcas)
        {
            if (m == null || m.fuente != FuenteTerreno.Mosaico) continue;
            var terr = m.GetComponent<Terrain>();
            if (terr == null || terr.terrainData == null) continue;

            bool activo = m.gameObject.activeInHierarchy && terr.enabled && terr.drawHeightmap;
            Vector3 p = terr.transform.position;
            Vector3 s = terr.terrainData.size;
            float y = p.y + s.y * 0.0f + 2f;
            var v = new[]
            {
                new Vector3(p.x,       y, p.z),
                new Vector3(p.x,       y, p.z + s.z),
                new Vector3(p.x + s.x, y, p.z + s.z),
                new Vector3(p.x + s.x, y, p.z)
            };
            Color baseCol = ColorAnillo(m.anillo);
            Color cara = baseCol; cara.a = activo ? 0.07f : 0.015f;
            Color borde = baseCol; borde.a = activo ? 0.85f : 0.20f;
            Handles.DrawSolidRectangleWithOutline(v, cara, borde);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  PANEL DE LA VENTANA
    // ═════════════════════════════════════════════════════════════════════════
    void OnGUI()
    {
        EditorGUI.BeginChangeCheck();

        _on = EditorGUILayout.ToggleLeft("◉ Telemetría activa", _on, EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Muestreo", EditorStyles.miniBoldLabel);
            _intervalo = EditorGUILayout.Slider("Intervalo (s)", _intervalo, 0.1f, 3f);
            _tamCelda  = EditorGUILayout.Slider("Lado celda (m)", _tamCelda, 8f, 200f);
            _soloVisibles = EditorGUILayout.ToggleLeft("Solo renderers visibles", _soloVisibles);
            _enEdicion    = EditorGUILayout.ToggleLeft("Muestrear fuera de Play", _enEdicion);
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Capas", EditorStyles.miniBoldLabel);
            _capaCalor    = EditorGUILayout.ToggleLeft("Calor de render (rejilla)", _capaCalor);
            _capaCosturas = EditorGUILayout.ToggleLeft("Costuras del lattice (mosaico)", _capaCosturas);
            _capaTiles    = EditorGUILayout.ToggleLeft("Tiles/chunks activos", _capaTiles);
            _alpha = EditorGUILayout.Slider("Opacidad", _alpha, 0.1f, 1f);
            _gpu   = EditorGUILayout.ToggleLeft("GPU (DrawMeshInstanced · off = Handles, fallback HDRP)", _gpu);
        }

        if (EditorGUI.EndChangeCheck()) { GuardarPrefs(); SceneView.RepaintAll(); }

        DibujarEstadisticas();
        DibujarLeyenda();
    }

    void DibujarEstadisticas()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Estado en vivo", EditorStyles.miniBoldLabel);
            if (Application.isPlaying)
            {
                Fila("Draw calls",   UnityStats.drawCalls.ToString());
                Fila("Batches",      UnityStats.batches.ToString());
                Fila("SetPass calls",UnityStats.setPassCalls.ToString());
                Fila("Triángulos",   $"{UnityStats.triangles / 1000f:N0} k");
                Fila("Vértices",     $"{UnityStats.vertices / 1000f:N0} k");
            }
            else
            {
                EditorGUILayout.HelpBox("Las cifras globales de draw calls solo son válidas en Play Mode.",
                    MessageType.None);
            }
            Fila("Renderers muestreados", _numRenderers.ToString());
            Fila("Celdas calientes", _grid.Count.ToString());
            Fila("Pico de celda (≈draws)", _calorMax.ToString("F0"));

            EditorGUILayout.Space(2);
            int nCost = Telemetria.Costuras.Count;
            Fila("Tiles cosidos", nCost.ToString());
            Fila("Peor costura", $"{Telemetria.PeorCosturaMs:F1} ms");
            if (GUILayout.Button("Limpiar costuras")) { Telemetria.Limpiar(); SceneView.RepaintAll(); Repaint(); }
        }
    }

    static void Fila(string k, string v)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(k, GUILayout.Width(170));
            EditorGUILayout.LabelField(v, EditorStyles.boldLabel);
        }
    }

    void DibujarLeyenda()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Leyenda calor", EditorStyles.miniBoldLabel);
            var r = GUILayoutUtility.GetRect(0, 16, GUILayout.ExpandWidth(true));
            int pasos = Mathf.Max(8, (int)r.width / 6);
            for (int i = 0; i < pasos; i++)
            {
                float t = i / (float)(pasos - 1);
                var seg = new Rect(r.x + r.width * t, r.y, r.width / pasos + 1, r.height);
                EditorGUI.DrawRect(seg, ColorCalor(t));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("frío", GUILayout.Width(40));
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("pico", GUILayout.Width(40));
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  UTILIDADES
    // ═════════════════════════════════════════════════════════════════════════
    static Color ColorCalor(float t)   // azul → cian → verde → amarillo → rojo
    {
        t = Mathf.Clamp01(t);
        if (t < 0.25f) return Color.Lerp(new Color(0.10f, 0.20f, 0.90f), new Color(0f, 0.85f, 0.95f), t / 0.25f);
        if (t < 0.50f) return Color.Lerp(new Color(0f, 0.85f, 0.95f), new Color(0.20f, 0.90f, 0.20f), (t - 0.25f) / 0.25f);
        if (t < 0.75f) return Color.Lerp(new Color(0.20f, 0.90f, 0.20f), new Color(0.95f, 0.90f, 0.10f), (t - 0.50f) / 0.25f);
        return Color.Lerp(new Color(0.95f, 0.90f, 0.10f), new Color(0.95f, 0.10f, 0.10f), (t - 0.75f) / 0.25f);
    }

    static Color ColorAnillo(int anillo) => anillo switch
    {
        0 => new Color(0.30f, 0.95f, 0.45f),   // urbano
        1 => new Color(0.30f, 0.80f, 0.95f),   // valle
        2 => new Color(0.55f, 0.50f, 0.95f),   // sierras
        _ => Color.gray
    };

    void CrearRecursos()
    {
        _mpb ??= new MaterialPropertyBlock();
        if (_quad == null)
        {
            _quad = new Mesh { name = "HeatmapQuad", hideFlags = HideFlags.HideAndDontSave };
            _quad.vertices = new[]
            {
                new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f),
                new Vector3( 0.5f, 0,  0.5f), new Vector3(-0.5f, 0, 0.5f)
            };
            _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };   // normal +Y (vista cenital)
            _quad.RecalculateNormals();
            _quad.RecalculateBounds();
        }
        if (_mat == null)
        {
            var sh = Shader.Find("Hidden/Alsasua/HeatmapInstanced");
            if (sh != null)
            {
                _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                _mat.enableInstancing = true;
            }
        }
    }

    void DestruirRecursos()
    {
        if (_quad != null) DestroyImmediate(_quad);
        if (_mat  != null) DestroyImmediate(_mat);
        _quad = null; _mat = null;
    }

    void GuardarPrefs()
    {
        EditorPrefs.SetBool(P_ON, _on);
        EditorPrefs.SetFloat(P_INTERV, _intervalo);
        EditorPrefs.SetFloat(P_CELDA, _tamCelda);
        EditorPrefs.SetFloat(P_ALPHA, _alpha);
        EditorPrefs.SetBool(P_GPU, _gpu);
        EditorPrefs.SetBool(P_VIS, _soloVisibles);
        EditorPrefs.SetBool(P_EDIT, _enEdicion);
        EditorPrefs.SetBool(P_LCALOR, _capaCalor);
        EditorPrefs.SetBool(P_LCOST, _capaCosturas);
        EditorPrefs.SetBool(P_LTILES, _capaTiles);
    }
}
