// Assets/Scripts/Editor/UtilOcclusionEstatica.cs
// ═══════════════════════════════════════════════════════════════════════════
//  UTILIDAD DE EDITOR — preparar geometría para Occlusion Culling
//  (Blueprint AAA+++, Pilar Rendimiento §4.4)
//
//  El occlusion culling de Unity es un BAKE de editor: requiere que la
//  geometría del mundo esté marcada con los static flags correctos ANTES de
//  hornear. En una ciudad de calles estrechas (casco histórico de Alsasua) es
//  de las optimizaciones de mayor retorno: oculta lo que no se ve tras edificios.
//
//  Esta herramienta automatiza el paso tedioso: marca los contenedores de mundo
//  como Occluder + Occludee + Batching Static de una pasada. Después, el bake
//  se hace a mano (no es automatizable de forma fiable):
//
//   1. Menú  Alsasua ▸ Occlusion ▸ Marcar geometría estática
//   2. Window ▸ Rendering ▸ Occlusion Culling
//   3. Pestaña "Bake": Smallest Occluder ≈ 5 (edificios), Smallest Hole ≈ 0.25
//   4. Botón "Bake".
//
//  Nota: tras mover/regenerar geometría hay que volver a bakear.
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class UtilOcclusionEstatica
{
    // Contenedores de mundo cuyos hijos deben ocluir/ocluirse.
    static readonly string[] CONTENEDORES_OCCLUDER =
    {
        "Edificios_OSM", "Edificios_Precisos", "EdificiosAAA",
        "Suelo", "SueloBase", "Calles", "Muros", "Tuneles",
    };

    [MenuItem("Alsasua/Occlusion/Marcar geometría estática")]
    static void MarcarEstaticos()
    {
        int objetos = 0, contenedores = 0;

        var flags = StaticEditorFlags.OccluderStatic
                  | StaticEditorFlags.OccludeeStatic
                  | StaticEditorFlags.BatchingStatic;

        foreach (var nombre in CONTENEDORES_OCCLUDER)
        {
            var go = GameObject.Find(nombre);
            if (go == null) continue;
            contenedores++;

            // El contenedor y todos sus hijos con Renderer/MeshFilter.
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                Undo.RegisterFullObjectHierarchyUndo(r.gameObject, "Occlusion static");
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags);
                objetos++;
            }
        }

        if (contenedores == 0)
        {
            EditorUtility.DisplayDialog("Occlusion",
                "No se encontró ningún contenedor de mundo (Edificios_OSM, etc.).\n" +
                "Genera el mundo primero, o ajusta CONTENEDORES_OCCLUDER.", "OK");
            return;
        }

        // Marcar la escena como modificada para que se guarde.
        if (!Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log($"[Occlusion] {objetos} objetos marcados (Occluder+Occludee+Batching) " +
                  $"en {contenedores} contenedores. Ahora: Window ▸ Rendering ▸ Occlusion Culling ▸ Bake.");
        EditorUtility.DisplayDialog("Occlusion",
            $"{objetos} objetos marcados como estáticos en {contenedores} contenedores.\n\n" +
            "Siguiente: Window ▸ Rendering ▸ Occlusion Culling ▸ Bake.", "OK");
    }

    [MenuItem("Alsasua/Occlusion/Marcar geometría estática", validate = true)]
    static bool MarcarEstaticosValida() => !Application.isPlaying;
}
#endif
