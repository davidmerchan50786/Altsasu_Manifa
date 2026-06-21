// Assets/Scripts/Editor/BaselineAlturasEditor.cs
// ═══════════════════════════════════════════════════════════════════════════
//  BASELINE DE ALTURAS PRE-MOSAICO (F0 del plan Terreno Mosaico V2)
//
//  Captura la altura del terreno ACTUAL bajo ~1000 edificios (centroides de
//  buildings_final.json) y ~1000 árboles (trees_unity.json) y la guarda en
//  Assets/AlsasuaData/baseline_alturas_pre_mosaico.json.
//
//  Tras construir el mosaico V2, el AuditorTerrenoMosaico compara estas
//  alturas con las nuevas: nada existente debe flotar ni enterrarse >0.5 m.
//
//  Uso: entrar en Play (el terreno se genera en runtime), esperar al log
//  "✅ Suelo listo", y ejecutar Tools/Alsasua/Mundo/📐 Capturar Baseline Alturas.
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class BaselineAlturasEditor
{
    const int MAX_MUESTRAS_POR_TIPO = 1000;
    const string PATH_SALIDA    = "Assets/AlsasuaData/baseline_alturas_pre_mosaico.json";
    const string PATH_EDIFICIOS = "Assets/AlsasuaData/buildings_final.json";
    const string PATH_ARBOLES   = "Assets/AlsasuaData/trees_unity.json";

    [Serializable]
    public class Muestra
    {
        public string tipo;        // "edificio" | "arbol" | "referencia"
        public string id;
        public float  x, z;        // coords Unity
        public float  alturaUnity; // SampleHeight + transform.y (datum Z_MIN)
    }

    [MenuItem("Tools/Alsasua/Terreno/📐 Capturar Baseline Alturas", priority = 12)]
    public static void Capturar()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("Baseline Alturas",
                "No hay Terrain activo en la escena.\n\n" +
                "Entra en Play, espera al log \"✅ Suelo listo\" de ServicioTerreno " +
                "y vuelve a ejecutar este menú.", "OK");
            return;
        }

        var muestras = new List<Muestra>(MAX_MUESTRAS_POR_TIPO * 2 + 8);

        // ── Puntos de referencia fijos ────────────────────────────────────
        muestras.Add(Muestrear("referencia", "HerrikoPlaza", GeoDataAlsasua.OX, GeoDataAlsasua.OZ, terrain));
        muestras.Add(Muestrear("referencia", "EstacionTren", GeoDataAlsasua.EstacionTren.x, GeoDataAlsasua.EstacionTren.z, terrain));
        muestras.Add(Muestrear("referencia", "CuartelGC", GeoDataAlsasua.CuartelGC.x, GeoDataAlsasua.CuartelGC.z, terrain));

        // ── Edificios: centroide del footprint (vértices OSM-relativos) ──
        int nEdif = 0;
        string pathEdif = Path.GetFullPath(PATH_EDIFICIOS);
        if (File.Exists(pathEdif))
        {
            var edificios = JArray.Parse(File.ReadAllText(pathEdif));
            int paso = Mathf.Max(1, edificios.Count / MAX_MUESTRAS_POR_TIPO);
            for (int i = 0; i < edificios.Count; i += paso)
            {
                var ed = edificios[i];
                var verts = ed["vertices"] as JArray;
                if (verts == null || verts.Count == 0) continue;

                float cx = 0f, cz = 0f;
                foreach (var v in verts)
                {
                    cx += (float)v["x"];
                    cz += (float)v["z"];
                }
                cx = cx / verts.Count + GeoDataAlsasua.OX;
                cz = cz / verts.Count + GeoDataAlsasua.OZ;

                muestras.Add(Muestrear("edificio", ed["id"]?.ToString() ?? i.ToString(), cx, cz, terrain));
                nEdif++;
            }
        }
        else Debug.LogWarning($"[Baseline] No existe {PATH_EDIFICIOS}");

        // ── Árboles: coords Unity absolutas ───────────────────────────────
        int nArb = 0;
        string pathArb = Path.GetFullPath(PATH_ARBOLES);
        if (File.Exists(pathArb))
        {
            var arboles = JArray.Parse(File.ReadAllText(pathArb));
            int paso = Mathf.Max(1, arboles.Count / MAX_MUESTRAS_POR_TIPO);
            for (int i = 0; i < arboles.Count; i += paso)
            {
                var a = arboles[i];
                muestras.Add(Muestrear("arbol", i.ToString(), (float)a["x"], (float)a["z"], terrain));
                nArb++;
            }
        }
        else Debug.LogWarning($"[Baseline] No existe {PATH_ARBOLES}");

        // ── Guardar ───────────────────────────────────────────────────────
        var salida = new
        {
            fecha          = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            terreno        = terrain.name,
            terrenoSize    = new { x = terrain.terrainData.size.x, y = terrain.terrainData.size.y, z = terrain.terrainData.size.z },
            terrenoPosY    = terrain.transform.position.y,
            datumZMin      = GeoDataAlsasua.Z_MIN,
            nota           = "alturaUnity = SampleHeight + transform.y (cota real = alturaUnity + datumZMin)",
            muestras
        };

        File.WriteAllText(Path.GetFullPath(PATH_SALIDA),
            JsonConvert.SerializeObject(salida, Formatting.Indented));
        AssetDatabase.Refresh();

        Debug.Log($"[Baseline] ✅ Guardadas {muestras.Count} muestras " +
                  $"({nEdif} edificios, {nArb} árboles, 3 referencias) en {PATH_SALIDA}\n" +
                  $"  Plaza: alturaUnity={muestras[0].alturaUnity:F2} " +
                  $"(cota real {muestras[0].alturaUnity + GeoDataAlsasua.Z_MIN:F2} m, " +
                  $"esperada {GeoDataAlsasua.COTA_PLAZA:F2} m)");
    }

    static Muestra Muestrear(string tipo, string id, float x, float z, Terrain t)
        => new Muestra
        {
            tipo = tipo,
            id   = id,
            x    = x,
            z    = z,
            alturaUnity = t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y
        };
}
