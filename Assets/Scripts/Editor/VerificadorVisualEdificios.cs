// Assets/Scripts/Editor/VerificadorVisualEdificios.cs
// ═══════════════════════════════════════════════════════════════════════════
//  VERIFICADOR VISUAL DE EDIFICIOS — B7
//
//  Compara renders Unity con fotos reales de fachadas (Mapillary/StreetView)
//  para detectar errores en la reconstrucción 3D de Alsasua.
//
//  Menú: Altsasua → Verificar Edificios Visualmente
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

namespace AltsasuEditor
{
    public class VerificadorVisualEdificios : EditorWindow
    {
        // ── Campos ────────────────────────────────────────────────────────────
        string  _pathMapping  = "Assets/AlsasuaData/photo_building_mapping.json";
        string  _pathCaptures = "Assets/AlsasuaData/VerificationCaptures/";
        int     _renderW      = 1024;
        int     _renderH      = 768;
        float   _camDist      = 20f;
        bool    _autoSelect   = true;
        Vector2 _scroll;
        List<VerifResult> _results = new List<VerifResult>();

        // ── Clase interna ─────────────────────────────────────────────────────
        [Serializable]
        class VerifResult
        {
            public string    id, zona, estado; // OK / REVISAR / CRITICO / SIN_MODELO / SIN_FOTO
            public float     ssim, edgeSim;
            public string    renderPath, fotoPath;
            public Texture2D renderThumb, fotoThumb; // 64×64 para GUI
        }

        // ── JSON helpers ──────────────────────────────────────────────────────
        [Serializable] class PhotoMapping   { public PhotoEntry[] mappings; }
        [Serializable] class PhotoEntry     { public string foto_procesada, edificio_id, zona, confidence; }
        [Serializable] class VerifReportOut { public VerifResultOut[] edificios; public Summary summary; }
        [Serializable] class VerifResultOut { public string id, zona, estado; public float ssim, edge_sim; public string render_path, foto_path; }
        [Serializable] class Summary        { public int ok, revisar, critico; public float ssim_medio; }

        // ── Apertura de ventana ───────────────────────────────────────────────
        [MenuItem("Altsasua/Verificar Edificios Visualmente")]
        static void Open()
        {
            var w = GetWindow<VerificadorVisualEdificios>("Verificador Visual");
            w.minSize = new Vector2(700, 500);
            w.Show();
        }

        // ── OnGUI ─────────────────────────────────────────────────────────────
        void OnGUI()
        {
            EditorGUILayout.Space(6);
            GUILayout.Label("VERIFICADOR VISUAL DE EDIFICIOS", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // Parámetros
            _pathMapping  = EditorGUILayout.TextField("Mapping JSON",       _pathMapping);
            _pathCaptures = EditorGUILayout.TextField("Carpeta capturas",   _pathCaptures);
            EditorGUILayout.BeginHorizontal();
            _renderW   = EditorGUILayout.IntField("Ancho render", _renderW);
            _renderH   = EditorGUILayout.IntField("Alto render",  _renderH);
            EditorGUILayout.EndHorizontal();
            _camDist    = EditorGUILayout.FloatField("Distancia cámara", _camDist);
            _autoSelect = EditorGUILayout.Toggle("Seleccionar CRITICO", _autoSelect);

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("GENERAR REPORTE", GUILayout.Height(32)))
                GenerarReporte();
            if (GUILayout.Button("Limpiar", GUILayout.Height(32), GUILayout.Width(80)))
            {
                _results.Clear();
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);

            // Estado vacío
            if (_results.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Sin resultados.\n" +
                    "Sube fotos a Assets/AlsasuaData/FacadeTextures/StreetView/ y " +
                    "ejecuta B3 para generar photo_building_mapping.json.\n" +
                    "Después pulsa GENERAR REPORTE.",
                    MessageType.Info);
                return;
            }

            // Tabla de resultados
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var r in _results)
                DrawResultRow(r);
            EditorGUILayout.EndScrollView();

