// Assets/Scripts/Editor/HorneadorCiudad.cs
// ═══════════════════════════════════════════════════════════════════════════
//  HORNEADOR DE CIUDAD — Fase 1 del plan AAA (Docs/plan_render_aaa.md)
//  REDISEÑO "estilo Rockstar/RAGE": no escondemos geometría en runtime — la
//  HORNEAMOS offline en una PIRÁMIDE DE LOD por celda y dejamos que el motor la
//  swapee y la cullee él solo (LODGroup + GPU instancing + occlusion estática).
//
//  EL CAMBIO QUE LO CAMBIA TODO: pasar de "generar 77.000 GameObjects en cada Play"
//  a "fusionar la ciudad UNA vez en el editor y guardarla optimizada".
//
//  Qué produce, por CELDA (200 m, igual que SistemaZonas):
//    · LOD0 (HD)   = mallas fusionadas POR MATERIAL → 1 draw call por material/celda.
//    · LOD1 (HLOD) = la celda entera fusionada en UNA sola malla (material dominante,
//                    sombras OFF) → 1 draw call por celda a distancia (proxy lejano,
//                    el "SLOD" de RAGE). [decimación real de triángulos = follow-up
//                    con paquete de simplificación; aquí LOD1 baja draw calls + sombras.]
//    · LODGroup en la raíz de la celda → Unity muestra HD de cerca, HLOD de lejos,
//      CULL al fondo. Cero código de runtime para el swap.
//  Además: activa GPU instancing en los materiales, marca todo static
//  (batching + occluder/occludee + GI) y escribe manifest_ciudad.json + prefab/celda.
//
//  REQUISITO: la escena debe TENER la geometría del mundo ya generada en editor
//  (Tools/Alsasua → 🌍 Director de Mundo / flujo de generación). Si está vacía, avisa.
//  Reversible: menú ↩️ Deshacer Horneado (reactiva originales, borra la raíz de escena).
//
//  NO toca (denylist): terreno, árboles, agua, vehículos/NPCs/multitud, luces, cámara,
//  jugador, UI, partículas y managers — dinámicos o gestionados por otros sistemas.
// ═══════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class HorneadorCiudad
{
    const float  CELDA      = 200f;                 // m — igual que SistemaZonas.zonSize
    const string DIR_RAIZ   = "Assets/CiudadHorneada";
    const string DIR_MESH   = DIR_RAIZ + "/Meshes";
    const string DIR_CELDA  = DIR_RAIZ + "/Celdas";
    const string MANIFEST   = DIR_RAIZ + "/manifest_ciudad.json";
    const int    MIN_MALLAS = 30;                   // por debajo, la escena no tiene mundo

    // ScriptableObject con refs directas a prefabs → cargable por CargadorCiudadHorneada en runtime
    const string RESOURCES_DIR = "Assets/Resources/CiudadHorneada";
    const string SO_PATH       = RESOURCES_DIR + "/ManifestCiudadSO.asset";

    // Transiciones del LODGroup (altura relativa en pantalla). HD→HLOD→cull.
    const float LOD0_HASTA = 0.30f;   // por encima de 30% de pantalla → HD
    const float LOD1_HASTA = 0.045f;  // entre 30% y 4.5% → HLOD; por debajo → cull

    // Decimación del HLOD: agrupa vértices en células de este tamaño (metros).
    // 2.5m → ~65% reducción de tris (proxy visible a 100-400m).
    const float HLOD_CELL_SIZE = 2.5f;

    // Denylist centralizada en DenylistUtility.cs — fuente única de verdad.
    // Lección del [DIAG]: filtrar por "manager" saltaba TODA la ciudad; la denylist
    // ahora solo excluye lo que de verdad NO es geometría estática de mundo.

    [System.Serializable] struct CeldaManifest { public int cx, cz; public float centroX, centroZ; public string prefab; public int materiales; }
    [System.Serializable] struct CiudadManifest { public float celda; public int totalCeldas; public int drawCallsAprox; public List<CeldaManifest> celdas; }

    // ════════════════════════════════════════════════════════════════════════
    [MenuItem("Tools/Alsasua/Mundo/🏗️ Hornear Ciudad (LOD pyramid estilo RAGE)", priority = 30)]
    static void Hornear() => HornearNucleo(true);

    /// <summary>Bake SIN diálogos (invocado desde FLUJO_COMPLETO y AutoHornearEnPlay).</summary>
    public static void HornearAuto() => HornearNucleo(false);

    static void HornearNucleo(bool interactivo)
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) { EditorUtility.DisplayDialog("Hornear Ciudad", "No hay escena activa.", "Vale"); return; }

        // 1) Recoger MeshRenderers fusionables (estáticos-ish, legibles, fuera de denylist).
        var todos = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var fusionables = new List<MeshRenderer>(todos.Length);
        int deny = 0, noLegible = 0, sinMF = 0;
        foreach (var mr in todos)
        {
            if (mr == null || !mr.enabled) continue;
            if (EnDenylist(mr.transform)) { deny++; continue; }
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) { sinMF++; continue; }
            if (!mf.sharedMesh.isReadable) { noLegible++; continue; }
            fusionables.Add(mr);
        }

        if (fusionables.Count < MIN_MALLAS)
        {
            Debug.LogWarning($"[Horneador] Solo {fusionables.Count} mallas fusionables " +
                $"(saltadas: {deny} denylist, {noLegible} NO legibles -Read/Write off-, {sinMF} sin malla). Genera la ciudad primero.");
            if (interactivo) EditorUtility.DisplayDialog("Hornear Ciudad",
                $"Solo {fusionables.Count} mallas fusionables (saltadas: {deny} denylist, {noLegible} no legibles, {sinMF} sin malla).\n\n" +
                "Genera primero la ciudad (Play o editor) y reejecuta.", "Vale");
            return;
        }
        if (interactivo && !EditorUtility.DisplayDialog("Hornear Ciudad (estilo RAGE)",
            $"Voy a hornear {fusionables.Count} mallas en una pirámide LOD por celda de {CELDA:F0} m.\n\n" +
            "Por celda: LOD0 (HD por material) + LOD1 (HLOD 1 malla) + LODGroup + GPU instancing.\n" +
            "Crea Assets/CiudadHorneada/ (mallas+prefabs+manifest), marca static y DESACTIVA los originales (reversible).\n\n¿Continuar?",
            "Hornear", "Cancelar")) return;

        // 2) Agrupar por celda → CombineInstance por material (HD) y lista global (HLOD).
        var porCelda = new Dictionary<(int, int), Dictionary<Material, List<CombineInstance>>>();
        var originales = new List<GameObject>(fusionables.Count);
        foreach (var mr in fusionables)
        {
            var mesh = mr.GetComponent<MeshFilter>().sharedMesh;
            var mats = mr.sharedMaterials;
            Vector3 p = mr.bounds.center;
            var cell = (Mathf.FloorToInt((p.x - GeoDataAlsasua.OX) / CELDA), Mathf.FloorToInt((p.z - GeoDataAlsasua.OZ) / CELDA));
            if (!porCelda.TryGetValue(cell, out var porMat)) { porMat = new Dictionary<Material, List<CombineInstance>>(); porCelda[cell] = porMat; }

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                Material mat = (mats != null && s < mats.Length) ? mats[s] : (mats != null && mats.Length > 0 ? mats[0] : null);
                if (mat == null) continue;
                if (!porMat.TryGetValue(mat, out var lista)) { lista = new List<CombineInstance>(64); porMat[mat] = lista; }
                lista.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = mr.transform.localToWorldMatrix });
            }
            originales.Add(mr.gameObject);
        }

        Directory.CreateDirectory(DIR_MESH);
        Directory.CreateDirectory(DIR_CELDA);
        AssetDatabase.Refresh();

        var raiz = new GameObject("CiudadHorneada");
        var manifest = new CiudadManifest { celda = CELDA, celdas = new List<CeldaManifest>(porCelda.Count) };
        int drawCallsHD = 0, celdaIdx = 0;
        var instanciados = new HashSet<Material>();

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var kvCelda in porCelda)
            {
                celdaIdx++;
                var (cx, cz) = kvCelda.Key;
                if (EditorUtility.DisplayCancelableProgressBar("Hornear Ciudad",
                    $"Celda {celdaIdx}/{porCelda.Count} ({cx},{cz})…", celdaIdx / (float)porCelda.Count)) break;

                var porMat = kvCelda.Value;
                var celdaGO = new GameObject($"Celda_{cx}_{cz}");
                celdaGO.transform.SetParent(raiz.transform);

                // ── LOD0 (HD): una malla fusionada por material ──
                var hd = new GameObject("LOD0_HD"); hd.transform.SetParent(celdaGO.transform);
                var renderersHD = new List<Renderer>(porMat.Count);
                Material matDominante = null; int maxInst = -1;
                var todasInstancias = new List<CombineInstance>(256);

                foreach (var kvMat in porMat)
                {
                    var mat = kvMat.Key; var combines = kvMat.Value;
                    if (combines.Count == 0) continue;
                    if (combines.Count > maxInst) { maxInst = combines.Count; matDominante = mat; }
                    todasInstancias.AddRange(combines);

                    var m = NuevaMallaCombinada(combines, $"hd_{cx}_{cz}_{Safe(mat.name)}");
                    if (m == null) continue;
                    string mp = AssetDatabase.GenerateUniqueAssetPath($"{DIR_MESH}/hd_{cx}_{cz}_{Safe(mat.name)}.asset");
                    AssetDatabase.CreateAsset(m, mp);

                    var go = new GameObject($"HD_{Safe(mat.name)}"); go.transform.SetParent(hd.transform);
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
                    renderersHD.Add(r);
                    drawCallsHD++;

                    if (instanciados.Add(mat) && !mat.enableInstancing) { mat.enableInstancing = true; EditorUtility.SetDirty(mat); }
                }

                // ── LOD1 (HLOD): toda la celda fusionada + DECIMADA (60-80% menos tris) ────
                Renderer rHLOD = null;
                if (todasInstancias.Count > 0 && matDominante != null)
                {
                    var mhFull = NuevaMallaCombinada(todasInstancias, $"hlod_{cx}_{cz}");
                    if (mhFull != null)
                    {
                        // Decimar con vertex clustering — mantiene silueta, reduce tris
                        var mhDecimated = DecimadorMeshLOD1.Decimar(mhFull, HLOD_CELL_SIZE);
                        var mh = mhDecimated ?? mhFull;   // fallback si decimación devuelve null
                        mh.name = $"hlod_{cx}_{cz}";
                        if (mhDecimated != null) Object.DestroyImmediate(mhFull);  // no guardar la sin decimar

                        string mp = AssetDatabase.GenerateUniqueAssetPath($"{DIR_MESH}/hlod_{cx}_{cz}.asset");
                        AssetDatabase.CreateAsset(mh, mp);
                        var go = new GameObject("HLOD"); go.transform.SetParent(celdaGO.transform);
                        go.AddComponent<MeshFilter>().sharedMesh = mh;
                        rHLOD = go.AddComponent<MeshRenderer>();
                        rHLOD.sharedMaterial = matDominante;
                        rHLOD.shadowCastingMode = ShadowCastingMode.Off;
                    }
                }

                // ── LODGroup: HD → HLOD → cull ──
                var lg = celdaGO.AddComponent<LODGroup>();
                var lods = (rHLOD != null)
                    ? new[] { new LOD(LOD0_HASTA, renderersHD.ToArray()), new LOD(LOD1_HASTA, new[] { rHLOD }) }
                    : new[] { new LOD(LOD1_HASTA, renderersHD.ToArray()) };
                lg.SetLODs(lods);
                lg.RecalculateBounds();

                MarcarStaticRecursivo(celdaGO);
                string prefabPath = $"{DIR_CELDA}/Celda_{cx}_{cz}.prefab";
                PrefabUtility.SaveAsPrefabAsset(celdaGO, prefabPath);

                Vector3 c = ZonaCentro(cx, cz);
                manifest.celdas.Add(new CeldaManifest { cx = cx, cz = cz, centroX = c.x, centroZ = c.z, prefab = prefabPath, materiales = renderersHD.Count });
            }
        }
        finally { AssetDatabase.StopAssetEditing(); EditorUtility.ClearProgressBar(); }

        // 3) Manifest JSON + desactivar originales.
        manifest.totalCeldas = manifest.celdas.Count;
        manifest.drawCallsAprox = drawCallsHD;
        File.WriteAllText(MANIFEST, JsonUtility.ToJson(manifest, true));
        foreach (var go in originales) if (go != null) go.SetActive(false);

        // 4) ManifestCiudadSO en Resources/ → CargadorCiudadHorneada puede cargarlo en runtime.
        Directory.CreateDirectory(RESOURCES_DIR);
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();

        var so = AssetDatabase.LoadAssetAtPath<ManifestCiudadSO>(SO_PATH);
        bool soNuevo = (so == null);
        if (soNuevo) so = ScriptableObject.CreateInstance<ManifestCiudadSO>();
        so.celda           = CELDA;
        so.totalCeldas     = manifest.totalCeldas;
        so.drawCallsAprox  = drawCallsHD;
        var prefabRefs = new List<GameObject>(manifest.celdas.Count);
        foreach (var c in manifest.celdas)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(c.prefab);
            if (p != null) prefabRefs.Add(p);
        }
        so.prefabs = prefabRefs.ToArray();
        if (so.prefabs.Length == 0)
            Debug.LogWarning($"[Horneador] ⚠️ ManifestCiudadSO creado pero sin prefabs cargables — " +
                "verifica que los prefabs existen en Assets/CiudadHorneada/Celdas/.");
        if (soNuevo) AssetDatabase.CreateAsset(so, SO_PATH);
        else         EditorUtility.SetDirty(so);

        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);

        // Invalidar firma de bake guardada → GestorEstadoCiudad descartará deltas obsoletos
        GestorEstadoCiudad.InvalidarFirmaGuardada();

        Debug.Log($"[Horneador] ✅ Ciudad horneada (pirámide LOD). {originales.Count} renderers → " +
                  $"{manifest.totalCeldas} celdas · ~{drawCallsHD} draw calls en LOD0 (cerca), 1/celda en LOD1 (lejos). " +
                  $"GPU instancing ON en {instanciados.Count} materiales. ManifestSO: {SO_PATH}. Originales DESACTIVADOS.");
        if (interactivo) EditorUtility.DisplayDialog("Hornear Ciudad",
            $"✅ {originales.Count} renderers → {manifest.totalCeldas} celdas con LODGroup.\n" +
            $"LOD0 ≈ {drawCallsHD} draw calls de cerca; LOD1 = 1/celda de lejos; cull al fondo.\n" +
            $"Instancing ON en {instanciados.Count} materiales. Prefabs+manifest en {DIR_RAIZ}/.\n\n" +
            "Dale a Play y mira el [DIAG] → 'GPU draw' debe desplomarse.", "Genial");
    }

    [MenuItem("Tools/Alsasua/Mundo/↩️ Deshacer Horneado (reactivar originales)", priority = 31)]
    static void Deshacer()
    {
        var raiz = GameObject.Find("CiudadHorneada");
        if (raiz != null) Object.DestroyImmediate(raiz);
        int n = 0;
        foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!mr.gameObject.activeSelf && !EnDenylist(mr.transform)) { mr.gameObject.SetActive(true); n++; }
        Debug.Log($"[Horneador] Deshecho: {n} objetos reactivados, raíz CiudadHorneada eliminada. " +
                  "(Los assets en Assets/CiudadHorneada/ no se borran.)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    static Mesh NuevaMallaCombinada(List<CombineInstance> combines, string nombre)
    {
        var m = new Mesh { name = nombre, indexFormat = IndexFormat.UInt32 };
        m.CombineMeshes(combines.ToArray(), mergeSubMeshes: true, useMatrices: true);
        if (m.vertexCount == 0) { Object.DestroyImmediate(m); return null; }
        m.RecalculateBounds();
        return m;
    }

    static void MarcarStaticRecursivo(GameObject go)
    {
        var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI;
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
    }

    static bool EnDenylist(Transform t) => DenylistUtility.EnDenylist(t);

    static Vector3 ZonaCentro(int cx, int cz)
        => new Vector3(GeoDataAlsasua.OX + (cx + 0.5f) * CELDA, 0f, GeoDataAlsasua.OZ + (cz + 0.5f) * CELDA);

    static string Safe(string s)
    {
        if (string.IsNullOrEmpty(s)) return "mat";
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(" ", "_").Replace("(Instance)", "").Trim('_');
    }
}
