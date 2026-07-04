// Assets/Scripts/Editor/BatchActivadorAAA.cs
// ─────────────────────────────────────────────────────────────────────────────
//  Punto de entrada BATCHMODE para activar la deuda AAA sin abrir la UI:
//    Unity.exe -batchmode -quit -projectPath <proyecto>
//      -executeMethod BatchActivadorAAA.Activar
//  Abre la escena principal, ejecuta ActivadorAAA.FaseAssetsYEscena()
//  (assets Sintonia/ParanoiaGC + GameObjects AAA_Gameplay/AAA_ClipmapV3/
//  AAA_Impostores wireados) y guarda escena + assets.
//  Pensado para automatizacion (CI/agente); en editor interactivo usa el menu
//  Tools/Alsasua/Activar AAA.
// ─────────────────────────────────────────────────────────────────────────────
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BatchActivadorAAA
{
    const string ESCENA = "Assets/#Scenes/Alsasua_Main.unity";

    public static void Activar()
    {
        Debug.Log("[BatchActivadorAAA] Abriendo escena " + ESCENA);
        var escena = EditorSceneManager.OpenScene(ESCENA, OpenSceneMode.Single);
        if (!escena.IsValid())
        {
            Debug.LogError("[BatchActivadorAAA] No se pudo abrir la escena.");
            EditorApplication.Exit(1);
            return;
        }

        // FASE 1 (mover staged) ya esta hecha: los scripts viven en sus capas.
        // FASE 2: assets + montaje de GameObjects wireados.
        ActivadorAAA.FaseAssetsYEscena();

        EditorSceneManager.SaveScene(escena);
        AssetDatabase.SaveAssets();
        Debug.Log("[BatchActivadorAAA] ✅ Escena activada y guardada.");
    }
}
