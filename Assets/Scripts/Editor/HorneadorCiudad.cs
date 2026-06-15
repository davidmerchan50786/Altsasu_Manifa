// Assets/Scripts/Editor/HorneadorCiudad.cs
// ═══════════════════════════════════════════════════════════════════════════
//  HORNEADOR DE CIUDAD — Fase 1 del plan AAA (Docs/plan_render_aaa.md)
//
//  EL CAMBIO QUE LO CAMBIA TODO: pasar de "generar 77.000 GameObjects en cada Play"
//  a "fusionar la ciudad UNA vez en el editor y guardarla optimizada".
//
//  Qué hace: recorre las mallas estáticas de la escena activa, las agrupa por
//  CELDA (200 m, igual que SistemaZonas) y por MATERIAL, y fusiona cada grupo en
//  UNA sola malla (Mesh.CombineMeshes, UInt32) → 1 draw call por material por celda.
//  Guarda las mallas como assets y cada celda como prefab en Assets/CiudadHorneada/,
//  marca todo static (batching + occlusion + GI) y desactiva los originales.
//
//  Resultado típico esperado: decenas de miles de renderers → unos cientos.
//  Mide antes/después con los mismos contadores que el [DIAG] (UnityStats).
//
//  REQUISITO: la escena debe TENER la geometría del mundo ya generada (vía los
//  menús de generación en editor: 🌍 Director de Mundo / FLUJO_COMPLETO / etc.).
//  Si la escena está casi vacía, avisa y no hace nada.
//
//  NO fusiona (denylist): terreno, árboles, agua, vehículos/NPCs/multitud, luces,
//  cámara, jugador, UI, partículas y managers — los llevan otros sistemas o son
//  dinámicos. Tampoco mallas no legibles (Read/Write off) ni skinned.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class HorneadorCiudad
{
    const float  CELDA     = 200f;                 // m — igual que SistemaZonas.zonSize
    const string DIR_RAIZ  = "Assets/CiudadHorneada";
    const string DIR_MESH  = DIR_RAIZ + "/Meshes";
    const string DIR_CELDA = DIR_RAIZ + "/Celdas";
    const int    MIN_MALLAS = 30;                  // por debajo de esto, la escena no tiene mundo

    // Raíces/objetos que NUNCA se fusionan (substring, case-insensitive).
    static readonly string[] DENY = {
        "terrain", "terreno", "mosaico",
        "arbol", "arboles", "árbol", "vegetacion", "vegetación", "tree", "grass", "hierba",
        "agua", "water", "river", "rio", "río", "charco",
        "player", "jugador", "camera", "cámara", "camara", "cam",
        "light", "luz", "sun", "sol",
        "canvas", "hud", "ui", "eventsystem", "audio",
        "cesium", "georeference",
        "navmesh", "streamer", "sistema", "manager", "director", "diagnostic", "diagnóstico",
        "vehiculo", "vehículo", "coche", "car", "npc", "civil", "peaton", "peatón",
        "multitud", "crowd", "manifest", "particle", "particula", "partícula", "fx", "vfx",
        "ciudadhorneada", "_zonas_",
    };

    [MenuItem("Tools/Alsasua/Mundo/🏗️ Hornear Ciudad (fusionar mallas)", priority = 6)]
    static void Hornear()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            EditorUtility.DisplayDialog("Hornear Ciudad", "No hay escena activa.", "Vale");
            return;
        }

        // 1) Recoger MeshRenderers fusionables (estáticos, legibles, fuera de la denylist).
        var todos = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var fusionables = new List<MeshRenderer>(todos.Length);
        int saltadosDeny = 0, saltadosNoLegible = 0, saltadosSinMF = 0;

        foreach (var mr in todos)
        {
            if (mr == null || !mr.enabled) continue;
            if (EnDenylist(mr.transform)) { saltadosDeny++; continue; }
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) { saltadosSinMF++; continue; }
            if (!mf.sharedMesh.isReadable) { saltadosNoLegible++; continue; }
            fusionables.Add(mr);
        }

        if (fusionables.Count < MIN_MALLAS)
        {
            EditorUtility.DisplayDialog("Hornear Ciudad",
                $"Solo encontré {fusionables.Count} mallas fusionables en la escena " +
                $"(saltadas: {saltadosDeny} denylist, {saltadosNoLegible} no legibles, {saltadosSinMF} sin malla).\n\n" +
                "Genera primero la ciudad en el editor (Tools/Alsasua → 🌍 Director de Mundo o el flujo completo), " +
                "y vuelve a ejecutar este horneador.", "Vale");
            return;
        }

        if (!EditorUtility.DisplayDialog("Hornear Ciudad",
            $"Voy a fusionar {fusionables.Count} mallas por celda de {CELDA:F0} m y por material.\n\n" +
            "Esto crea Assets/CiudadHorneada/ (mallas + prefabs), marca todo static y DESACTIVA " +
            "los objetos originales en la escena. Reversible (los originales no se borran).\n\n¿Continuar?",
            "Hornear", "Cancelar"))
            return;

        // 2) Agrupar CombineInstance por (celda, material) — por SUBMALLA (multi-material correcto).
        var grupos = new Dictionary<(int cx, int cz, Material mat), List<CombineInstance>>();
        var originales = new List<GameObject>(fusionables.Count);

        foreach (var mr in fusionables)
        {
            var mf   = mr.GetComponent<MeshFilter>();
            var mesh = mf.sharedMesh;
            var mats = mr.sharedMaterials;
            Vector3 p = mr.bounds.center;
            int cx = Mathf.FloorToInt((p.x - GeoDataAlsasua.OX) / CELDA);
            int cz = Mathf.FloorToInt((p.z - GeoDataAlsasua.OZ) / CELDA);

            int subCount = mesh.subMeshCount;
            for (int s = 0; s < subCount; s++)
            {
                Material mat = (mats != null && s < mats.Length) ? mats[s] : (mats != null && mats.Length > 0 ? mats[0] : null);
                if (mat == null) continue;
                var key = (cx, cz, mat);
                if (!grupos.TryGetValue(key, out var lista)) { lista = new List<CombineInstance>(64); grupos[key] = lista; }
                lista.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = mr.transform.localToWorldMatrix });
            }
            originales.Add(mr.gameObject);
        }

        // 3) Preparar carpetas de salida.
        Directory.CreateDirectory(DIR_MESH);
        Directory.CreateDirectory(DIR_CELDA);
        AssetDatabase.Refresh();

        var raiz = new GameObject("CiudadHorneada");
        var celdaRoots = new Dictionary<(int, int), Transform>();
        int meshIdx = 0, fusionadas = 0, gruposTotal = grupos.Count;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var kv in grupos)
            {
                meshIdx++;
                if (EditorUtility.DisplayCancelableProgressBar("Hornear Ciudad",
                    $"Fusionando grupo {meshIdx}/{gruposTotal}…", meshIdx / (float)gruposTotal))
                    break;

                var (cx, cz, mat) = kv.Key;
                var combines = kv.Value;
                if (combines.Count == 0) continue;

                var combinada = new Mesh { name = $"celda_{cx}_{cz}_{SafeName(mat.name)}", indexFormat = IndexFormat.UInt32 };
                combinada.CombineMeshes(combines.ToArray(), mergeSubMeshes: true, useMatrices: true);
                if (combinada.vertexCount == 0) { Object.DestroyImmediate(combinada); continue; }
                combinada.RecalculateBounds();

                string meshPath = AssetDatabase.GenerateUniqueAssetPath($"{DIR_MESH}/celda_{cx}_{cz}_{SafeName(mat.name)}.asset");
                AssetDatabase.CreateAsset(combinada, meshPath);

                if (!celdaRoots.TryGetValue((cx, cz), out var celdaT))
                {
                    var celdaGO = new GameObject($"Celda_{cx}_{cz}");
                    celdaGO.transform.SetParent(raiz.transform);
                    celdaT = celdaGO.transform;
                    celdaRoots[(cx, cz)] = celdaT;
                }

                var go = new GameObject($"Fusion_{SafeName(mat.name)}");
                go.transform.SetParent(celdaT);
                go.AddComponent<MeshFilter>().sharedMesh = combinada;
                var mrOut = go.AddComponent<MeshRenderer>();
                mrOut.sharedMaterial = mat;
                GameObjectUtility.SetStaticEditorFlags(go,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI);
                fusionadas++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }

        // 4) Guardar cada celda como prefab y desactivar originales.
        foreach (var kv in celdaRoots)
        {
            var (cx, cz) = kv.Key;
            string prefabPath = $"{DIR_CELDA}/Celda_{cx}_{cz}.prefab";
            PrefabUtility.SaveAsPrefabAsset(kv.Value.gameObject, prefabPath);
        }
        foreach (var go in originales)
            if (go != null) go.SetActive(false);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorSceneManagerMarkDirty(scene);

        Debug.Log($"[Horneador] ✅ Ciudad horneada. " +
                  $"{originales.Count} renderers originales → {fusionadas} mallas fusionadas en {celdaRoots.Count} celdas " +
                  $"(materiales×celda). Prefabs en {DIR_CELDA}/. Originales DESACTIVADOS (no borrados).\n" +
                  $"Reducción aproximada de draw calls: {originales.Count} → ~{fusionadas}. " +
                  $"Saltados: {saltadosDeny} denylist, {saltadosNoLegible} no legibles.");

        EditorUtility.DisplayDialog("Hornear Ciudad",
            $"✅ Listo.\n\n{originales.Count} renderers → {fusionadas} mallas fusionadas ({celdaRoots.Count} celdas).\n" +
            $"Prefabs en {DIR_CELDA}/.\nLos originales quedan DESACTIVADOS (reversibles).\n\n" +
            "Dale a Play y mira el [DIAG]: 'GPU draw' debería bajar muchísimo.", "Genial");
    }

    // ── Restaura los originales y borra la ciudad horneada de la escena (deshacer) ──
    [MenuItem("Tools/Alsasua/Mundo/↩️ Deshacer Horneado (reactivar originales)", priority = 7)]
    static void Deshacer()
    {
        var raiz = GameObject.Find("CiudadHorneada");
        if (raiz != null) Object.DestroyImmediate(raiz);

        int reactivados = 0;
        foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!mr.gameObject.activeSelf && !EnDenylist(mr.transform)) { mr.gameObject.SetActive(true); reactivados++; }

        Debug.Log($"[Horneador] Deshecho: {reactivados} objetos reactivados, raíz CiudadHorneada eliminada de la escena. " +
                  "(Los assets en Assets/CiudadHorneada/ no se borran; bórralos a mano si quieres.)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    static bool EnDenylist(Transform t)
    {
        // Comprueba el nombre del objeto y de todos sus padres hasta la raíz.
        for (var cur = t; cur != null; cur = cur.parent)
        {
            string n = cur.name.ToLowerInvariant();
            foreach (var d in DENY) if (n.Contains(d)) return true;
        }
        return false;
    }

    static string SafeName(string s)
    {
        if (string.IsNullOrEmpty(s)) return "mat";
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(" ", "_").Replace("(Instance)", "").Trim('_');
    }

    static void EditorSceneManagerMarkDirty(Scene s)
        => UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(s);
}
