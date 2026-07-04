// Assets/Scripts/Runtime/PuenteDialogo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PUENTE DIÁLOGO → APOYO / MISIONES — cierra el círculo narrativo.
//
//  Escucha SistemaDialogo.AlEvento y traduce los eventos de las conversaciones
//  (subir_apoyo, bajar_apoyo, aliado_amaia…) en cambios reales de apoyo popular,
//  y re-emite TODOS los eventos como AlEventoNarrativo para que el sistema de
//  misiones (u otros) reaccione a las pistas/objetivos (pista_pendrive,
//  objetivo_fabrica, manu_se_queda…).
//
//  Así, una decisión en el diálogo mueve la barra de apoyo y avanza la trama
//  sin acoplar el motor de diálogo a esos sistemas.
//
//  Capa RUNTIME. Auto-arranque; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;

[DefaultExecutionOrder(96)]
public sealed class PuenteDialogo : MonoBehaviour
{
    /// <summary>Re-emite cualquier evento narrativo del diálogo para misiones/otros sistemas.</summary>
    public static event System.Action<string> AlEventoNarrativo;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("PuenteDialogo");
        DontDestroyOnLoad(go);
        go.AddComponent<PuenteDialogo>();
    }

    void OnEnable()  => SistemaDialogo.AlEvento += Enrutar;
    void OnDisable() => SistemaDialogo.AlEvento -= Enrutar;

    void Enrutar(string e)
    {
        if (string.IsNullOrEmpty(e)) return;
        var ap = SistemaApoyoPopular.Instance;

        switch (e)
        {
            case "subir_apoyo":   ap?.SumarApoyo(8f,  e); break;
            case "bajar_apoyo":   ap?.RestarApoyo(6f, e); break;
            case "aliado_amaia":  ap?.SumarApoyo(5f,  e); break;
            case "rechazo_amaia": ap?.RestarApoyo(4f, e); break;
            case "manu_se_queda": ap?.SumarApoyo(10f, e); break;
            case "manifa_pacifica":   ap?.SumarApoyo(12f, e); break;
            case "manifa_desbordada": ap?.RestarApoyo(10f, e); break;
            case "aliada_sara":       ap?.SumarApoyo(6f,  e); break;
            case "perder_aliado":     ap?.RestarApoyo(5f, e); break;
            // pista_pendrive / objetivo_fabrica / objetivo_pleno: sin efecto de
            // apoyo, pero se re-emiten abajo para que las misiones avancen.
        }

        AlEventoNarrativo?.Invoke(e);
        Debug.Log($"[PuenteDialogo] Evento narrativo enrutado: {e}");
    }
}
