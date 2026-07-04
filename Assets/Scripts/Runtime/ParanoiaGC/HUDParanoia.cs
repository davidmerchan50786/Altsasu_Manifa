// Assets/Scripts/_ParanoiaGC~/HUDParanoia.cs  (STAGING/DRAFT — carpeta ~ no se compila)
// ─────────────────────────────────────────────────────────────────────────────
//  Medidor de paranoia estilo fanzine (OnGUI, sin Canvas). Barra verde→ámbar→rojo
//  y aviso parpadeante "⚠ TRICORNIOS EN LA ZONA (n)" cuando se cruza el umbral.
//  Para producción, recrear en uGUI/TMP con la tipografía serigrafiada (ver
//  Docs/Narrativa/TONO_BSO_Estetica.md). Esto es un scaffold legible.
// ─────────────────────────────────────────────────────────────────────────────
using UnityEngine;

public class HUDParanoia : MonoBehaviour
{
    public float umbralAviso = 70f;
    public Vector2 margen = new Vector2(16, 16);
    public float ancho = 230f, alto = 18f;

    GUIStyle _st;

    static float ParanoiaActual()
    {
        if (SistemaParanoiaGuardiaCivil.Instance != null) return SistemaParanoiaGuardiaCivil.Instance.Paranoia;
        return SistemaApoyoPopular.Instance != null ? SistemaApoyoPopular.Instance.paranoia : 0f;
    }
    static int ConvertidosActual()
        => SistemaParanoiaGuardiaCivil.Instance != null ? SistemaParanoiaGuardiaCivil.Instance.Convertidos() : 0;

    void OnGUI()
    {
        float p = ParanoiaActual();
        int conv = ConvertidosActual();

        float x = Screen.width - ancho - margen.x, y = margen.y;
        Color col = p < umbralAviso
            ? Color.Lerp(new Color(0.3f, 0.8f, 0.3f), new Color(1f, 0.6f, 0f), p / Mathf.Max(1f, umbralAviso))
            : Color.Lerp(new Color(1f, 0.6f, 0f), new Color(0.9f, 0.1f, 0.1f), (p - umbralAviso) / Mathf.Max(1f, 100f - umbralAviso));

        // marco + fondo + relleno (look bloque fanzine)
        Caja(x - 2, y - 2, ancho + 4, alto + 4, new Color(0, 0, 0, 0.7f));
        Caja(x, y, ancho, alto, new Color(0.1f, 0.1f, 0.1f, 0.85f));
        Caja(x, y, ancho * Mathf.Clamp01(p / 100f), alto, col);

        _st ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 12 };
        GUI.color = Color.white;
        GUI.Label(new Rect(x + 4, y, ancho, alto), $"PARANOIA  {p:F0}", _st);

        if (p >= umbralAviso)
        {
            bool parpadeo = (Time.unscaledTime % 1f) < 0.5f;
            GUI.color = parpadeo ? new Color(0.95f, 0.15f, 0.15f) : Color.white;
            GUI.Label(new Rect(x - 60, y + alto + 2, ancho + 60, 20),
                $"⚠  TRICORNIOS EN LA ZONA  ({conv})", _st);
            GUI.color = Color.white;
        }
    }

    static void Caja(float x, float y, float w, float h, Color c)
    {
        var prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = prev;
    }
}
