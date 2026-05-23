// Assets/Scripts/Editor/InicializadorProyecto.cs
// ═══════════════════════════════════════════════════════════════════════════
//  INICIALIZADOR — se ejecuta automáticamente al compilar en Unity
//
//  Al primer arranque (o cuando faltan datos críticos):
//    1. Detecta qué falta
//    2. Ofrece ejecutar la descarga PowerShell
//    3. Ofrece construir el proyecto completo
//    4. Guarda flag para no volver a preguntar
// ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

[InitializeOnLoad]
public static class InicializadorProyecto
{
    const string KEY_INIT = "AltsasuaInit_v3";

    static InicializadorProyecto()
    {
        EditorApplication.delayCall += ComprobarEstado;
    }

    static void ComprobarEstado()
    {
        // Solo una vez por sesión
        if (SessionState.GetBool(KEY_INIT, false)) return;
        SessionState.SetBool(KEY_INIT, true);

        bool faltanDatos    = !File.Exists(Application.dataPath + "/AlsasuaData/buildings_unity.json");
        bool faltaEscena    = !File.Exists(Application.dataPath + "/../Assets/#Scenes/Alsasua_Main.unity");
        bool faltaPrefabCar = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Coches/Interceptor_Jugador.prefab") == null;

        if (!faltanDatos && !faltaEscena && !faltaPrefabCar) return; // todo OK

        string msg = "⚠  ALTSASUA SIMULATOR — Proyecto incompleto\n\n";
        if (faltanDatos)    msg += "❌ Datos OSM (edificios/carreteras) no descargados\n";
        if (faltaEscena)    msg += "❌ Escena principal (Alsasua_Main.unity) no creada\n";
        if (faltaPrefabCar) msg += "❌ Prefab Interceptor_Jugador no creado\n";
        msg += "\n¿Qué quieres hacer?";

        int opcion = EditorUtility.DisplayDialogComplex(
            "Altsasua Simulator — Setup inicial",
            msg,
            "🏗 Construir TODO ahora",
            "Ignorar (haré manualmente)",
            "📥 Solo descargar datos OSM");

        switch (opcion)
        {
            case 0: // Construir todo
                if (faltanDatos) EjecutarDescargaPS();
                EditorApplication.delayCall += FLUJO_COMPLETO.Ejecutar;
                break;
            case 2: // Solo descargar OSM
                EjecutarDescargaPS();
                break;
        }
    }

    static void EjecutarDescargaPS()
    {
        string psScript = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "DescargarDatosAlsasua.ps1"));

        if (!File.Exists(psScript))
        { Debug.LogWarning("[Init] No se encontró DescargarDatosAlsasua.ps1"); return; }

        try
        {
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName        = "powershell.exe",
                Arguments       = $"-ExecutionPolicy Bypass -File \"{psScript}\"",
                UseShellExecute = true,   // ventana visible para ver el progreso
                CreateNoWindow  = false
            };
            var proc = System.Diagnostics.Process.Start(info);
            Debug.Log("[Init] Descarga iniciada en PowerShell. Espera a que termine y luego refresca Unity (Ctrl+R).");

            // Monitorear en background
            EditorApplication.update += () =>
            {
                if (proc != null && proc.HasExited)
                {
                    AssetDatabase.Refresh();
                    Debug.Log("[Init] ✅ Descarga completada — Unity refrescado");
                    proc = null;
                }
            };
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Init] No se pudo ejecutar PowerShell: {e.Message}\n" +
                           $"Ejecuta manualmente: {psScript}");
        }
    }

    // [DESACTIVADO] [MenuItem("Tools/Alsasua/🔄 Comprobar y reparar proyecto", priority = 2)]
    public static void ComprobarYReparar()
    {
        SessionState.SetBool(KEY_INIT, false);
        ComprobarEstado();
    }
}
#endif
