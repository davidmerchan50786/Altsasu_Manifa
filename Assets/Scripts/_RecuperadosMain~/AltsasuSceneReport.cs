// AltsasuSceneReport.cs — Assets/Scripts/Editor/AltsasuSceneReport.cs
// Ejecuta: Altsasu GTA / Scene Report / Volcar jerarquía
// Pega la salida de la consola de Unity para que pueda diagnosticar la escena.
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using System.Text;

public static class AltsasuSceneReport
{
    [MenuItem("Altsasu GTA/Scene Report/Volcar jerarquía de escena", false, 300)]
    public static void VolcarJerarquia()
    {
        var sb = new StringBuilder();
        sb.AppendLine("════════ SCENE REPORT — " + SceneManager.GetActiveScene().name + " ════════");

        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            Recorrer(root, 0, sb, 0);

        sb.AppendLine("════════ FIN REPORT ════════");
        Debug.Log(sb.ToString());
        EditorUtility.DisplayDialog("Scene Report", "Informe volcado a consola.\nCópialo y pégalo.", "OK");
    }

    static void Recorrer(GameObject go, int depth, StringBuilder sb, int count)
    {
        if (depth > 4 || count > 2000) return;
        var indent = new string(' ', depth * 2);
        var mr  = go.GetComponent<MeshRenderer>();
        var mf  = go.GetComponent<MeshFilter>();
        var cam = go.GetComponent<Camera>();

        string info = "";
        if (mr != null)  info += "[MeshRenderer]";
        if (mf != null && mf.sharedMesh != null) info += $"[Mesh:{mf.sharedMesh.vertexCount}v]";
        if (cam != null) info += "[Camera]";
        if (go.GetComponent<Light>() != null) info += "[Light]";

        sb.AppendLine($"{indent}{go.name}  active={go.activeSelf}  {info}");

        int c = 0;
        foreach (Transform child in go.transform)
        {
            Recorrer(child.gameObject, depth + 1, sb, ++c);
            if (c >= 10) { sb.AppendLine($"{indent}  ... (+{go.transform.childCount - 10} más)"); break; }
        }
    }
}
#endif
