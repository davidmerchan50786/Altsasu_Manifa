// Assets/Scripts/Systems/DiagnosticoMundoDump.cs
// Auto-arranca en Play (solo Editor). A los 15 s vuelca a
// <proyecto>/Screenshots/diagnostico_mundo.txt:
//   · TODOS los mensajes de Console (info/warning/error) desde el arranque
//   · Estado del mundo: escena, Terrain(s), chunks/zonas, Cesium, jugador
// Sirve para diagnosticar arranques rotos sin copiar la Console a mano.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DiagnosticoMundoDump : MonoBehaviour
{
    static readonly List<string> _logs = new List<string>(1024);
    const float SEGUNDOS_HASTA_DUMP = 15f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isEditor) return;
        Application.logMessageReceived += Capturar;
        var go = new GameObject("DiagnosticoMundoDump");
        DontDestroyOnLoad(go);
        go.AddComponent<DiagnosticoMundoDump>();
    }

    static void Capturar(string msg, string stack, LogType tipo)
    {
        if (_logs.Count > 5000) return; // tope de seguridad
        string pre = tipo == LogType.Error || tipo == LogType.Exception ? "[ERROR] "
                   : tipo == LogType.Warning ? "[WARN ] " : "[info ] ";
        _logs.Add(pre + msg);
        if (tipo == LogType.Exception) _logs.Add("        " + stack.Replace("\n", "\n        "));
    }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(SEGUNDOS_HASTA_DUMP);
        Volcar();
        // refresco periódico por si el estado cambia después
        while (true) { yield return new WaitForSeconds(30f); Volcar(); }
    }

    void Volcar()
    {
        var sb = new StringBuilder(32 * 1024);
        sb.AppendLine($"═══ DIAGNÓSTICO MUNDO — {System.DateTime.Now:HH:mm:ss} (t={Time.time:F0}s) ═══");
        sb.AppendLine($"Escena activa: {SceneManager.GetActiveScene().name}");
        sb.AppendLine();

        // ── Terrains ─────────────────────────────────────────────────────────
        var terrains = FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        sb.AppendLine($"── Terrains en escena: {terrains.Length}");
        foreach (var t in terrains)
            sb.AppendLine($"   '{t.name}' activo={t.isActiveAndEnabled} pos={t.transform.position} " +
                          $"size={t.terrainData?.size} (activeTerrain={(Terrain.activeTerrain == t ? "SÍ" : "no")})");

        // ── Jugador ──────────────────────────────────────────────────────────
        var jugador = GameObject.FindGameObjectWithTag("Player");
        sb.AppendLine($"── Jugador: {(jugador ? $"'{jugador.name}' pos={jugador.transform.position}" : "NO ENCONTRADO")}");
        if (jugador && Terrain.activeTerrain)
        {
            float hSuelo = Terrain.activeTerrain.SampleHeight(jugador.transform.position)
                         + Terrain.activeTerrain.transform.position.y;
            sb.AppendLine($"   altura suelo Terrain bajo jugador: {hSuelo:F1} (jugador Y={jugador.transform.position.y:F1})");
        }

        // ── Sistemas de mundo (por nombre, sin acoplar) ──────────────────────
        sb.AppendLine("── Sistemas de mundo:");
        foreach (var nombre in new[] { "SistemaChunks", "SistemaZonas", "GeneradorMundoOSM",
                                       "SceneBootstrapper", "SistemaTerreno", "SistemaMisiones" })
        {
            var go = GameObject.Find(nombre);
            var comp = go == null ? EncontrarPorTipo(nombre) : go;
            sb.AppendLine($"   {nombre}: {(comp != null ? "presente" : "AUSENTE")}");
        }

        // ── Cesium ───────────────────────────────────────────────────────────
        sb.AppendLine("── Cesium:");
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mb == null) continue;
            string tn = mb.GetType().Name;
            if (tn == "CesiumGeoreference")
                sb.AppendLine($"   Georeference '{mb.name}' pos={mb.transform.position} activo={mb.isActiveAndEnabled}");
            else if (tn == "Cesium3DTileset")
                sb.AppendLine($"   Tileset '{mb.name}' activo={mb.isActiveAndEnabled}");
        }

        // ── Console completa ─────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine($"═══ CONSOLE ({_logs.Count} mensajes) ═══");
        foreach (var l in _logs) sb.AppendLine(l);

        string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Screenshots");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "diagnostico_mundo.txt"), sb.ToString());
        Debug.Log($"[Diagnóstico] 🩺 volcado → Screenshots/diagnostico_mundo.txt");
    }

    static GameObject EncontrarPorTipo(string nombreTipo)
    {
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (mb != null && mb.GetType().Name == nombreTipo) return mb.gameObject;
        return null;
    }
}
