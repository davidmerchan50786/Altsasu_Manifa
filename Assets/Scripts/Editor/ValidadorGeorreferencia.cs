// Assets/Scripts/Editor/ValidadorGeorreferencia.cs
// ─────────────────────────────────────────────────────────────────────────────
//  GATE DE VALIDACIÓN DE GEORREFERENCIACIÓN  (calidad AAA)
//
//  Verifica, sin necesidad de Play, que el mundo sigue en UTM real isótropo y
//  que los datos no han regresado a la compresión vieja ni perdido la autovía.
//  Pensado para correr antes de un commit / build, igual que ValidarMosaicoV2.
//
//  Menú:  Tools ▸ Alsasua ▸ Calidad ▸ ✅ Validar georreferenciación (<0.5 m)
//
//  Comprobaciones DURAS (hacen fallar el gate):
//    1. ESCALA_UTM_X == 1 (isótropo; el bug 0.93687 reintroduciría compresión).
//    2. UTMaUnity(origen UTM) cae en Herriko Plaza (OX, OZ).
//    3. UnityAUTM∘UTMaUnity = identidad (ida-vuelta exacta) en varios puntos.
//    4. La iglesia (OSM 91927762) cae en su sitio real OSM/Catastro (<1 m).
//    5. roads_unity.json contiene la autovía (≥1 tramo motorway/trunk).
//    6. buildings_unity.json tiene el censo esperado (>1000 edificios).
//  Comprobación INFORMATIVA (no falla): cota del terreno bajo la plaza.
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class ValidadorGeorreferencia
{
    const long  IGLESIA_ID = 91927762;
    static readonly Vector2 IGLESIA_ABS = new Vector2(1892.65f, 8235.53f); // OSM/Catastro real
    const float TOL_M = 0.5f;   // tolerancia objetivo
    const float TOL_IGLESIA = 1.0f;

    [MenuItem("Tools/Alsasua/Calidad/✅ Validar georreferenciación (<0.5 m)", priority = 0)]
    public static void Validar()
    {
        var fallos = new List<string>();
        var ok = new List<string>();

        // 1. Escala isótropa
        if (Mathf.Abs(GeoDataAlsasua.ESCALA_UTM_X - 1f) < 1e-6f)
            ok.Add("ESCALA_UTM_X = 1 (isótropo)");
        else
            fallos.Add($"ESCALA_UTM_X = {GeoDataAlsasua.ESCALA_UTM_X} (debe ser 1; ¿reintroducida la compresión?)");

        // 2. Origen → Herriko Plaza
        Vector2 o = GeoDataAlsasua.UTMaUnity(GeoDataAlsasua.UTM_E_ORIGIN, GeoDataAlsasua.UTM_N_ORIGIN);
        if (Mathf.Abs(o.x - GeoDataAlsasua.OX) < TOL_M && Mathf.Abs(o.y - GeoDataAlsasua.OZ) < TOL_M)
            ok.Add("Origen UTM mapea a Herriko Plaza (OX, OZ)");
        else
            fallos.Add($"Origen UTM cae en ({o.x:F2},{o.y:F2}) en vez de ({GeoDataAlsasua.OX},{GeoDataAlsasua.OZ})");

        // 3. Ida-vuelta identidad
        double[,] pts = { { 567951, 4749902 }, { 568951, 4750902 }, { 566000, 4748000 } };
        float maxErr = 0f;
        for (int i = 0; i < pts.GetLength(0); i++)
        {
            Vector2 u = GeoDataAlsasua.UTMaUnity(pts[i, 0], pts[i, 1]);
            GeoDataAlsasua.UnityAUTM(u.x, u.y, out double e2, out double n2);
            maxErr = Mathf.Max(maxErr, (float)System.Math.Abs(e2 - pts[i, 0]));
            maxErr = Mathf.Max(maxErr, (float)System.Math.Abs(n2 - pts[i, 1]));
        }
        if (maxErr < 0.05f) ok.Add($"Ida-vuelta UTM↔Unity exacta (máx {maxErr*100f:F2} cm)");
        else fallos.Add($"Ida-vuelta UTM↔Unity con error {maxErr:F3} m (>0.05)");

        // 4. Iglesia en su sitio (lee buildings_unity.json)
        try
        {
            JArray edif = LeerJsonArray("buildings_unity.json");
            int censo = edif?.Count ?? 0;
            JObject ig = null;
            if (edif != null)
                foreach (var b in edif)
                    if ((long)b["id"] == IGLESIA_ID) { ig = (JObject)b; break; }

            if (ig != null)
            {
                float cx = 0, cz = 0; int n = 0;
                foreach (var v in (JArray)ig["vertices"]) { cx += (float)v["x"]; cz += (float)v["z"]; n++; }
                cx = cx / n + GeoDataAlsasua.OX; cz = cz / n + GeoDataAlsasua.OZ;
                float d = Vector2.Distance(new Vector2(cx, cz), IGLESIA_ABS);
                if (d <= TOL_IGLESIA) ok.Add($"Iglesia a {d:F2} m de su posición OSM/Catastro real");
                else fallos.Add($"Iglesia a {d:F2} m del sitio real ({cx:F1},{cz:F1}) (>1 m)");
            }
            else fallos.Add("No se encontró la iglesia (id 91927762) en buildings_unity.json");

            // 6. Censo de edificios
            if (censo > 1000) ok.Add($"Censo de edificios = {censo}");
            else fallos.Add($"Censo de edificios sospechoso: {censo} (<=1000)");
        }
        catch (System.Exception ex) { fallos.Add("Error leyendo buildings_unity.json: " + ex.Message); }

        // 5. Autovía presente
        try
        {
            JArray roads = LeerJsonArray("roads_unity.json");
            int av = 0;
            if (roads != null)
                foreach (var r in roads)
                {
                    string t = (string)r["type"];
                    if (t == "motorway" || t == "trunk" || t == "motorway_link" || t == "trunk_link") av++;
                }
            if (av > 0) ok.Add($"Autovía presente ({av} tramos motorway/trunk)");
            else fallos.Add("Falta la autovía: 0 tramos motorway/trunk en roads_unity.json");
        }
        catch (System.Exception ex) { fallos.Add("Error leyendo roads_unity.json: " + ex.Message); }

        // 7. Cota del terreno bajo la plaza (informativo)
        string infoTerreno;
        var terr = Terrain.activeTerrain;
        if (terr != null)
        {
            float y = GeoDataAlsasua.AlturaTerreno(GeoDataAlsasua.OX, GeoDataAlsasua.OZ);
            float esperado = GeoDataAlsasua.COTA_PLAZA - GeoDataAlsasua.Z_MIN; // ≈ 20.61
            infoTerreno = $"Cota terreno en plaza: {y:F2} (esperado ≈ {esperado:F2}). " +
                          "Nota: en EditMode con mosaico Terrain.activeTerrain es arbitrario; " +
                          "validación fiable solo en Play o con ServicioTerreno listo.";
        }
        else infoTerreno = "Terreno no presente en la escena → comprobación de cota saltada.";

        // ── Reporte ───────────────────────────────────────────────────────────
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══ Validación georreferenciación Altsasu ═══");
        foreach (var s in ok) sb.AppendLine("  ✓ " + s);
        foreach (var f in fallos) sb.AppendLine("  ✗ " + f);
        sb.AppendLine("  · " + infoTerreno);

        if (fallos.Count == 0)
        {
            Debug.Log(sb.ToString());
            EditorUtility.DisplayDialog("Validación georreferenciación",
                "✅ TODO CORRECTO\n\nEl mundo está en UTM real isótropo y los datos\nestán en su sitio (<0.5 m). " +
                $"Iglesia, autovía y censo OK.\n\n{infoTerreno}", "Perfecto");
        }
        else
        {
            Debug.LogError(sb.ToString());
            EditorUtility.DisplayDialog("Validación georreferenciación",
                $"❌ {fallos.Count} FALLO(S)\n\n" + string.Join("\n", fallos) +
                "\n\nRevisa la consola para el detalle.", "Entendido");
        }
    }

    static JArray LeerJsonArray(string nombre)
    {
        string ruta = Path.Combine(Application.dataPath, "AlsasuaData", nombre);
        if (!File.Exists(ruta)) return null;
        return JArray.Parse(File.ReadAllText(ruta));
    }
}
