// Assets/Scripts/Editor/ConfiguradorLODsNPCs.cs
// Añade LODGroup a todos los prefabs NPC de Resources/Prefabs/NPCs/.
// LOD0 (0-18m): malla completa + sombras
// LOD1 (18-35m): malla completa, sombras desactivadas
// LOD2 (35-65m): cápsula proxy (1 tri, sin sombra) — el impostor GPU toma el relevo
// Culled >65m: gestionado por ImpostoresNPCDistantes
// Menú: Tools/Alsasua/Assets/🎭 Configurar LODs de NPCs

using System.IO;
using UnityEditor;
using UnityEngine;

public static class ConfiguradorLODsNPCs
{
    const string RUTA_PREFABS = "Assets/Resources/Prefabs/NPCs";

    // Distancias LOD (fracción de la distancia de culling del LODGroup, que Unity escala por Screen Height)
    // Estos valores son los "relative height" que Unity pide: 1.0 = llena pantalla, 0.0 = culled
    // Aproximaciones para un NPC de 1.8m a distintas distancias:
    const float SCREEN_LOD0 = 0.15f;   // ~0-18m → malla completa
    const float SCREEN_LOD1 = 0.05f;   // ~18-35m → sin sombra
    const float SCREEN_LOD2 = 0.015f;  // ~35-65m → cápsula proxy

    [MenuItem("Tools/Alsasua/Assets/🎭 Configurar LODs de NPCs", priority = 7)]
    public static void Configurar()
    {
        if (!Directory.Exists(RUTA_PREFABS)) { Debug.LogError("No existe " + RUTA_PREFABS); return; }

        var capsulaMesh = CrearCapsulaMesh();
        string capsulaPath = "Assets/Resources/Prefabs/NPCs/_ProxyCapsule.mesh";
        if (!File.Exists(Path.GetFullPath(capsulaPath)))
        {
            AssetDatabase.CreateAsset(capsulaMesh, capsulaPath);
            AssetDatabase.SaveAssets();
        }
        capsulaMesh = AssetDatabase.LoadAssetAtPath<Mesh>(capsulaPath);

        var prefabPaths = Directory.GetFiles(RUTA_PREFABS, "NPC_*.prefab");
        int ok = 0;
        foreach (var path in prefabPaths)
        {
            string assetPath = path.Replace("\\", "/");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) continue;

            using var editScope = new PrefabUtility.EditPrefabContentsScope(assetPath);
            var root = editScope.prefabContentsRoot;

            if (!AplicarLOD(root, capsulaMesh))
                Debug.LogWarning($"[LOD NPC] Sin SkinnedMeshRenderer en {assetPath}");
            else
                ok++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("LODs NPC", $"✅ LODGroup añadido a {ok} prefabs NPC.", "OK");
        Debug.Log($"[LOD NPC] {ok} prefabs configurados con LOD0/LOD1/LOD2+proxy.");
    }

    static bool AplicarLOD(GameObject root, Mesh capsulaMesh)
    {
        var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (renderers.Length == 0) return false;

        // Eliminar LODGroup previo si existe
        var oldLOD = root.GetComponent<LODGroup>();
        if (oldLOD != null) Object.DestroyImmediate(oldLOD);

        // LOD2 proxy — hijo invisible hasta que el LODGroup lo activa
        var proxy = root.transform.Find("_LOD2_Proxy")?.gameObject;
        if (proxy == null)
        {
            proxy = new GameObject("_LOD2_Proxy");
            proxy.transform.SetParent(root.transform, false);
        }
        var mr = proxy.GetComponent<MeshRenderer>() ?? proxy.AddComponent<MeshRenderer>();
        var mf = proxy.GetComponent<MeshFilter>() ?? proxy.AddComponent<MeshFilter>();
        mf.sharedMesh = capsulaMesh;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.sharedMaterial = new Material(Shader.Find("HDRP/Lit") ?? Shader.Find("Standard"));
        mr.sharedMaterial.color = new Color(0.5f, 0.5f, 0.5f, 0f); // invisible por transparencia
        mr.enabled = false; // LODGroup lo activa solo

        // LOD1 — desactivar sombras en los renderers originales (se clonan las referencias)
        var renderersLOD1 = new Renderer[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            renderersLOD1[i] = renderers[i];

        // Configurar LODGroup
        var lodGroup = root.AddComponent<LODGroup>();
        lodGroup.fadeMode = LODFadeMode.SpeedTree; // cross-fade suave
        lodGroup.animateCrossFading = true;

        var lods = new LOD[]
        {
            new LOD(SCREEN_LOD0, renderers),      // LOD0: malla completa
            new LOD(SCREEN_LOD1, renderersLOD1),  // LOD1: misma malla (las sombras las controla el GobernadorRender)
            new LOD(SCREEN_LOD2, new Renderer[] { mr }), // LOD2: cápsula proxy
        };
        lodGroup.SetLODs(lods);
        lodGroup.RecalculateBounds();

        // Marcar con tag para que ImpostoresNPCDistantes los reconozca
        if (!root.CompareTag("NPC") && !root.CompareTag("Civilian") &&
            !root.CompareTag("Manifestante") && !root.CompareTag("GuardiaCivil"))
            root.tag = "Civilian";

        return true;
    }

    // Cápsula muy ligera (cilindro de 8 verts) para el proxy LOD2
    static Mesh CrearCapsulaMesh()
    {
        // Reutilizar la primitiva de Unity pero como asset separado
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        var mesh = Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
        Object.DestroyImmediate(go);
        mesh.name = "NPC_ProxyCapsule";
        return mesh;
    }
}
