#if UNITY_EDITOR
// Assets/Scripts/Editor/GeneradorVegetacionReal.cs
// ═══════════════════════════════════════════════════════════════════════════
//  GENERADOR DE VEGETACIÓN REAL — usa los FBX de Assets/Models/Flora/
//
//  Distribuye los modelos reales de árboles y plantas en:
//   • Zonas de bosque (GeoDataAlsasua.ZonasBosque) — densidad alta
//   • Bordes de carretera — densidad media
//   • Parques y plazas — densidad baja decorativa
//
//  Usa los 24 modelos disponibles en Assets/Models/Flora/ (01.FBX..07.FBX,
//  bamboo, bush, hemp, swirl, white_flower, etc.) con variaciones.
//
//  MENÚ: Altsasu GTA → Territorio Real → ★ Vegetación Real (24 plantas)
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class GeneradorVegetacionReal
{
    const string FLORA_DIR = "Assets/Models/Flora";
    const string GRASS_DIR = "Assets/Models/Flora/Grass3D";

    static Terrain _terrain;
    static List<GameObject> _modelosArbol;     // árboles grandes
    static List<GameObject> _modelosArbusto;   // arbustos
    static List<GameObject> _modelosHierba;    // grass

    // MENÚ-LEGACY: [MenuItem("Altsasu GTA/Territorio Real/★ Vegetación Real (24 plantas)", false, 13)]
    public static void Generar()
    {
        _terrain = Object.FindFirstObjectByType<Terrain>();
        if (_terrain == null) { EditorUtility.DisplayDialog("Sin terrain", "Crea terrain primero.", "OK"); return; }

        CargarModelos();
        if (_modelosArbol.Count + _modelosArbusto.Count == 0)
        {
            EditorUtility.DisplayDialog("Sin modelos",
                "No se encontró ningún FBX en Assets/Models/Flora/.\n" +
                "Comprueba que están allí.", "OK");
            return;
        }

        var antiguo = GameObject.Find("Vegetacion_Real");
        if (antiguo != null) Undo.DestroyObjectImmediate(antiguo);

        var raiz = new GameObject("Vegetacion_Real");
        Undo.RegisterCreatedObjectUndo(raiz, "Vegetacion");

        try
        {
            int totalArboles = 0, totalArbustos = 0;

            if (GeoDataAlsasua.ZonasBosque != null)
            {
                foreach (var zona in GeoDataAlsasua.ZonasBosque)
                {
                    EditorUtility.DisplayProgressBar("Vegetación Real",
                        $"Zona {zona.Nombre}...", 0.5f);

                    float cx = zona.Centro.x + 1918f;
                    float cz = zona.Centro.z + 8570f;

                    // Densidad: ~1 árbol cada 100 m² → 1 por cada 10m radio
                    int numArboles = Mathf.RoundToInt(zona.Radio * zona.Radio * 0.01f);
                    numArboles = Mathf.Min(numArboles, 200); // límite por zona

                    var sub = new GameObject($"Bosque_{zona.Nombre}");
                    sub.transform.SetParent(raiz.transform);

                    for (int i = 0; i < numArboles; i++)
                    {
                        // Distribución pseudo-aleatoria con Perlin para zonas más densas
                        Vector2 r = Random.insideUnitCircle * zona.Radio * 0.95f;
                        float ruido = Mathf.PerlinNoise(r.x * 0.05f, r.y * 0.05f);
                        if (ruido < 0.35f) continue; // huecos naturales

                        Vector3 pos = new Vector3(cx + r.x, 0, cz + r.y);
                        float h = _terrain.SampleHeight(pos);
                        pos.y = h;

                        var modelo = _modelosArbol[Random.Range(0, _modelosArbol.Count)];
                        if (modelo == null) continue;
                        var t = (GameObject)PrefabUtility.InstantiatePrefab(modelo, sub.transform);
                        t.transform.position = pos;
                        t.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);

                        // Escala con variación
                        float esc = Random.Range(0.85f, 1.35f);
                        EscalarArbol(t, esc);
                        totalArboles++;

                        // Arbusto ocasional al lado
                        if (Random.value < 0.25f && _modelosArbusto.Count > 0)
                        {
                            var arb = _modelosArbusto[Random.Range(0, _modelosArbusto.Count)];
                            if (arb != null)
                            {
                                var ab = (GameObject)PrefabUtility.InstantiatePrefab(arb, sub.transform);
                                Vector3 off = new Vector3(Random.Range(-3f, 3f), 0, Random.Range(-3f, 3f));
                                ab.transform.position = new Vector3(pos.x + off.x, _terrain.SampleHeight(pos + off), pos.z + off.z);
                                ab.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                                EscalarArbusto(ab);
                                totalArbustos++;
                            }
                        }
                    }
                }
            }

            // Árboles a lo largo de aceras de carreteras principales
            int arbolesUrbanos = ColocarArbolesUrbanos(raiz.transform);
            totalArboles += arbolesUrbanos;

            Debug.Log($"[Vegetación] ✓ {totalArboles} árboles + {totalArbustos} arbustos.");
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        EditorUtility.DisplayDialog("✅ Vegetación real",
            $"Modelos disponibles:\n• {_modelosArbol.Count} árboles\n• {_modelosArbusto.Count} arbustos\n\n" +
            "Plantados en las 6 zonas de bosque vasco + bordes urbanos.", "OK");
    }

    // =========================================================================
    //  CARGAR MODELOS
    // =========================================================================

    static void CargarModelos()
    {
        _modelosArbol = new List<GameObject>();
        _modelosArbusto = new List<GameObject>();
        _modelosHierba = new List<GameObject>();

        if (!AssetDatabase.IsValidFolder(FLORA_DIR)) return;

        var guids = AssetDatabase.FindAssets("t:Model", new[] { FLORA_DIR });
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            string nombre = Path.GetFileNameWithoutExtension(p).ToLower();
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go == null) continue;

            if (nombre.Contains("grass") || nombre.Contains("hierba"))
                _modelosHierba.Add(go);
            else if (nombre.Contains("bush") || nombre.Contains("arbusto") ||
                     nombre.Contains("flower") || nombre.Contains("hemp") ||
                     nombre.Contains("swirl") || nombre.Contains("bamboo"))
                _modelosArbusto.Add(go);
            else
                _modelosArbol.Add(go);
        }
        Debug.Log($"[Vegetación] {_modelosArbol.Count} árboles + {_modelosArbusto.Count} arbustos + {_modelosHierba.Count} grass cargados.");
    }

    static void EscalarArbol(GameObject go, float factorBase)
    {
        var b = CalcularBounds(go);
        if (b.size.y < 0.01f) return;
        // Árboles reales 5-12m
        float alturaObj = Random.Range(5f, 12f) * factorBase;
        go.transform.localScale *= alturaObj / b.size.y;
    }

    static void EscalarArbusto(GameObject go)
    {
        var b = CalcularBounds(go);
        if (b.size.y < 0.01f) return;
        float alturaObj = Random.Range(0.6f, 1.4f);
        go.transform.localScale *= alturaObj / b.size.y;
    }

    static Bounds CalcularBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // =========================================================================
    //  ÁRBOLES URBANOS (en aceras de calles)
    // =========================================================================

    static int ColocarArbolesUrbanos(Transform padre)
    {
        if (_modelosArbol.Count == 0) return 0;

        var sub = new GameObject("Arboles_Urbanos");
        sub.transform.SetParent(padre);

        int count = 0;
        // Lugares estratégicos en el pueblo
        Vector3[] zonasUrbanas = {
            new Vector3(1880f, 0, 8550f),
            new Vector3(1950f, 0, 8550f),
            new Vector3(1900f, 0, 8650f),
            new Vector3(1850f, 0, 8500f),
            new Vector3(2000f, 0, 8500f),
        };

        foreach (var z in zonasUrbanas)
        {
            for (int i = 0; i < 5; i++)
            {
                Vector3 pos = z + new Vector3(Random.Range(-30f, 30f), 0, Random.Range(-30f, 30f));
                pos.y = _terrain.SampleHeight(pos);
                var modelo = _modelosArbol[Random.Range(0, _modelosArbol.Count)];
                if (modelo == null) continue;
                var t = (GameObject)PrefabUtility.InstantiatePrefab(modelo, sub.transform);
                t.transform.position = pos;
                t.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                EscalarArbol(t, 0.8f); // más pequeños en zona urbana
                count++;
            }
        }
        return count;
    }
}
#endif