            // Footer
            int ok      = _results.FindAll(r => r.estado == "OK").Count;
            int revisar = _results.FindAll(r => r.estado == "REVISAR").Count;
            int critico = _results.FindAll(r => r.estado == "CRITICO").Count;
            float ssimMedio = 0f;
            int validos = 0;
            foreach (var r in _results)
                if (r.ssim > 0) { ssimMedio += r.ssim; validos++; }
            if (validos > 0) ssimMedio /= validos;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(
                $"OK:{ok}  REVISAR:{revisar}  CRITICO:{critico}  |  SSIM medio: {ssimMedio:F2}",
                EditorStyles.boldLabel);
        }

        void DrawResultRow(VerifResult r)
        {
            EditorGUILayout.BeginHorizontal("box");

            // Thumbnail render
            if (r.renderThumb != null)
                GUILayout.Label(r.renderThumb, GUILayout.Width(64), GUILayout.Height(64));
            else
                GUILayout.Box("—", GUILayout.Width(64), GUILayout.Height(64));

            // Thumbnail foto
            if (r.fotoThumb != null)
                GUILayout.Label(r.fotoThumb, GUILayout.Width(64), GUILayout.Height(64));
            else
                GUILayout.Box("—", GUILayout.Width(64), GUILayout.Height(64));

            EditorGUILayout.BeginVertical();

            // ID y zona
            EditorGUILayout.LabelField($"{r.id}  [{r.zona}]", EditorStyles.boldLabel);

            // Barra SSIM coloreada
            Rect barRect = EditorGUILayout.GetControlRect(GUILayout.Height(14));
            float fill = Mathf.Clamp01(r.ssim);
            Color barCol = r.ssim > 0.75f ? new Color(0.2f, 0.8f, 0.2f)
                         : r.ssim > 0.5f  ? new Color(0.9f, 0.7f, 0.1f)
                                           : new Color(0.9f, 0.2f, 0.2f);
            EditorGUI.DrawRect(barRect, new Color(0.2f, 0.2f, 0.2f));
            EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, barRect.width * fill, barRect.height), barCol);
            EditorGUI.LabelField(barRect, $" SSIM:{r.ssim:F3}  Edge:{r.edgeSim:F3}", EditorStyles.miniLabel);

            // Badge estado
            GUIStyle badge = new GUIStyle(EditorStyles.miniLabel);
            badge.normal.textColor = r.estado == "OK"      ? Color.green
                                   : r.estado == "REVISAR" ? Color.yellow
                                   : r.estado == "CRITICO" ? Color.red
                                                            : Color.gray;
            EditorGUILayout.LabelField($"Estado: {r.estado}", badge);

            EditorGUILayout.EndVertical();

            // Botón "Ir"
            if (GUILayout.Button("Ir", GUILayout.Width(36), GUILayout.Height(36)))
            {
                var go = FindEdificio(r.id);
                if (go != null)
                {
                    Selection.activeGameObject = go;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        // ── GenerarReporte ────────────────────────────────────────────────────
        void GenerarReporte()
        {
            _results.Clear();

            // Leer mapping
            string mappingFull = Path.Combine(Application.dataPath, "..", _pathMapping)
                                     .Replace('\\', '/');
            if (!File.Exists(mappingFull))
            {
                Debug.LogWarning($"[VerificadorVisual] No existe: {mappingFull}");
                EditorUtility.DisplayDialog("Sin mapping",
                    $"No se encontró {_pathMapping}.\n" +
                    "Ejecuta B3 primero para generar el mapping de fotos.", "OK");
                return;
            }

            string json = File.ReadAllText(mappingFull);
            PhotoMapping pm = JsonUtility.FromJson<PhotoMapping>(json);
            if (pm == null || pm.mappings == null || pm.mappings.Length == 0)
            {
                Debug.LogWarning("[VerificadorVisual] Mapping vacío.");
                EditorUtility.DisplayDialog("Mapping vacío",
                    "Sube fotos a Assets/AlsasuaData/FacadeTextures/StreetView/ " +
                    "y ejecuta B3.", "OK");
                return;
            }

            // Asegurar directorio de capturas
            string capturesAbs = Path.Combine(Application.dataPath, "..",
                                     _pathCaptures.TrimEnd('/')).Replace('\\', '/');
            Directory.CreateDirectory(capturesAbs);

            var criticalGOs = new List<UnityEngine.Object>();
            float ssimAcum  = 0f;
            int   validos   = 0;

            int total = pm.mappings.Length;
            for (int i = 0; i < total; i++)
            {
                PhotoEntry m = pm.mappings[i];
                EditorUtility.DisplayProgressBar("Verificando edificios",
                    $"{m.edificio_id} ({i+1}/{total})", (float)i / total);

                var r = new VerifResult
                {
                    id        = m.edificio_id ?? "unknown",
                    zona      = m.zona        ?? "",
                    renderPath = "",
                    fotoPath   = ""
                };

                // Foto original
                string fotoAbs = "";
                if (!string.IsNullOrEmpty(m.foto_procesada))
                {
                    // Puede ser ruta relativa a Assets/ o absoluta
                    fotoAbs = m.foto_procesada.StartsWith("Assets/")
                        ? Path.Combine(Application.dataPath, "..", m.foto_procesada).Replace('\\', '/')
                        : m.foto_procesada;
                }

                // GameObject
                GameObject go = FindEdificio(m.edificio_id);

                if (go == null)
                {
                    r.estado = "SIN_MODELO";
                    r.ssim   = 0f;
                    _results.Add(r);
                    continue;
                }

                if (string.IsNullOrEmpty(fotoAbs) || !File.Exists(fotoAbs))
                {
                    r.estado = "SIN_FOTO";
                    r.ssim   = 0f;
                    _results.Add(r);
                    continue;
                }

                // Capturar render
                string renderOut = Path.Combine(capturesAbs, $"{SanitizeId(m.edificio_id)}_render.png");
                bool captured = CapturarRender(go, renderOut);
                if (!captured)
                {
                    r.estado = "ERROR_RENDER";
                    r.ssim   = 0f;
                    _results.Add(r);
                    continue;
                }

                r.renderPath = renderOut;
                r.fotoPath   = fotoAbs;

                // Cargar texturas para análisis
                Texture2D texRender = LoadTexture(renderOut);
                Texture2D texFoto   = LoadTexture(fotoAbs);

                if (texRender == null || texFoto == null)
                {
                    r.estado = "ERROR_CARGA";
                    r.ssim   = 0f;
                    _results.Add(r);
                    if (texRender != null) DestroyImmediate(texRender);
                    if (texFoto   != null) DestroyImmediate(texFoto);
                    continue;
                }

                // Métricas
                r.ssim    = SSIM(texRender, texFoto);
                r.edgeSim = EdgeSim(texRender, texFoto);

                // Estado
                r.estado = r.ssim > 0.75f ? "OK"
                         : r.ssim > 0.50f ? "REVISAR"
                                           : "CRITICO";

                // Thumbnails 64×64
                r.renderThumb = MakeThumbnail(texRender, 64, 64);
                r.fotoThumb   = MakeThumbnail(texFoto,   64, 64);

                DestroyImmediate(texRender);
                DestroyImmediate(texFoto);

                ssimAcum += r.ssim;
                validos++;

                if (r.estado == "CRITICO" && _autoSelect)
                    criticalGOs.Add(go);

                _results.Add(r);
            }

            EditorUtility.ClearProgressBar();

            // Guardar reporte JSON
            GuardarReporteJSON(capturesAbs, ssimAcum, validos);

            // Seleccionar críticos
            if (_autoSelect && criticalGOs.Count > 0)
                Selection.objects = criticalGOs.ToArray();

            // Log resumen
            int ok      = _results.FindAll(x => x.estado == "OK").Count;
            int revisar = _results.FindAll(x => x.estado == "REVISAR").Count;
            int critico = _results.FindAll(x => x.estado == "CRITICO").Count;
            float ssimMedio = validos > 0 ? ssimAcum / validos : 0f;
            Debug.Log($"[VerificadorVisual] Completado — OK:{ok} REVISAR:{revisar} " +
                      $"CRITICO:{critico} | SSIM medio:{ssimMedio:F3}");

            Repaint();
        }

        // ── CapturarRender ────────────────────────────────────────────────────
        bool CapturarRender(GameObject go, string outPath)
        {
            try
            {
                // Calcular bounds
                Renderer[] rends = go.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) return false;

                Bounds bounds = rends[0].bounds;
                foreach (var rd in rends) bounds.Encapsulate(rd.bounds);

                Vector3 center = bounds.center;

                // Cámara temporal
                var camGO = new GameObject("__VerifCam__");
                var cam   = camGO.AddComponent<Camera>();
                cam.fieldOfView  = 60f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane  = 1000f;
                cam.clearFlags    = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);

#if UNITY_HDRP
                var hdCam = camGO.AddComponent<HDAdditionalCameraData>();
                hdCam.volumeLayerMask = 1; // Default layer
#endif

                camGO.transform.position = center + new Vector3(0f, bounds.size.y * 0.4f, -_camDist);
                camGO.transform.LookAt(center);

                // RenderTexture
                var rt = new RenderTexture(_renderW, _renderH, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = 1;
                rt.Create();

                cam.targetTexture = rt;
                cam.Render();

                // Leer píxeles
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(_renderW, _renderH, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, _renderW, _renderH), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                // Guardar PNG
                byte[] png = tex.EncodeToPNG();
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllBytes(outPath, png);

                // Limpiar
                DestroyImmediate(tex);
                cam.targetTexture = null;
                rt.Release();
                DestroyImmediate(rt);
                DestroyImmediate(camGO);

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VerificadorVisual] Error capturando {go.name}: {ex.Message}");
                return false;
            }
        }

        // ── SSIM ──────────────────────────────────────────────────────────────
        float SSIM(Texture2D a, Texture2D b)
        {
            const int SIZE   = 128;
            const int BLOCK  = 8;
            const float K1   = 0.0001f;
            const float K2   = 0.0009f;

            float[] ga = ToGray(Resize(a, SIZE, SIZE));
            float[] gb = ToGray(Resize(b, SIZE, SIZE));

            int blocks = SIZE / BLOCK;
            float sum  = 0f;
            int  count = 0;

            for (int by = 0; by < blocks; by++)
            {
                for (int bx = 0; bx < blocks; bx++)
                {
                    float m1 = 0, m2 = 0;
                    int n = BLOCK * BLOCK;
                    for (int py = 0; py < BLOCK; py++)
                        for (int px = 0; px < BLOCK; px++)
                        {
                            int idx = (by * BLOCK + py) * SIZE + (bx * BLOCK + px);
                            m1 += ga[idx];
                            m2 += gb[idx];
                        }
                    m1 /= n; m2 /= n;

                    float s1 = 0, s2 = 0, s12 = 0;
                    for (int py = 0; py < BLOCK; py++)
                        for (int px = 0; px < BLOCK; px++)
                        {
                            int   idx = (by * BLOCK + py) * SIZE + (bx * BLOCK + px);
                            float d1  = ga[idx] - m1;
                            float d2  = gb[idx] - m2;
                            s1  += d1 * d1;
                            s2  += d2 * d2;
                            s12 += d1 * d2;
                        }
                    s1  /= n; s2  /= n; s12 /= n;

                    float num   = (2f * m1 * m2 + K1) * (2f * s12 + K2);
                    float denom = (m1 * m1 + m2 * m2 + K1) * (s1 + s2 + K2);
                    sum += denom > 0f ? num / denom : 0f;
                    count++;
                }
            }
            return count > 0 ? sum / count : 0f;
        }

        // ── EdgeSim (correlación de Pearson sobre mapas Sobel) ────────────────
        float EdgeSim(Texture2D a, Texture2D b)
        {
            const int SIZE = 128;
            float[] ga = ToGray(Resize(a, SIZE, SIZE));
            float[] gb = ToGray(Resize(b, SIZE, SIZE));

            float[] ea = SobelMagnitude(ga, SIZE, SIZE);
            float[] eb = SobelMagnitude(gb, SIZE, SIZE);

            return PearsonCorrelation(ea, eb);
        }

        // ── Helpers imagen ────────────────────────────────────────────────────

        /// <summary>Redimensiona una textura a w×h usando bilinear manual sobre píxeles.</summary>
        Color[] Resize(Texture2D src, int w, int h)
        {
            Color[] srcPix = src.GetPixels();
            int sw = src.width, sh = src.height;
            Color[] dst = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * sw - 0.5f;
                    float v = (y + 0.5f) / h * sh - 0.5f;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, sw - 1);
                    int x1 = Mathf.Clamp(x0 + 1, 0, sw - 1);
                    int y0 = Mathf.Clamp(Mathf.FloorToInt(v), 0, sh - 1);
                    int y1 = Mathf.Clamp(y0 + 1, 0, sh - 1);
                    float fx = u - Mathf.Floor(u);
                    float fy = v - Mathf.Floor(v);
                    Color c = Color.Lerp(
                        Color.Lerp(srcPix[y0 * sw + x0], srcPix[y0 * sw + x1], fx),
                        Color.Lerp(srcPix[y1 * sw + x0], srcPix[y1 * sw + x1], fx),
                        fy);
                    dst[y * w + x] = c;
                }
            }
            return dst;
        }

        float[] ToGray(Color[] px)
        {
            float[] g = new float[px.Length];
            for (int i = 0; i < px.Length; i++)
                g[i] = 0.299f * px[i].r + 0.587f * px[i].g + 0.114f * px[i].b;
            return g;
        }

        float[] SobelMagnitude(float[] gray, int w, int h)
        {
            float[] mag = new float[w * h];
            float   max = 0f;
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    float tl = gray[(y-1)*w+(x-1)], tc = gray[(y-1)*w+x], tr = gray[(y-1)*w+(x+1)];
                    float ml = gray[  y  *w+(x-1)],                        mr = gray[  y  *w+(x+1)];
                    float bl = gray[(y+1)*w+(x-1)], bc = gray[(y+1)*w+x], br = gray[(y+1)*w+(x+1)];

                    float gx = -tl - 2*ml - bl + tr + 2*mr + br;
                    float gy = -tl - 2*tc - tr + bl + 2*bc + br;
                    float m  = Mathf.Sqrt(gx*gx + gy*gy);
                    mag[y*w+x] = m;
                    if (m > max) max = m;
                }
            }
            if (max > 0f)
                for (int i = 0; i < mag.Length; i++) mag[i] /= max;
            return mag;
        }

        float PearsonCorrelation(float[] x, float[] y)
        {
            int   n   = x.Length;
            float mx  = 0f, my = 0f;
            for (int i = 0; i < n; i++) { mx += x[i]; my += y[i]; }
            mx /= n; my /= n;

            float num = 0f, dx2 = 0f, dy2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float dx = x[i] - mx, dy = y[i] - my;
                num += dx * dy;
                dx2 += dx * dx;
                dy2 += dy * dy;
            }
            float denom = Mathf.Sqrt(dx2 * dy2);
            return denom > 0f ? num / denom : 0f;
        }

        Texture2D MakeThumbnail(Texture2D src, int w, int h)
        {
            Color[] px  = Resize(src, w, h);
            var     tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // ── Utilidades ────────────────────────────────────────────────────────

        static Texture2D LoadTexture(string path)
        {
            if (!File.Exists(path)) return null;
            byte[]   data = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(data))
            {
                DestroyImmediate(tex);
                return null;
            }
            return tex;
        }

        static GameObject FindEdificio(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var go = GameObject.Find(id);
            if (go != null) return go;

            // Buscar en hijo de "Edificios_AAA"
            var root = GameObject.Find("Edificios_AAA");
            if (root == null) return null;
            var t = root.transform.Find(id);
            if (t != null) return t.gameObject;

            // Búsqueda recursiva por nombre
            return FindInChildren(root.transform, id);
        }

        static GameObject FindInChildren(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform c = parent.GetChild(i);
                if (c.name == name) return c.gameObject;
                var found = FindInChildren(c, name);
                if (found != null) return found;
            }
            return null;
        }

        static string SanitizeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "unknown";
            char[] inv = Path.GetInvalidFileNameChars();
            foreach (char c in inv) id = id.Replace(c, '_');
            return id;
        }

        void GuardarReporteJSON(string capturesAbs, float ssimAcum, int validos)
        {
            var outList = new List<VerifResultOut>();
            foreach (var r in _results)
                outList.Add(new VerifResultOut
                {
                    id         = r.id,
                    zona       = r.zona,
                    estado     = r.estado,
                    ssim       = r.ssim,
                    edge_sim   = r.edgeSim,
                    render_path = r.renderPath,
                    foto_path   = r.fotoPath
                });

            var report = new VerifReportOut
            {
                edificios = outList.ToArray(),
                summary   = new Summary
                {
                    ok       = _results.FindAll(x => x.estado == "OK").Count,
                    revisar  = _results.FindAll(x => x.estado == "REVISAR").Count,
                    critico  = _results.FindAll(x => x.estado == "CRITICO").Count,
                    ssim_medio = validos > 0 ? ssimAcum / validos : 0f
                }
            };

            string outPath = Path.Combine(capturesAbs, "visual_verification_report.json");
            File.WriteAllText(outPath, JsonUtility.ToJson(report, true));
            Debug.Log($"[VerificadorVisual] Reporte guardado: {outPath}");
        }
    }
}
