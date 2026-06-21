// Assets/Scripts/Editor/ProcesadorDSMEditor.cs
// Tools → Alsasua → 🏔 Procesar DSM/LIDAR
// Tools → Alsasua → ⬇ Descargar LIDAR IGN

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public static class ProcesadorDSMEditor
{
    const string PATH_DSM     = "Assets/AlsasuaData/dsm_alsasua_5m.asc";
    const string PATH_DTM     = "Assets/AlsasuaData/dtm_alsasua_5m.asc";
    const string PATH_SCRIPT  = "DescargarLIDARAlsasua.ps1";
    const float  OX = GeoDataAlsasua.OX, OZ = GeoDataAlsasua.OZ;

    // ── Descargar IGN ─────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/Terreno/⬇ Descargar LIDAR IGN (DSM real)", priority = 53)]
    public static void DescargarLIDAR()
    {
        string scriptPath = Path.Combine(
            Application.dataPath.Replace("Assets", ""), PATH_SCRIPT);

        if (!File.Exists(scriptPath))
        {
            EditorUtility.DisplayDialog("⬇ Descargar LIDAR",
                "Script no encontrado en:\n" + scriptPath + "\n\n" +
                "Por favor, asegúrate de que DescargarLIDARAlsasua.ps1 existe.", "OK");
            return;
        }

        bool ok = EditorUtility.DisplayDialog("⬇ Descargar LIDAR IGN",
            "Descargará del IGN (Instituto Geográfico Nacional):\n\n" +
            "• DSM 5m — superficie con tejados reales\n" +
            "• DTM 5m — terreno sin edificios\n" +
            "• LIDAR PNOA — nube de puntos (si disponible)\n\n" +
            "Requiere conexión a internet.\n" +
            "Fuente: centros.cnig.es / idee.es\n" +
            "Licencia: CC BY 4.0 (IGN España)\n\n" +
            "¿Ejecutar descarga?",
            "⬇ Descargar", "Cancelar");

        if (!ok) return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = "powershell.exe",
            Arguments       = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = true,
        });

        EditorUtility.DisplayDialog("⬇ Descarga iniciada",
            "La descarga corre en PowerShell.\n\n" +
            "Cuando termine, vuelve a Unity y ejecuta:\n" +
            "Tools → Alsasua → 🏔 Procesar DSM/LIDAR",
            "OK");
    }

    // ── Procesar y aplicar DSM ────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/Terreno/🏔 Procesar DSM/LIDAR (tejados reales)", priority = 54)]
    public static void ProcesarDSM()
    {
        string dsmFull = Path.Combine(Application.dataPath.Replace("Assets",""), PATH_DSM);

        if (!File.Exists(dsmFull))
        {
            EditorUtility.DisplayDialog("🏔 DSM no encontrado",
                "No se encontró:\n" + PATH_DSM + "\n\n" +
                "Ejecuta primero:\n" +
                "Tools → Alsasua → ⬇ Descargar LIDAR IGN\n\n" +
                "O coloca manualmente el archivo dsm_alsasua_5m.asc\n" +
                "en Assets/AlsasuaData/",
                "OK");
            return;
        }

        EditorUtility.DisplayProgressBar("🏔 Procesando DSM", "Cargando datos...", 0f);

        try
        {
            // Cargar DSM y DTM
            var dsmData = LeerASCGrid(dsmFull,
                out int ncols, out int nrows, out float xll, out float yll,
                out float cell, out float nodata);

            string dtmFull = Path.Combine(Application.dataPath.Replace("Assets",""), PATH_DTM);
            float[] dtmData = File.Exists(dtmFull)
                ? LeerASCGrid(dtmFull, out _, out _, out _, out _, out _, out _)
                : null;

            AlsasuaLogger.Info("DSM",
                $"DSM cargado: {ncols}×{nrows}, cell={cell}m, xll={xll:F4}, yll={yll:F4}");

            // Cargar edificios OSM
            string jsonPath = Path.Combine(Application.dataPath.Replace("Assets",""),
                "Assets/AlsasuaData/buildings_unity.json");
            var edificios = JsonHelper.ParseArray<EdificioData>(
                File.ReadAllText(jsonPath));

            EditorUtility.DisplayProgressBar("🏔 DSM", "Procesando tejados...", 0.2f);

            var parent = GameObject.Find("Tejados_DSM");
            if (parent == null) parent = new GameObject("Tejados_DSM");

            int procesados = 0, conDatos = 0;

            for (int i = 0; i < edificios.Length; i++)
            {
                var ed = edificios[i];
                if (ed.vertices == null || ed.vertices.Length < 3) continue;

                float progreso = (float)i / edificios.Length;
                EditorUtility.DisplayProgressBar("🏔 DSM",
                    $"Edificio {i}/{edificios.Length}: {ed.name ?? ed.id.ToString()}", 0.2f + progreso * 0.7f);

                var footprint = ed.vertices
                    .Select(v => new Vector2(v.x + OX, v.z + OZ))
                    .ToArray();

                var puntos = MuestrarPuntosDSM(footprint, dsmData, dtmData,
                    ncols, nrows, xll, yll, cell, nodata);

                if (puntos != null && puntos.Count >= 4)
                {
                    float yBase = puntos.Min(p => p.y);
                    var mesh = GeneradorGeometriaPrecisa.Instance != null
                        ? null // usa ProcesadorNubePuntos en runtime
                        : GenerarMeshDelaunay(puntos, yBase);

                    if (mesh != null)
                    {
                        var go = new GameObject($"Tejado_DSM_{ed.id}");
                        go.transform.SetParent(parent.transform, false);
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        go.AddComponent<MeshRenderer>().sharedMaterial =
                            AssetDatabase.LoadAssetAtPath<Material>(
                                "Assets/Materials/Personajes/NPC_Civil_Casual.mat")
                            ?? new Material(Shader.Find("HDRP/Lit"));
                        go.isStatic = true;
                        conDatos++;
                    }
                }

                procesados++;
            }

            AssetDatabase.Refresh();
            EditorUtility.ClearProgressBar();

            EditorUtility.DisplayDialog("🏔 DSM procesado",
                $"Resultados:\n\n" +
                $"• {procesados} edificios procesados\n" +
                $"• {conDatos} con tejado reconstruido desde DSM real\n" +
                $"• {procesados - conDatos} sin datos DSM suficientes\n\n" +
                $"Los tejados DSM tienen la forma EXACTA del IGN.\n" +
                $"Resolución del DSM: {cell}m/pixel",
                "OK");
        }
        catch (System.Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"[DSM] Error: {e.Message}\n{e.StackTrace}");
            EditorUtility.DisplayDialog("Error", $"Error procesando DSM:\n{e.Message}", "OK");
        }
    }

    // ── Estado del DSM ────────────────────────────────────────────────────

    [MenuItem("Tools/Alsasua/Debug/📊 Estado DSM/LIDAR", priority = 20)]
    public static void EstadoDSM()
    {
        string dsmFull = Path.Combine(Application.dataPath.Replace("Assets",""), PATH_DSM);
        string dtmFull = Path.Combine(Application.dataPath.Replace("Assets",""), PATH_DTM);
        string lazFull = Path.Combine(Application.dataPath.Replace("Assets",""),
            "Assets/AlsasuaData/lidar_alsasua.laz");

        string msg = "=== ESTADO DATOS 3D ALSASUA ===\n\n";
        msg += $"DSM (tejados): {(File.Exists(dsmFull) ? $"✅ {new FileInfo(dsmFull).Length/1024}KB" : "❌ No encontrado")}\n";
        msg += $"DTM (terreno): {(File.Exists(dtmFull) ? $"✅ {new FileInfo(dtmFull).Length/1024}KB" : "❌ No encontrado")}\n";
        msg += $"LIDAR LAZ:     {(File.Exists(lazFull)  ? $"✅ {new FileInfo(lazFull).Length/1024}KB"  : "❌ No encontrado")}\n";
        msg += "\nSi no están descargados:\n";
        msg += "Tools → ⬇ Descargar LIDAR IGN\n\n";
        msg += "Fuentes manuales gratuitas:\n";
        msg += "• IGN: centrodedescargas.cnig.es\n";
        msg += "• IDENA Navarra: idena.navarra.es/descargas\n";
        msg += "• CNIG: ftp.cnig.es/ATOM/MDT/MDT05/\n";
        msg += "\nHoja MTN50: #114 (Alsasua/Altsasua)\n";
        msg += "Coordenadas: 42.875-42.920N, 2.195-2.145W\n";
        msg += "Sistema: ETRS89 UTM 30N (EPSG:25830)\n";

        Debug.Log(msg);
        EditorUtility.DisplayDialog("📊 Estado DSM/LIDAR", msg, "OK");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ALGORITMOS (espejo de ProcesadorNubePuntos para el Editor)
    // ════════════════════════════════════════════════════════════════════════

    static float[] LeerASCGrid(string path,
        out int ncols, out int nrows,
        out float xll, out float yll,
        out float cell, out float nodata)
    {
        ncols = nrows = 0; xll = yll = cell = 0f; nodata = -9999f;
        using var r = new StreamReader(path);
        ncols  = int.Parse(r.ReadLine().Split()[1]);
        nrows  = int.Parse(r.ReadLine().Split()[1]);
        xll    = float.Parse(r.ReadLine().Split()[1], System.Globalization.CultureInfo.InvariantCulture);
        yll    = float.Parse(r.ReadLine().Split()[1], System.Globalization.CultureInfo.InvariantCulture);
        cell   = float.Parse(r.ReadLine().Split()[1], System.Globalization.CultureInfo.InvariantCulture);
        nodata = float.Parse(r.ReadLine().Split()[1], System.Globalization.CultureInfo.InvariantCulture);
        var data = new float[ncols * nrows];
        int idx = 0; string line;
        while ((line = r.ReadLine()) != null && idx < data.Length)
            foreach (var t in line.Split(' ','\t'))
                if (!string.IsNullOrWhiteSpace(t) && idx < data.Length)
                    data[idx++] = float.Parse(t, System.Globalization.CultureInfo.InvariantCulture);
        return data;
    }

    const double LAT0 = 42.8987, LON0 = -2.1677;
    const float M_LAT = 111320f, M_LON = 76400f;

    static List<Vector3> MuestrarPuntosDSM(Vector2[] footprint,
        float[] dsm, float[] dtm,
        int ncols, int nrows, float xll, float yll, float cell, float nodata)
    {
        float minX = footprint.Min(v=>v.x), maxX = footprint.Max(v=>v.x);
        float minZ = footprint.Min(v=>v.y), maxZ = footprint.Max(v=>v.y);
        var result = new List<Vector3>();

        float lonMin = (minX - OX) / M_LON + (float)LON0;
        float lonMax = (maxX - OX) / M_LON + (float)LON0;
        float latMin = (minZ - OZ) / M_LAT + (float)LAT0;
        float latMax = (maxZ - OZ) / M_LAT + (float)LAT0;

        int c0 = Mathf.Max(0, Mathf.FloorToInt((lonMin - xll) / cell));
        int c1 = Mathf.Min(ncols-1, Mathf.CeilToInt((lonMax - xll) / cell));
        int r0 = Mathf.Max(0, nrows-1-Mathf.CeilToInt((latMax - yll) / cell));
        int r1 = Mathf.Min(nrows-1, nrows-1-Mathf.FloorToInt((latMin - yll) / cell));

        for (int row = r0; row <= r1; row++)
        for (int col = c0; col <= c1; col++)
        {
            float h = dsm[row * ncols + col];
            if (Mathf.Approximately(h, nodata)) continue;

            float lon = xll + col * cell;
            float lat = yll + (nrows - 1 - row) * cell;
            float ux = (float)((lon - LON0) * M_LON) + OX;
            float uz = (float)((lat - LAT0) * M_LAT) + OZ;
            var p2 = new Vector2(ux, uz);

            if (!PuntoEnPoli(p2, footprint)) continue;

            float ndsm = h;
            if (dtm != null)
            {
                float dt = dtm[row * ncols + col];
                if (!Mathf.Approximately(dt, nodata)) ndsm = h - dt;
            }
            if (ndsm < 1.5f) continue;
            result.Add(new Vector3(ux, ndsm, uz));
        }
        return result.Count >= 3 ? result : null;
    }

    static bool PuntoEnPoli(Vector2 p, Vector2[] poly)
    {
        bool inside = false; int n = poly.Length;
        for (int i=0, j=n-1; i<n; j=i++)
        {
            if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                p.x < (poly[j].x-poly[i].x)*(p.y-poly[i].y)/(poly[j].y-poly[i].y)+poly[i].x)
                inside = !inside;
        }
        return inside;
    }

    static Mesh GenerarMeshDelaunay(List<Vector3> pts, float yBase)
    {
        if (pts.Count < 3) return null;

        // Usar el algoritmo Bowyer-Watson real de ProcesadorNubePuntos
        var pts2D  = pts.Select(p => new Vector2(p.x, p.z)).ToList();
        var tris   = ProcesadorNubePuntos.DelaunayBowyerWatson(pts2D);

        // Fallback fan si Delaunay falla (puntos colineales, etc.)
        if (tris == null || tris.Count == 0)
        {
            Vector2 c = new Vector2(pts.Average(p => p.x), pts.Average(p => p.z));
            var ordered = pts.OrderBy(p => Mathf.Atan2(p.z - c.y, p.x - c.x)).ToList();
            tris = new System.Collections.Generic.List<int>();
            for (int i = 1; i < ordered.Count - 1; i++)
            { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
            pts = ordered;   // usar el orden para los vértices
        }

        var mesh  = new Mesh { name = "TejadoDSM" };
        mesh.indexFormat = pts.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(pts.Select(p => new Vector3(p.x, yBase + p.y, p.z)).ToList());
        mesh.SetTriangles(tris, 0);

        // UVs normalizadas desde posición XZ
        float minX = pts2D.Min(p => p.x), dX = pts2D.Max(p => p.x) - minX + 0.001f;
        float minZ = pts2D.Min(p => p.y), dZ = pts2D.Max(p => p.y) - minZ + 0.001f;
        mesh.SetUVs(0, pts.Select(p =>
            new Vector2((p.x - minX) / dX, (p.z - minZ) / dZ)).ToList());

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
#endif
