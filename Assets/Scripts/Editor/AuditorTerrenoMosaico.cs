// Assets/Scripts/Editor/AuditorTerrenoMosaico.cs
// ═══════════════════════════════════════════════════════════════════════════
//  AUDITOR TERRENO MOSAICO — verificación multi-método EN UNITY (fase F3 del
//  plan Terreno Mosaico V2). Complementa al gate Python (ValidarMosaicoV2.py):
//  aquí se verifica lo que Unity pudo corromper al importar/instanciar.
//
//  Checks:
//    1. Inventario: nº tiles == manifest, sin solapes de bounds intra-anillo
//    2. Costuras desde terrainData.GetHeights (mundo): intra-anillo exactas,
//       cross-ring coincidentes exactas
//    3. Continuidad de normales a ±0.5 m de cada costura (<2° intra, <8° cross)
//    4. SampleHeight vs RAW decodificado en N puntos aleatorios (≤1 cm)
//    5. Baseline pre-mosaico (edificios/árboles): distribución de Δaltura
//    6. Capturas: top-down del anillo 0 + rasantes de costuras cross-ring →
//       VerificationCaptures/
//    7. Scripts que aún usan Terrain.activeTerrain (informativo)
//    8. Reporte agregado → Assets/AlsasuaData/terrain_v2_audit_report.json
//
//  Requiere el mosaico en escena (bake del ConstructorMosaicoEditor, o Play
//  con el cargador runtime). El cruce con Cesium vive en SistemaDiagnostico
//  (runtime), donde el tileset existe de verdad.
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class AuditorTerrenoMosaico
{
    const string RUTA_REPORTE = "Assets/AlsasuaData/terrain_v2_audit_report.json";
    const string DIR_CAPTURAS = "Assets/AlsasuaData/VerificationCaptures";
    const float TOL_SEAM_INTRA = 0.0005f;  // m — igualdad float tras decode q/64
    const float TOL_SEAM_CROSS = 0.017f;   // m — 1 cuanto (1.5625 cm) + eps float
    const float TOL_NORMAL_INTRA = 2f;     // grados
    const float TOL_NORMAL_CROSS = 8f;
    const float TOL_SAMPLE_RAW = 0.01f;    // m
    const int N_PUNTOS_SAMPLE = 10000;

    class Tile
    {
        public Terrain terr;
        public MosaicoManifest.TileDef def;
        public MarcadorTerrenoAltsasua marca;
        public float X0 => def.x; public float Z0 => def.z; public float Ancho => def.ancho;
    }

    [MenuItem("Tools/Alsasua/Terreno/🔍 Auditar Mosaico V2", priority = 11)]
    public static void Auditar()
    {
        string rutaManifest = CargadorMosaicoTerreno.RutaManifest();
        if (rutaManifest == null)
        { EditorUtility.DisplayDialog("Auditor", "No hay manifest_v2.json.", "OK"); return; }
        var manifest = MosaicoManifest.Cargar(rutaManifest);

        // tiles en escena con marcador de mosaico
        var marcas = UnityEngine.Object.FindObjectsByType<MarcadorTerrenoAltsasua>(
            FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(m => m.fuente == FuenteTerreno.Mosaico && m.GetComponent<Terrain>() != null)
            .ToList();
        if (marcas.Count == 0)
        {
            EditorUtility.DisplayDialog("Auditor",
                "No hay tiles de mosaico en escena.\nEjecuta antes el bake: " +
                "Tools/Alsasua/Mundo/🧩 Construir Mosaico V2.", "OK");
            return;
        }

        var tiles = new List<Tile>();
        foreach (var m in marcas)
        {
            var terr = m.GetComponent<Terrain>();
            var p = terr.transform.position;
            var def = manifest.tiles.FirstOrDefault(d =>
                Mathf.Approximately(d.x, p.x) && Mathf.Approximately(d.z, p.z) &&
                d.anillo == m.anillo);
            if (def != null) tiles.Add(new Tile { terr = terr, def = def, marca = m });
        }

        var reporte = new JObject
        {
            ["fecha"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            ["escena"] = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
        };
        var checks = new JObject(); reporte["checks"] = checks;
        bool verde = true;
        void Check(string nombre, bool ok, string detalle, bool bloqueante = true)
        {
            checks[nombre] = new JObject { ["ok"] = ok, ["bloqueante"] = bloqueante, ["detalle"] = detalle };
            Debug.Log($"[Auditor] {(ok ? "✅" : bloqueante ? "❌" : "⚠")} {nombre}: {detalle}");
            if (!ok && bloqueante) verde = false;
        }

        try
        {
            // ═══ 1. Inventario y solapes ════════════════════════════════════
            EditorUtility.DisplayProgressBar("Auditor Mosaico", "Inventario...", 0.05f);
            bool solape = false;
            for (int i = 0; i < tiles.Count && !solape; i++)
                for (int j = i + 1; j < tiles.Count; j++)
                {
                    var a = tiles[i]; var b = tiles[j];
                    if (a.def.anillo != b.def.anillo) continue;
                    if (a.X0 < b.X0 + b.Ancho - 0.01f && b.X0 < a.X0 + a.Ancho - 0.01f &&
                        a.Z0 < b.Z0 + b.Ancho - 0.01f && b.Z0 < a.Z0 + a.Ancho - 0.01f)
                    { solape = true; break; }
                }
            Check("inventario", tiles.Count == manifest.tiles.Count && !solape,
                  $"{tiles.Count}/{manifest.tiles.Count} tiles en escena" +
                  (solape ? " — ¡SOLAPE de bounds!" : ", sin solapes"));

            // ═══ 2. Costuras desde GetHeights ═══════════════════════════════
            EditorUtility.DisplayProgressBar("Auditor Mosaico", "Costuras...", 0.2f);
            AuditarCosturas(tiles, manifest, Check);

            // ═══ 3. Normales en costuras ════════════════════════════════════
            EditorUtility.DisplayProgressBar("Auditor Mosaico", "Normales...", 0.4f);
            AuditarNormales(tiles, manifest, Check);

            // ═══ 4. SampleHeight vs RAW ═════════════════════════════════════
            EditorUtility.DisplayProgressBar("Auditor Mosaico", "SampleHeight vs RAW...", 0.55f);
            AuditarSampleVsRaw(tiles, manifest, Path.GetDirectoryName(rutaManifest), Check);

            // ═══ 5. Baseline pre-mosaico ════════════════════════════════════
            EditorUtility.DisplayProgressBar("Auditor Mosaico", "Baseline edificios/árboles...", 0.7f);
            AuditarBaseline(tiles, Check, reporte);

            // ═══ 6. Capturas ════════════════════════════════════════════════
            EditorUtility.DisplayProgressBar("Auditor Mosaico", "Capturas...", 0.8f);
            try
            {
                var rutas = CapturarVerificacion(tiles, manifest);
                Check("capturas", rutas.Count > 0,
                      $"{rutas.Count} capturas → {DIR_CAPTURAS}", bloqueante: false);
                reporte["capturas"] = new JArray(rutas);
            }
            catch (Exception ex)
            { Check("capturas", false, $"error: {ex.Message}", bloqueante: false); }

            // ═══ 7. Lectores de activeTerrain (informativo) ═════════════════
            EditorUtility.DisplayProgressBar("Auditor Mosaico", "Scripts activeTerrain...", 0.95f);
            var usos = ContarActiveTerrain();
            Check("lectoresActiveTerrain", true,
                  $"{usos.Count} scripts compilados aún usan Terrain.activeTerrain " +
                  "(capa compat TerrenoGlobal disponible)", bloqueante: false);
            reporte["activeTerrain"] = new JArray(usos);
        }
        finally { EditorUtility.ClearProgressBar(); }

        reporte["verde"] = verde;
        File.WriteAllText(Path.GetFullPath(RUTA_REPORTE),
                          reporte.ToString(Formatting.Indented), Encoding.UTF8);
        AssetDatabase.Refresh();
        Debug.Log($"[Auditor] {(verde ? "✅ AUDITORÍA VERDE" : "❌ AUDITORÍA ROJA")} → {RUTA_REPORTE}");
    }

    // ── alturas mundo de una línea de borde del heightmap ────────────────────
    static float[] BordeMundo(Tile t, bool fila, int indice)
    {
        var td = t.terr.terrainData;
        int res = td.heightmapResolution;
        float[,] h = fila ? td.GetHeights(0, indice, res, 1)
                          : td.GetHeights(indice, 0, 1, res);
        var outv = new float[res];
        float y = t.terr.transform.position.y, alto = td.size.y;
        for (int i = 0; i < res; i++)
            outv[i] = y + (fila ? h[0, i] : h[i, 0]) * alto;
        return outv;
    }

    static void AuditarCosturas(List<Tile> tiles, MosaicoManifest man, Action<string, bool, string, bool> check)
    {
        var porPos = tiles.ToDictionary(t => (t.def.anillo, t.X0, t.Z0));
        float peorIntra = 0f; int nIntra = 0;
        foreach (var t in tiles)
        {
            if (porPos.TryGetValue((t.def.anillo, t.X0 + t.Ancho, t.Z0), out var dE))
            {
                nIntra++;
                var a = BordeMundo(t, false, t.def.res - 1);
                var b = BordeMundo(dE, false, 0);
                for (int i = 0; i < a.Length; i++) peorIntra = Mathf.Max(peorIntra, Mathf.Abs(a[i] - b[i]));
            }
            if (porPos.TryGetValue((t.def.anillo, t.X0, t.Z0 + t.Ancho), out var dN))
            {
                nIntra++;
                var a = BordeMundo(t, true, t.def.res - 1);
                var b = BordeMundo(dN, true, 0);
                for (int i = 0; i < a.Length; i++) peorIntra = Mathf.Max(peorIntra, Mathf.Abs(a[i] - b[i]));
            }
        }
        check("costurasIntra", peorIntra <= TOL_SEAM_INTRA,
              $"{nIntra} aristas, max|Δ| = {peorIntra * 1000f:F2} mm (tol {TOL_SEAM_INTRA * 1000f:F1} mm)", true);

        // cross-ring: vértices del grueso coincidentes con el borde fino
        float peorCross = 0f; int nCross = 0;
        foreach (var (finoId, half) in new[] { (0, 1200f), (1, 3600f) })
        {
            float ox = (float)man.convencionHorizontal.OX, oz = (float)man.convencionHorizontal.OZ;
            var finos = tiles.Where(t => t.def.anillo == finoId).ToList();
            var gruesos = tiles.Where(t => t.def.anillo == finoId + 1).ToList();
            foreach (var tf in finos)
            {
                foreach (var (coord, esFila, idxF) in new[]
                {
                    (tf.Z0, true, 0), (tf.Z0 + tf.Ancho, true, tf.def.res - 1),
                    (tf.X0, false, 0), (tf.X0 + tf.Ancho, false, tf.def.res - 1)
                })
                {
                    if (Mathf.Abs(Mathf.Abs(coord - (esFila ? oz : ox)) - half) > 0.01f) continue;
                    var lineaF = BordeMundo(tf, esFila, idxF);
                    float iniF = esFila ? tf.X0 : tf.Z0;
                    float pasoF = tf.Ancho / (tf.def.res - 1);
                    foreach (var tg in gruesos)
                    {
                        float gLo = esFila ? tg.Z0 : tg.X0;
                        if (!(Mathf.Abs(coord - gLo) < 0.01f || Mathf.Abs(coord - gLo - tg.Ancho) < 0.01f)) continue;
                        float vLo = esFila ? tg.X0 : tg.Z0;
                        if (vLo >= iniF + tf.Ancho - 0.01f || vLo + tg.Ancho <= iniF + 0.01f) continue;
                        int idxG = Mathf.Abs(coord - gLo) < 0.01f ? 0 : tg.def.res - 1;
                        var lineaG = BordeMundo(tg, esFila, idxG);
                        float pasoG = tg.Ancho / (tg.def.res - 1);
                        nCross++;
                        for (int kg = 0; kg < tg.def.res; kg++)
                        {
                            float pos = vLo + kg * pasoG;
                            float fk = (pos - iniF) / pasoF;
                            int kf = Mathf.RoundToInt(fk);
                            if (Mathf.Abs(fk - kf) > 1e-3f || kf < 0 || kf >= tf.def.res) continue;
                            peorCross = Mathf.Max(peorCross, Mathf.Abs(lineaF[kf] - lineaG[kg]));
                        }
                    }
                }
            }
        }
        check("costurasCross", peorCross <= TOL_SEAM_CROSS,
              $"{nCross} bordes, max|Δ| vértices coincidentes = {peorCross * 1000f:F2} mm " +
              $"(tol {TOL_SEAM_CROSS * 1000f:F1} mm)", true);
    }

    static void AuditarNormales(List<Tile> tiles, MosaicoManifest man, Action<string, bool, string, bool> check)
    {
        Vector3 NormalEn(float wx, float wz, List<Tile> ts)
        {
            // tile más fino que contiene el punto
            Tile mejor = null;
            foreach (var t in ts)
                if (t.X0 <= wx && wx <= t.X0 + t.Ancho && t.Z0 <= wz && wz <= t.Z0 + t.Ancho)
                    if (mejor == null || t.def.anillo < mejor.def.anillo) mejor = t;
            if (mejor == null) return Vector3.up;
            var td = mejor.terr.terrainData;
            var p = mejor.terr.transform.position;
            return td.GetInterpolatedNormal((wx - p.x) / td.size.x, (wz - p.z) / td.size.z);
        }

        float ox = (float)man.convencionHorizontal.OX, oz = (float)man.convencionHorizontal.OZ;
        float peorIntra = 0f, peorCross = 0f;
        int n = 0;
        var rng = new System.Random(42);
        // muestrear las fronteras de bloque (cross) y algunas aristas intra
        foreach (var half in new[] { 1200f, 3600f })
        {
            for (int k = 0; k < 200; k++)
            {
                float a = (float)(rng.NextDouble() * 2.0 - 1.0) * half;
                foreach (var (wx, wz) in new[]
                {
                    (ox + a, oz - half), (ox + a, oz + half),
                    (ox - half, oz + a), (ox + half, oz + a)
                })
                {
                    bool vertical = Mathf.Abs(wz - oz + half) < 0.01f || Mathf.Abs(wz - oz - half) < 0.01f;
                    float dx = vertical ? 0f : 0.5f, dz = vertical ? 0.5f : 0f;
                    var n1 = NormalEn(wx - dx, wz - dz, tiles);
                    var n2 = NormalEn(wx + dx, wz + dz, tiles);
                    peorCross = Mathf.Max(peorCross, Vector3.Angle(n1, n2));
                    n++;
                }
            }
        }
        // aristas intra-anillo del anillo 1 (muestra)
        for (int k = 0; k < 400; k++)
        {
            float x = ox + ((k % 5) - 2) * 1200f;        // líneas de malla
            float z = oz + (float)(rng.NextDouble() * 2.0 - 1.0) * 3500f;
            if (Mathf.Abs(x - ox) <= 1200f && Mathf.Abs(z - oz) <= 1200f) continue;
            var n1 = NormalEn(x - 0.5f, z, tiles);
            var n2 = NormalEn(x + 0.5f, z, tiles);
            peorIntra = Mathf.Max(peorIntra, Vector3.Angle(n1, n2));
        }
        check("normalesIntra", peorIntra <= TOL_NORMAL_INTRA,
              $"max ángulo a ±0.5 m de costuras intra = {peorIntra:F2}° (tol {TOL_NORMAL_INTRA}°)", false);
        check("normalesCross", peorCross <= TOL_NORMAL_CROSS,
              $"max ángulo en fronteras cross-ring = {peorCross:F2}° (tol {TOL_NORMAL_CROSS}°, {n} pts)", false);
    }

    static void AuditarSampleVsRaw(List<Tile> tiles, MosaicoManifest man, string dirRaw,
                                   Action<string, bool, string, bool> check)
    {
        var rng = new System.Random(42);
        var cacheRaw = new Dictionary<string, ushort[]>();
        float peor = 0f; int n = 0;
        for (int k = 0; k < N_PUNTOS_SAMPLE; k++)
        {
            var t = tiles[rng.Next(tiles.Count)];
            float fx = (float)rng.NextDouble() * (t.def.res - 1);
            float fz = (float)rng.NextDouble() * (t.def.res - 1);
            float paso = t.Ancho / (t.def.res - 1);
            float wx = t.X0 + fx * paso, wz = t.Z0 + fz * paso;

            if (!cacheRaw.TryGetValue(t.def.file, out var q))
            {
                var bytes = File.ReadAllBytes(Path.Combine(dirRaw, t.def.file));
                q = new ushort[t.def.res * t.def.res];
                Buffer.BlockCopy(bytes, 0, q, 0, bytes.Length);
                cacheRaw[t.def.file] = q;
            }
            int i0 = Mathf.Min((int)fx, t.def.res - 2), j0 = Mathf.Min((int)fz, t.def.res - 2);
            float tx = fx - i0, tz = fz - j0;
            int res = t.def.res;
            float Decode(int jj, int ii) => t.def.y + q[jj * res + ii] / 64f;
            float esperado = (Decode(j0, i0) * (1 - tx) + Decode(j0, i0 + 1) * tx) * (1 - tz)
                           + (Decode(j0 + 1, i0) * (1 - tx) + Decode(j0 + 1, i0 + 1) * tx) * tz;
            float muestreado = t.terr.SampleHeight(new Vector3(wx, 0, wz)) + t.terr.transform.position.y;
            peor = Mathf.Max(peor, Mathf.Abs(muestreado - esperado));
            n++;
        }
        check("sampleVsRaw", peor <= TOL_SAMPLE_RAW,
              $"{n} puntos, max|SampleHeight − RAW decodificado| = {peor * 1000f:F2} mm " +
              $"(tol {TOL_SAMPLE_RAW * 1000f:F0} mm)", true);
    }

    static void AuditarBaseline(List<Tile> tiles, Action<string, bool, string, bool> check, JObject reporte)
    {
        string ruta = "Assets/AlsasuaData/baseline_alturas_pre_mosaico.json";
        if (!File.Exists(Path.GetFullPath(ruta)))
        {
            check("baseline", true,
                  "baseline_alturas_pre_mosaico.json no existe — omitido (capturarlo con el " +
                  "terreno viejo ya no es posible tras el bake; informativo)", false);
            return;
        }
        float Altura(float x, float z)
        {
            Tile mejor = null;
            foreach (var t in tiles)
                if (t.X0 <= x && x <= t.X0 + t.Ancho && t.Z0 <= z && z <= t.Z0 + t.Ancho)
                    if (mejor == null || t.def.anillo < mejor.def.anillo) mejor = t;
            if (mejor == null) return float.NaN;
            return mejor.terr.SampleHeight(new Vector3(x, 0, z)) + mejor.terr.transform.position.y;
        }
        var bl = JObject.Parse(File.ReadAllText(Path.GetFullPath(ruta)));
        var difs = new List<float>();
        int grandes = 0;
        foreach (var m in (JArray)bl["muestras"])
        {
            float x = (float)m["x"], z = (float)m["z"], antes = (float)m["alturaUnity"];
            float ahora = Altura(x, z);
            if (float.IsNaN(ahora)) continue;
            float d = ahora - antes;
            difs.Add(d);
            if (Mathf.Abs(d) > 0.5f) grandes++;
        }
        if (difs.Count == 0) { check("baseline", true, "sin muestras comparables", false); return; }
        difs.Sort();
        float mediana = difs[difs.Count / 2];
        float maxAbs = Mathf.Max(Mathf.Abs(difs[0]), Mathf.Abs(difs[^1]));
        // INFORMATIVO: el mosaico es MÁS preciso que el terreno viejo (2.9 m/px),
        // así que se esperan diferencias; lo que vigila es un offset SISTEMÁTICO
        check("baseline", Mathf.Abs(mediana) < 0.5f,
              $"{difs.Count} muestras: mediana Δ={mediana:+0.00;-0.00} m, max|Δ|={maxAbs:F2} m, " +
              $"{grandes} con |Δ|>0.5 m (esperable: el mosaico corrige el terreno viejo)", false);
        reporte["baselineStats"] = new JObject
        { ["mediana"] = mediana, ["maxAbs"] = maxAbs, ["n"] = difs.Count, ["mayores05"] = grandes };
    }

    // ── capturas de verificación ─────────────────────────────────────────────
    static List<string> CapturarVerificacion(List<Tile> tiles, MosaicoManifest man)
    {
        Directory.CreateDirectory(Path.GetFullPath(DIR_CAPTURAS));
        Directory.CreateDirectory(Path.GetFullPath($"{DIR_CAPTURAS}/seams"));
        var rutas = new List<string>();
        float ox = (float)man.convencionHorizontal.OX, oz = (float)man.convencionHorizontal.OZ;
        float yPlaza = 0f;
        foreach (var t in tiles)
            if (t.X0 <= ox && ox <= t.X0 + t.Ancho && t.Z0 <= oz && oz <= t.Z0 + t.Ancho && t.def.anillo == 0)
                yPlaza = t.terr.SampleHeight(new Vector3(ox, 0, oz)) + t.terr.transform.position.y;

        var go = new GameObject("_CamAuditor") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;

            // 1. top-down ortográfico del anillo 0 (2.4×2.4 km)
            cam.orthographic = true;
            cam.orthographicSize = 1200f;
            cam.transform.position = new Vector3(ox, yPlaza + 900f, oz);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.farClipPlane = 2000f;
            rutas.Add(Capturar(cam, 2048, $"{DIR_CAPTURAS}/auditor_topdown_anillo0.png"));

            // 2. rasantes en los puntos medios de las fronteras cross-ring
            cam.orthographic = false;
            cam.fieldOfView = 50f;
            foreach (var (half, etiqueta) in new[] { (1200f, "a0a1"), (3600f, "a1a2") })
            {
                foreach (var (px, pz, mira) in new[]
                {
                    (ox, oz - half, Vector3.forward), (ox, oz + half, Vector3.back),
                    (ox - half, oz, Vector3.right),  (ox + half, oz, Vector3.left)
                })
                {
                    float y = AlturaEnTiles(tiles, px, pz) + 2.0f;
                    cam.transform.position = new Vector3(px, y, pz) - mira * 60f + Vector3.up * 8f;
                    cam.transform.rotation = Quaternion.LookRotation(
                        (new Vector3(px, y, pz) - cam.transform.position).normalized);
                    string nombre = $"{DIR_CAPTURAS}/seams/seam_{etiqueta}_{mira.x:F0}{mira.z:F0}_{px:F0}_{pz:F0}.png";
                    rutas.Add(Capturar(cam, 1280, nombre));
                }
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
        AssetDatabase.Refresh();
        return rutas;
    }

    static float AlturaEnTiles(List<Tile> tiles, float x, float z)
    {
        Tile mejor = null;
        foreach (var t in tiles)
            if (t.X0 <= x && x <= t.X0 + t.Ancho && t.Z0 <= z && z <= t.Z0 + t.Ancho)
                if (mejor == null || t.def.anillo < mejor.def.anillo) mejor = t;
        return mejor == null ? 0f
            : mejor.terr.SampleHeight(new Vector3(x, 0, z)) + mejor.terr.transform.position.y;
    }

    static string Capturar(Camera cam, int lado, string ruta)
    {
        var rt = new RenderTexture(lado, lado, 24);
        cam.targetTexture = rt;
        cam.Render();
        var tex = new Texture2D(lado, lado, TextureFormat.RGB24, false);
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, lado, lado), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        File.WriteAllBytes(Path.GetFullPath(ruta), tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);
        return ruta;
    }

    static List<string> ContarActiveTerrain()
    {
        var resultado = new List<string>();
        string raiz = Path.Combine(Application.dataPath, "Scripts");
        foreach (var f in Directory.EnumerateFiles(raiz, "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains("_Deprecated~") || f.Contains("_RecuperadosMain~")) continue;
            int c = 0; foreach (var ln in File.ReadLines(f)) if (ln.Contains("Terrain.activeTerrain")) c++;
            if (c > 0) resultado.Add($"{Path.GetFileName(f)}: {c}");
        }
        return resultado;
    }
}
