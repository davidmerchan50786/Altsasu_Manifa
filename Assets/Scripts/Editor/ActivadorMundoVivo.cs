// Assets/Scripts/Editor/ActivadorMundoVivo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ACTIVADOR MUNDO VIVO (EXTRA) — añade a la escena los sistemas nuevos que
//  esta rama no traía: viento→vegetación, charcos, humo de fábricas, tren y
//  túneles. NO toca el tráfico, vegetación, NPCs ni clima propios del proyecto
//  (que ya están y son mejores); sólo se asegura de que exista un SistemaClima
//  para que el viento y los charcos tengan de dónde leer.
//
//  Menú:  Tools/Alsasua/✨ Añadir Mundo Vivo EXTRA
// ═══════════════════════════════════════════════════════════════════════════

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ActivadorMundoVivo
{
    [MenuItem("Tools/Alsasua/Mundo/✨ Añadir Mundo Vivo EXTRA", priority = 52)]
    public static void Activar()
    {
        // Contenedor de los sistemas nuevos
        var raiz = GameObject.Find("MundoVivoExtra") ?? new GameObject("MundoVivoExtra");
        Anadir<SistemaVientoVegetacion>(raiz);
        Anadir<SistemaCharcos>(raiz);
        Anadir<SistemaHumoFabricas>(raiz);
        Anadir<SistemaTren>(raiz);
        Anadir<SistemaTuneles>(raiz);

        // El viento y los charcos leen del clima: asegurar que hay uno en escena.
        if (Object.FindFirstObjectByType<SistemaClima>() == null)
        {
            var clima = GameObject.Find("Simulacion") ?? new GameObject("Simulacion");
            clima.AddComponent<SistemaClima>();
            Debug.Log("[MundoVivoExtra] No había SistemaClima en la escena; añadido uno.");
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log("[MundoVivoExtra] ✅ Añadidos: viento→vegetación, charcos, humo de fábricas, tren y túneles.");
        EditorUtility.DisplayDialog("Mundo Vivo EXTRA",
            "✅ Sistemas nuevos añadidos a la escena:\n\n" +
            "• Viento que mueve la vegetación\n" +
            "• Charcos / suelo mojado con lluvia\n" +
            "• Humo en las fábricas del Polígono Isasia\n" +
            "• Tren que llega, para y se va de la estación\n" +
            "• Túneles de la autovía N-1\n\n" +
            "Dale a Play. (No se ha tocado tu tráfico, vegetación ni NPCs.)", "Genial");
    }

    static void Anadir<T>(GameObject go) where T : MonoBehaviour
    {
        if (go.GetComponent<T>() == null) go.AddComponent<T>();
    }
}
