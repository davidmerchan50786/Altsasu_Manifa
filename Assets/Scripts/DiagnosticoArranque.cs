// Assets/Scripts/DiagnosticoArranque.cs
// Ejecuta en Start y imprime en Console un resumen del estado de todos los sistemas.
// Adjunta este componente a cualquier GameObject de la escena (ej. GameManager).

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System.Text;

[DefaultExecutionOrder(9999)] // ejecutar DESPUÉS de que todos los sistemas inicialicen
public class DiagnosticoArranque : MonoBehaviour
{
    void Start()
    {
        var sb = new StringBuilder();
        sb.AppendLine("\n══════════════════════════════════════════════");
        sb.AppendLine("  DIAGNÓSTICO DE ARRANQUE — Altsasu Manifa");
        sb.AppendLine("══════════════════════════════════════════════");

        Check(sb, "Terreno LIDAR",        Terrain.activeTerrain != null);
        Check(sb, "Sol (Directional)",    FindAnyObjectByType<Light>() != null);
        Check(sb, "HDRP Volume",          FindAnyObjectByType<Volume>() != null);
        Check(sb, "AltsasuCore",          AltsasuCore.I != null);
        Check(sb, "GameManager",          FindAnyObjectByType<GameManagerAltsasua>() != null);
        Check(sb, "ControladorJugador",   FindAnyObjectByType<ControladorJugador>() != null);
        Check(sb, "SistemaChunks",        FindAnyObjectByType<SistemaChunks>() != null);
        Check(sb, "SistemaZonas",         FindAnyObjectByType<SistemaZonas>() != null);
        Check(sb, "SistemaManifestacion", FindAnyObjectByType<SistemaManifestacion>() != null);
        Check(sb, "SistemaClima",         FindAnyObjectByType<SistemaClima>() != null);
        Check(sb, "SistemaVidaNocturna",  FindAnyObjectByType<SistemaVidaNocturna>() != null);
        Check(sb, "AudioManager",         FindAnyObjectByType<AudioManager>() != null);
        Check(sb, "SistemaEdificiosAAA",  FindAnyObjectByType<SistemaEdificiosAAA>() != null);
        Check(sb, "MobiliarioUrbano",     FindAnyObjectByType<MobiliarioUrbano>() != null);

        // Terreno: resolución y tamaño
        var t = Terrain.activeTerrain;
        if (t != null)
        {
            var td = t.terrainData;
            sb.AppendLine($"\n  Terreno: {td.heightmapResolution}x{td.heightmapResolution} " +
                          $"| Tamaño: {td.size.x}x{td.size.z}m | Altura máx: {td.size.y:F1}m");
        }

        // Jugador: posición actual
        var jugador = FindAnyObjectByType<ControladorJugador>();
        if (jugador != null)
            sb.AppendLine($"  Jugador en: {jugador.transform.position}");

        // Cámara principal
        bool cam = Camera.main != null;
        Check(sb, "Camera.main", cam);

        // HDRP pipeline
        bool hdrp = GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset;
        Check(sb, "Pipeline HDRP activo", hdrp);

        sb.AppendLine("══════════════════════════════════════════════");
        sb.AppendLine("  Si ves ❌ busca el sistema en la Hierarchy");
        sb.AppendLine("  o copia el mensaje de error rojo al chat.");
        sb.AppendLine("══════════════════════════════════════════════");

        Debug.Log(sb.ToString());
    }

    static void Check(StringBuilder sb, string nombre, bool ok)
    {
        string icono = ok ? "✅" : "❌";
        sb.AppendLine($"  {icono} {nombre}");
        if (!ok)
            Debug.LogWarning($"[Diagnostico] FALTA: {nombre}");
    }
}
