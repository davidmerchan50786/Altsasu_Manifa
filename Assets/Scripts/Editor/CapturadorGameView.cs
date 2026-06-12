// Assets/Scripts/Editor/CapturadorGameView.cs
// Tools → Alsasua → 📸 Capturar Game View  (o tecla F12)
// Guarda un PNG de la ventana Game en <proyecto>/Screenshots/
// Funciona en Play Mode y en Edit Mode (fuerza un repaint de la Game View).

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

public static class CapturadorGameView
{
    [MenuItem("Tools/Alsasua/📸 Capturar Game View _F12")]
    public static void Capturar()
    {
        string dir = Path.Combine(
            Path.GetDirectoryName(Application.dataPath), "Screenshots");
        Directory.CreateDirectory(dir);
        string ruta = Path.Combine(
            dir, $"game_{System.DateTime.Now:yyyyMMdd_HHmmss}.png");

        // Asegurar que la Game View existe y repinta (necesario en Edit Mode)
        var tipoGameView = System.Type.GetType("UnityEditor.GameView,UnityEditor");
        if (tipoGameView != null)
        {
            var gv = EditorWindow.GetWindow(tipoGameView, false, null, false);
            gv?.Repaint();
        }

        // Se escribe al terminar de renderizar el siguiente frame
        ScreenCapture.CaptureScreenshot(ruta, 1);
        Debug.Log($"[Captura] 📸 Game View → {ruta}");
    }
}
#endif
