// Assets/Scripts/Editor/ValidadorMisiones.cs
// Herramienta editor: valida que todos los sistemas requeridos por M00→M12
// están en escena o tienen auto-bootstrap antes de hacer Play.

using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ValidadorMisiones
{
    // sistemas requeridos: (nombre de tipo, descripción, qué misión lo necesita)
    static readonly (string tipo, string descripcion, string mision)[] REQUERIDOS = {
        ("SistemaMisiones",       "Sistema de misiones principal",         "M00+"),
        ("SistemaGrafitis",       "Graffiti y pegatinas",                  "M03, M09"),
        ("SistemaApoyoPopular",   "Apoyo popular (recompensas)",           "M03-M12"),
        // RadioAskatasuna es clase Mision, no MonoBehaviour → no necesita validación
        // ("RadioAskatasuna", "Emisora clandestina", "M06"),
        ("SistemaManifestacion",  "Manifestaciones y disturbios",          "M04, M12"),
    };

    static ValidadorMisiones()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    static void OnPlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;

        // Pequeño delay para que los sistemas auto-bootstrap primero
        EditorApplication.delayCall += () =>
        {
            int faltantes = 0;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[ValidadorMisiones] Verificando dependencias M00→M12:\n");

            foreach (var (tipo, desc, mision) in REQUERIDOS)
            {
                var t = System.Type.GetType(tipo) ??
                        System.Type.GetType(tipo + ", Assembly-CSharp");
                if (t == null) continue;

                var obj = Object.FindFirstObjectByType(t);
                if (obj == null)
                {
                    sb.AppendLine($"  ⚠️ FALTA: {tipo} ({desc}) — necesario para {mision}");
                    faltantes++;
                }
                else sb.AppendLine($"  ✅ {tipo}");
            }

            if (faltantes > 0)
            {
                sb.AppendLine($"\n{faltantes} sistemas ausentes. " +
                    "Las misiones afectadas se bloquearán silenciosamente.\n" +
                    "BootstrapMisiones.cs intentará crearlos automáticamente.");
                Debug.LogWarning(sb.ToString());
            }
            else
            {
                Debug.Log("[ValidadorMisiones] ✅ Todas las dependencias M00→M12 presentes.");
            }
        };
    }

    [MenuItem("Tools/Alsasua/Misiones/🔍 Validar Dependencias M00→M12", priority = 80)]
    static void ValidarManual()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Dependencias M00→M12:\n");
        int ok = 0, falta = 0;

        foreach (var (tipo, desc, mision) in REQUERIDOS)
        {
            var t = System.Type.GetType(tipo) ?? System.Type.GetType(tipo + ", Assembly-CSharp");
            if (t == null) { sb.AppendLine($"  ❓ {tipo} — tipo no encontrado"); continue; }

            bool hayInstancia = Object.FindFirstObjectByType(t) != null;
            bool tieneBootstrap = t.GetMethod("Bootstrap",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic) != null;

            if (hayInstancia)
            {
                sb.AppendLine($"  ✅ {tipo} — en escena");
                ok++;
            }
            else if (tieneBootstrap)
            {
                sb.AppendLine($"  ⚡ {tipo} — no en escena pero tiene auto-bootstrap");
                ok++;
            }
            else
            {
                sb.AppendLine($"  ⚠️ {tipo} ({mision}) — ausente, sin auto-bootstrap");
                falta++;
            }
        }

        sb.AppendLine($"\n{ok} OK · {falta} ausentes");
        string titulo = falta == 0 ? "✅ Misiones listas" : $"⚠️ {falta} dependencias ausentes";
        EditorUtility.DisplayDialog(titulo, sb.ToString(), "Entendido");
    }
}
