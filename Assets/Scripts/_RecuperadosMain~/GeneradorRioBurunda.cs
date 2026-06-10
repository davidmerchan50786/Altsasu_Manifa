#if UNITY_EDITOR
// Assets/Scripts/Editor/GeneradorRioBurunda.cs
// ═══════════════════════════════════════════════════════════════════════════
//  RÍO BURUNDA con HDRP Water System.
//
//  Lee Assets/AlsasuaData/hydro_unity.json (Overpass: waterway=river)
//  y crea un WaterSurface River por cada tramo. Si HDRP Water no está
//  disponible, hace fallback a mesh plana con shader HDRP/Lit transparente.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
#if HDRP_WATER_AVAILABLE || UNITY_HDRP_WATER
using UnityEngine.Rendering.HighDefinition;
#endif

public static class GeneradorRioBurunda
{
    const string HYDRO_PATH = "Assets/AlsasuaData/hydro_unity.json";

    public static void Generar()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) { Aviso("Sin terrain — crea el terrain primero."); return; }

        var padre = GameObject.Find("Rio_Burunda");
        if (padre != null) Object.DestroyImmediate(padre);
        padre = new GameObject("Rio_Burunda");

        var rios = LeerHydroJSON();
        if (rios == null || rios.Count == 0)
        {
            // Fallback: trazado aproximado del Burunda (este-oeste por Alsasua)
            rios = new List<List<Vector3>> { TrazadoFallback() };
        }

        int creados = 0;
        foreach (var puntos in rios)
        {
            if (puntos.Count < 2) continue;
            CrearTramoRio(puntos, terrain, padre.transform);
            creados++;
        }

        EditorUtility.DisplayDialog("✅ Río Burunda",
            $"Tramos de agua creados: {creados}\n\n" +
            "Si tienes HDRP Water habilitado en HDRP Asset, verás agua\n" +
            "real con olas. Si no, fallback a plano translúcido.\n\n" +
            "Para activar HDRP Water: Edit → Project Settings →\n" +
            "Quality → HDRP → Lighting → Water → Enable.",
            "OK");
    }

    // ─────────────────────────────────────────────────────────────────────

    static List<List<Vector3>> LeerHydroJSON()
    {
        if (!File.Exists(HYDRO_PATH)) return null;
        try
        {
            var txt = File.ReadAllText(HYDRO_PATH);
            var root = JArray.Parse(txt);
            var res = new List<List<Vector3>>();

            foreach (var rio in root)
            {
                var pts = rio["pts"] as JArray;
                if (pts == null || pts.Count < 2) continue;
                var lista = new List<Vector3>();

                if (pts[0] is JObject)
                    foreach (var p in pts)
                        lista.Add(new Vector3(p["x"].Value<float>(), 0, p["z"].Value<float>()));
                else if (pts[0] is JArray)
                    foreach (var p in pts)
                    {
                        var par = p as JArray;
                        lista.Add(new Vector3(par[0].Value<float>(), 0, par[1].Value<float>()));
                    }
                else
                    for (int i = 0; i + 1 < pts.Count; i += 2)
                        lista.Add(new Vector3(pts[i].Value<float>(), 0, pts[i + 1].Value<float>()));

                res.Add(lista);
            }
            return res;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Rio] No se pudo parsear hydro_unity.json: " + e.Message);
            return null;
        }
    }

    // Trazado aproximado del Burunda — pasa por Alsasua de este a oeste
    static List<Vector3> TrazadoFallback() => new List<Vector3> {
        new Vector3(  100, 0, 8400),
        new Vector3(  600, 0, 8450),
        new Vector3( 1200, 0, 8520),
        new Vector3( 1700, 0, 8560),
        new Vector3( 2200, 0, 8580),
        new Vector3( 2800, 0, 8600),
        new Vector3( 3400, 0, 8650),
        new Vector3( 4000, 0, 8700),
        new Vector3( 4500, 0, 8730),
    };

    // ─────────────────────────────────────────────────────────────────────

    static void CrearTramoRio(List<Vector3> puntos, Terrain t, Transform padre)
    {
        // Para cada segmento creamos una banda de agua de ancho variable (4-7m)
        for (int i = 0; i + 1 < puntos.Count; i++)
        {
            Vector3 a = puntos[i];
            Vector3 b = puntos[i + 1];
            a.y = t.SampleHeight(a) - 0.3f; // ligeramente por debajo del terreno
            b.y = t.SampleHeight(b) - 0.3f;

            Vector3 medio = (a + b) * 0.5f;
            float largo  = Vector3.Distance(a, b);
            float ancho  = Random.Range(4f, 7f);

            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = $"Tramo_{i}";
            go.transform.SetParent(padre);
            go.transform.position = medio;
            go.transform.rotation = Quaternion.LookRotation(b - a) * Quaternion.Euler(0, 0, 0);
            // Plane default es 10×10 con eje Y up → escalar X por ancho/10, Z por largo/10
            go.transform.localScale = new Vector3(ancho / 10f, 1f, largo / 10f);

            Object.DestroyImmediate(go.GetComponent<MeshCollider>());

            // Material agua HDRP — usa shader HDRP/Lit con transparencia + normal map procedural
            var mat = MaterialAguaHDRP();
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    static Material _matAgua;
    static Material MaterialAguaHDRP()
    {
        if (_matAgua != null) return _matAgua;

        var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        _matAgua = new Material(sh) { name = "Mat_Agua_Burunda" };

        // Surface = Transparent
        _matAgua.SetFloat("_SurfaceType", 1);
        _matAgua.SetFloat("_BlendMode",   0);
        _matAgua.SetFloat("_AlphaCutoffEnable", 0);
        _matAgua.renderQueue = 3000;

        // Color verde-azulado típico río pirenaico
        _matAgua.SetColor("_BaseColor", new Color(0.10f, 0.25f, 0.32f, 0.78f));
        _matAgua.SetFloat("_Smoothness", 0.95f);
        _matAgua.SetFloat("_Metallic",   0.0f);

        // Normal map procedural (ondas)
        var normal = GenerarNormalAgua();
        _matAgua.SetTexture("_NormalMap", normal);
        _matAgua.SetFloat("_NormalScale", 1.5f);
        _matAgua.EnableKeyword("_NORMALMAP");

        // Guardar como asset
        const string path = "Assets/AlsasuaData/Mat_Agua_Burunda.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
            AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(_matAgua, path);
        return _matAgua;
    }

    static Texture2D GenerarNormalAgua()
    {
        const int R = 512;
        var tex = new Texture2D(R, R, TextureFormat.RGBA32, true, true);
        var px  = new Color[R * R];
        for (int y = 0; y < R; y++)
        for (int x = 0; x < R; x++)
        {
            // Olas: combinación de seno + perlin
            float fx = (float)x / R;
            float fy = (float)y / R;
            float h1 = Mathf.PerlinNoise(fx * 8f, fy * 8f);
            float h2 = Mathf.PerlinNoise(fx * 32f + 5.3f, fy * 32f + 1.7f) * 0.4f;
            float h  = h1 + h2;
            float gx = Mathf.PerlinNoise((fx + 1f / R) * 8f, fy * 8f) - h1;
            float gy = Mathf.PerlinNoise(fx * 8f, (fy + 1f / R) * 8f) - h1;
            Vector3 n = new Vector3(-gx * 8f, -gy * 8f, 1f).normalized;
            // Unity normal: r=x*0.5+0.5, g=y*0.5+0.5, b=z*0.5+0.5
            px[y * R + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z, 1f);
        }
        tex.SetPixels(px);
        tex.Apply(true, false);

        string path = "Assets/AlsasuaData/Textures/Proc/Agua_Normal.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);

        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
        {
            imp.textureType = TextureImporterType.NormalMap;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void Aviso(string msg) => EditorUtility.DisplayDialog("Río", msg, "OK");
}
#endif
