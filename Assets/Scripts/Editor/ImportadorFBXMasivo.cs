// Assets/Scripts/Editor/ImportadorFBXMasivo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  IMPORTADOR MASIVO DE FBX — configura los 335 FBX de _ExtractedAssets
//  Tools → Alsasua → 📦 Importar y configurar todos los FBX
//
//  Para cada FBX/OBJ/GLB:
//    • Configura ModelImporter (escala, rotación, normales)
//    • Si es personaje → Humanoid, si no → Generic
//    • Crea prefab en Assets/Prefabs/FBX/{categoria}/
//    • Asigna materiales HDRP si existen texturas con mismo nombre
//    • Genera icono de preview
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public static class ImportadorFBXMasivo
{
    [MenuItem("Tools/Alsasua/📦 Importar y configurar todos los FBX", priority = 5)]
    public static void ImportarTodos()
    {
        var guids = AssetDatabase.FindAssets("t:Model", new[]{"Assets/_ExtractedAssets"});
        int total = guids.Length;

        Debug.Log($"[ImportadorFBX] Encontrados {total} modelos en _ExtractedAssets");

        int procesados = 0;
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            EditorUtility.DisplayProgressBar("Importando FBX...",
                $"{procesados}/{total}: {Path.GetFileName(path)}",
                (float)procesados / total);

            try { ProcesarModelo(path); }
            catch (System.Exception e) { Debug.LogWarning($"[ImportadorFBX] {path}: {e.Message}"); }

            procesados++;
            if (procesados % 25 == 0) AssetDatabase.Refresh();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.ClearProgressBar();

        int prefabs = AssetDatabase.FindAssets("t:Prefab", new[]{"Assets/Prefabs/FBX"}).Length;
        EditorUtility.DisplayDialog("✅ Importación completada",
            $"Procesados: {procesados} modelos\nPrefabs creados: {prefabs}\n\n" +
            "Los prefabs están en Assets/Prefabs/FBX/{categoria}/", "OK");
    }

    static void ProcesarModelo(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) return;

        string nombre = Path.GetFileNameWithoutExtension(path).ToLower();
        bool esPersonaje = EsPersonaje(nombre);
        bool esVehiculo  = EsVehiculo(nombre);
        bool esEdificio  = EsEdificio(nombre);

        // ── Configurar ModelImporter ──────────────────────────────────
        importer.globalScale = 1f;
        importer.importBlendShapes = true;
        importer.importCameras  = false;
        importer.importLights   = false;
        importer.importVisibility = false;
        importer.generateSecondaryUV = true; // para lightmapping

        // Normales
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;

        // Rig
        if (esPersonaje)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false;
        }
        else
        {
            importer.animationType = ModelImporterAnimationType.Generic;
        }

        // Materiales
        importer.materialImportMode  = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        importer.materialLocation    = ModelImporterMaterialLocation.InPrefab;
        importer.materialSearch      = ModelImporterMaterialSearch.Everywhere;

        importer.SaveAndReimport();

        // ── Crear prefab ──────────────────────────────────────────────
        string cat = ClasificarModelo(nombre);
        string destFolder = $"Assets/Prefabs/FBX/{cat}";
        if (!AssetDatabase.IsValidFolder(destFolder))
        {
            string parent = "Assets/Prefabs/FBX";
            if (!AssetDatabase.IsValidFolder(parent)) AssetDatabase.CreateFolder("Assets/Prefabs","FBX");
            AssetDatabase.CreateFolder(parent, cat);
        }

        string prefabRuta = $"{destFolder}/{Path.GetFileNameWithoutExtension(path)}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabRuta) != null) return;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null) return;

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        if (instance == null) return;

        // Añadir componentes según categoría
        if (esVehiculo && instance.GetComponent<Rigidbody>() == null)
        {
            instance.AddComponent<Rigidbody>().mass = 800f;
            instance.AddComponent<VehiculoNPC>();
        }

        // LOD automático
        var renderers = instance.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length > 0 && instance.GetComponent<LODGroup>() == null)
        {
            var lod = instance.AddComponent<LODGroup>();
            lod.SetLODs(new LOD[]
            {
                new LOD(0.15f, renderers.ToArray()),
                new LOD(0.03f, new Renderer[0])
            });
            lod.RecalculateBounds();
        }

        // Static flags para edificios
        if (esEdificio)
            GameObjectUtility.SetStaticEditorFlags(instance,
                StaticEditorFlags.ContributeGI |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic);

        // Asignar materiales HDRP si hay texturas con el mismo nombre base
        AsignarMaterialesHDRP(instance, nombre);

        PrefabUtility.SaveAsPrefabAsset(instance, prefabRuta);
        Object.DestroyImmediate(instance);
    }

    static void AsignarMaterialesHDRP(GameObject go, string nombreBase)
    {
        // Buscar material HDRP con nombre similar
        var matGuids = AssetDatabase.FindAssets($"t:Material {nombreBase}", new[]{"Assets/Materials"});
        if (matGuids.Length == 0)
        {
            // Buscar por palabras clave del nombre
            string keyword = nombreBase.Split('_')[0];
            matGuids = AssetDatabase.FindAssets($"t:Material {keyword}", new[]{"Assets/Materials"});
        }
        if (matGuids.Length == 0) return;

        var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(matGuids[0]));
        if (mat == null || !mat.shader.name.Contains("HDRP")) return;

        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] == null || mats[i].shader.name.Contains("Standard"))
                    mats[i] = mat;
            r.sharedMaterials = mats;
        }
    }

    // ── Clasificación ─────────────────────────────────────────────────

    static bool EsPersonaje(string n) =>
        n.Contains("human") || n.Contains("person") || n.Contains("guardia") ||
        n.Contains("soldier") || n.Contains("character") || n.Contains("man") ||
        n.Contains("woman") || n.Contains("civil");

    static bool EsVehiculo(string n) =>
        n.Contains("car") || n.Contains("vehicle") || n.Contains("truck") ||
        n.Contains("van") || n.Contains("bus") || n.Contains("moto");

    static bool EsEdificio(string n) =>
        n.Contains("house") || n.Contains("building") || n.Contains("wall") ||
        n.Contains("modular") || n.Contains("facade") || n.Contains("roof");

    static string ClasificarModelo(string nombre)
    {
        if (EsPersonaje(nombre)) return "Personajes";
        if (EsVehiculo(nombre))  return "Vehiculos";
        if (EsEdificio(nombre))  return "Edificios";
        if (nombre.Contains("tree") || nombre.Contains("bush") || nombre.Contains("plant") ||
            nombre.Contains("grass") || nombre.Contains("oak") || nombre.Contains("hedge"))
            return "Naturaleza";
        if (nombre.Contains("chicken") || nombre.Contains("horse") || nombre.Contains("rabbit") ||
            nombre.Contains("deer") || nombre.Contains("wolf") || nombre.Contains("dog") ||
            nombre.Contains("sheep") || nombre.Contains("crow") || nombre.Contains("rat"))
            return "Animales";
        return "Mundo";
    }
}
#endif
