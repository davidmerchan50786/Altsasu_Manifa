// Assets/Scripts/Editor/ConfiguradorTrafico.cs
// Tools → Alsasua → 🚗 Configurar Tráfico desde OSM
//
// Lee roads_unity.json, extrae los segmentos de calle principal cerca de
// Herriko Plaza, crea GOs Transform como waypoints y los asigna al
// SistemaTrafico como carriles. Los coches del sistema son cajas
// procedurales — se verán en Play sin modelo externo.

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static class ConfiguradorTrafico
{
    private const string JSON_PATH = "Assets/AlsasuaData/roads_unity.json";
    private const float  CENTRO_X  = 1918f, CENTRO_Z = 8570f, RADIO = 400f;

    [MenuItem("Tools/Alsasua/🚗 Configurar Tráfico desde OSM", priority = 36)]
    public static void Configurar()
    {
        string ruta = Path.GetFullPath(Path.Combine(Application.dataPath, "..", JSON_PATH));
        if (!File.Exists(ruta)) { Debug.LogWarning("roads_unity.json no encontrado."); return; }

        // Asegurar que SistemaTrafico existe en escena
        var trafico = Object.FindFirstObjectByType<SistemaTrafico>();
        if (trafico == null)
        {
            var go = new GameObject("SistemaTrafico");
            trafico = go.AddComponent<SistemaTrafico>();
            Undo.RegisterCreatedObjectUndo(go, "Crear SistemaTrafico");
        }

        // Limpiar waypoints anteriores
        var prevWP = trafico.transform.Find("Waypoints_Trafico");
        if (prevWP != null) Undo.DestroyObjectImmediate(prevWP.gameObject);

        var contenedor = new GameObject("Waypoints_Trafico");
        Undo.RegisterCreatedObjectUndo(contenedor, "Crear waypoints tráfico");
        contenedor.transform.SetParent(trafico.transform, false);

        var segmentos = ParsearRoads(File.ReadAllText(ruta));
        var tiposViales = new[]{ "Carretera de calzada", "Vía urbana genérica", "Vía urbana conexión" };

        var carriles = new List<object>(); // SistemaTrafico.Carril

        // Tipo interno Carril
        var tipoCarril = typeof(SistemaTrafico).GetNestedType("Carril",
            BindingFlags.Public | BindingFlags.NonPublic);
        if (tipoCarril == null) { Debug.LogError("No se encontró el tipo Carril."); return; }

        int numCarril = 0;
        foreach (var seg in segmentos.Where(s => tiposViales.Contains(s.tipo)))
        {
            if (seg.pts.Count < 2) continue;
            float dx = seg.pts[0].x - CENTRO_X, dz = seg.pts[0].z - CENTRO_Z;
            if (dx*dx + dz*dz > RADIO * RADIO) continue;

            // Crear GOs de waypoints
            var grupoWP = new GameObject($"Carril_{numCarril:D2}");
            grupoWP.transform.SetParent(contenedor.transform, false);

            var wps = new Transform[seg.pts.Count];
            for (int i = 0; i < seg.pts.Count; i++)
            {
                var wp = new GameObject($"WP_{i:D2}");
                wp.transform.SetParent(grupoWP.transform, false);
                float y = Terrain.activeTerrain != null
                    ? Terrain.activeTerrain.SampleHeight(seg.pts[i]) + 0.3f
                    : seg.pts[i].y + 0.3f;
                wp.transform.position = new Vector3(seg.pts[i].x, y, seg.pts[i].z);
                wps[i] = wp.transform;
            }

            // Crear instancia de Carril via reflexión
            var carril = System.Activator.CreateInstance(tipoCarril);

            // Usar los nombres reales de campo (verificados en SistemaTrafico.Carril)
            SetF(tipoCarril, carril, "waypoints",      wps);
            SetF(tipoCarril, carril, "velocidadMaxKmh", seg.tipo.Contains("calzada") ? 50f : 30f);
            SetF(tipoCarril, carril, "esAutovia",       false);
            carriles.Add(carril);

            numCarril++;
            if (numCarril >= 12) break; // limitar a 12 carriles
        }

        // Asignar carriles al SistemaTrafico via reflexión
        var fieldCarriles = typeof(SistemaTrafico).GetField("carriles",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (fieldCarriles != null)
        {
            var arr = System.Array.CreateInstance(tipoCarril, carriles.Count);
            for (int i = 0; i < carriles.Count; i++) arr.SetValue(carriles[i], i);
            fieldCarriles.SetValue(trafico, arr);
        }

        EditorUtility.SetDirty(trafico);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Debug.Log($"[Tráfico] ✅ {numCarril} carriles configurados. Los coches aparecerán en Play.");
    }

    // ── Parseo mínimo de roads_unity.json ─────────────────────────────────
    struct SegVia { public string tipo; public List<Vector3> pts; }

    static List<SegVia> ParsearRoads(string json)
    {
        var lista = new List<SegVia>(); int pos = 0;
        while (pos < json.Length)
        {
            int ini = json.IndexOf('{', pos); if (ini < 0) break;
            int depth = 0, fin = ini;
            for (int k = ini; k < json.Length; k++)
            {
                if (json[k]=='{' || json[k]=='[') depth++;
                else if (json[k]=='}'||json[k]==']') depth--;
                if (depth==0) { fin=k; break; }
            }
            string obj = json.Substring(ini, fin-ini+1);
            var s = new SegVia { tipo = LeerS(obj,"t"), pts = new List<Vector3>() };
            int pi = obj.IndexOf("\"pts\":[");
            if (pi >= 0)
            {
                pi = obj.IndexOf('[',pi); int pf = obj.IndexOf(']',pi);
                var nums = obj.Substring(pi+1,pf-pi-1).Split(',');
                for (int k=0;k+2<nums.Length;k+=3)
                    if (float.TryParse(nums[k].Trim(),System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,out float x) &&
                        float.TryParse(nums[k+1].Trim(),System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,out float y) &&
                        float.TryParse(nums[k+2].Trim(),System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,out float z))
                        s.pts.Add(new Vector3(x,y,z));
            }
            if (s.pts.Count >= 2) lista.Add(s);
            pos = fin+1;
        }
        return lista;
    }
    static void SetF(System.Type t, object obj, string field, object val)
    {
        var f = t.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (f != null) f.SetValue(obj, val);
        else Debug.LogWarning($"[Trafico] Campo '{field}' no encontrado en {t.Name}");
    }

    static string LeerS(string j, string k)
    {
        int ki=j.IndexOf($"\"{k}\":\""); if(ki<0) return "";
        ki+=k.Length+4; int fi=j.IndexOf('"',ki); return fi<0?"":j.Substring(ki,fi-ki);
    }
}
