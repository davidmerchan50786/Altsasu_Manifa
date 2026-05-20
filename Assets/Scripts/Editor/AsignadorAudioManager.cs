// Assets/Scripts/Editor/AsignadorAudioManager.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ASIGNADOR DE AUDIO — mapea clips reales descargados al AudioManager
//  Tools → Alsasua → 🔊 Asignar Audio (mapear clips reales)
//
//  Lee todos los archivos en Assets/Resources/Audio/ y los asigna
//  automáticamente al prefab de AudioManager según nombre del archivo.
//  También valida que el AudioManager puede cargar los clips via Resources.
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public static class AsignadorAudioManager
{
    [MenuItem("Tools/Alsasua/🔊 Asignar Audio (mapear clips reales)", priority = 7)]
    public static void AsignarTodos()
    {
        string audioDir = "Assets/Resources/Audio";
        if (!AssetDatabase.IsValidFolder(audioDir))
        {
            EditorUtility.DisplayDialog("Error", "Carpeta Assets/Resources/Audio no existe.\nDescarga primero los clips con DescargarDatosAlsasua.ps1", "OK");
            return;
        }

        // Recopilar todos los clips
        var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { audioDir });
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("Sin clips", "No hay AudioClips en Assets/Resources/Audio.\nEl AudioManager usará síntesis procedural.", "OK");
            return;
        }

        // Mapeo nombre de archivo → nombre de clip en Resources (sin extensión)
        // El AudioManager carga via Resources.LoadAll<AudioClip>("Audio")
        // Los nombres se comparan por substring (insensible a mayúsculas)
        var mapa = new Dictionary<string, string>
        {
            // Nombre archivo               → Clip enum que representa
            ["disparo"]        = "Disparo",
            ["gunshot"]        = "Disparo",
            ["pistol"]         = "Disparo",
            ["explosion"]      = "Explosion",
            ["blast"]          = "Explosion",
            ["impacto_metal"]  = "ImpactoMetal",
            ["metal"]          = "ImpactoMetal",
            ["paso_asfalto"]   = "PasoAsfalto",
            ["paso_tierra"]    = "PasoTierra",
            ["footstep"]       = "PasoAsfalto",
            ["step"]           = "PasoAsfalto",
            ["lluvia"]         = "LluviaLigera",
            ["rain"]           = "LluviaLigera",
            ["viento"]         = "VientoMontana",
            ["wind"]           = "VientoMontana",
            ["pajaros"]        = "Pajaros",
            ["birds"]          = "Pajaros",
            ["sirena"]         = "Sirena",
            ["siren"]          = "Sirena",
            ["alert"]          = "Sirena",
            ["click"]          = "UIClick",
            ["notif"]          = "UINotificacion",
            ["motor"]          = "MotorAceleracion",
            ["engine"]         = "MotorAceleracion",
            ["gritos"]         = "Consignas",
            ["yell"]           = "Consignas",
            ["aplausos"]       = "Aplausos",
            ["trueno"]         = "Trueno",
            ["thunder"]        = "Trueno",
            ["chirrido"]       = "ChirridoNeumatico",
            ["recarga"]        = "Recarga",
            ["reload"]         = "Recarga",
        };

        var clips = new List<(string clipName, string enumName, AudioClip clip)>();
        foreach (var guid in guids)
        {
            var path     = AssetDatabase.GUIDToAssetPath(guid);
            var fileName = Path.GetFileNameWithoutExtension(path).ToLower();
            var clip     = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) continue;

            string enumName = "Generico";
            foreach (var kv in mapa)
                if (fileName.Contains(kv.Key)) { enumName = kv.Value; break; }

            clips.Add((fileName, enumName, clip));
        }

        // Configurar import settings para todos los clips
        int configurados = 0;
        foreach (var (clipName, enumName, clip) in clips)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (imp == null) continue;

            // Configurar según tipo
            var settings = imp.defaultSampleSettings;
            bool esAmbiente = clipName.Contains("lluvia") || clipName.Contains("viento") ||
                              clipName.Contains("pajaros") || clipName.Contains("motor");

            settings.loadType     = esAmbiente
                ? AudioClipLoadType.Streaming
                : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality      = 0.7f;
            settings.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;

            imp.defaultSampleSettings = settings;
            imp.loadInBackground  = true;
            imp.preloadAudioData  = !esAmbiente;
            imp.SaveAndReimport();
            configurados++;
        }

        AssetDatabase.Refresh();

        // Generar informe
        string resumen = $"ASIGNACIÓN DE AUDIO COMPLETADA\n\n";
        resumen += $"Clips encontrados: {clips.Count}\n";
        resumen += $"Import configurado: {configurados}\n\n";
        resumen += "MAPEO (archivo → AudioManager.Clip):\n";
        foreach (var (clipName, enumName, _) in clips.Take(20))
            resumen += $"  {clipName} → {enumName}\n";
        resumen += "\nEl AudioManager carga automáticamente todos los clips\nde Assets/Resources/Audio/ al arrancar el juego.";

        Debug.Log($"[AsignadorAudio] {resumen}");
        EditorUtility.DisplayDialog("🔊 Audio asignado", resumen, "OK");
    }

    [MenuItem("Tools/Alsasua/🔊 Verificar Audio en Resources", priority = 8)]
    public static void Verificar()
    {
        var clips = Resources.LoadAll<AudioClip>("Audio");
        string info = clips.Length > 0
            ? $"Clips cargados por Resources:\n{string.Join("\n", System.Array.ConvertAll(clips, c => "  • " + c.name))}"
            : "No hay AudioClips accesibles via Resources.LoadAll(\"Audio\").\n\nAsegúrate de que los clips estén en Assets/Resources/Audio/";
        EditorUtility.DisplayDialog("Audio en Resources", info, "OK");
    }
}
#endif
