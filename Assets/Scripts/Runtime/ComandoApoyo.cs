// Assets/Scripts/Runtime/ComandoApoyo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  COMANDO DE APOYO — pide refuerzos del movimiento (aliados), según el APOYO.
//
//    · Tecla G → invoca un comando de aliados que combaten a tu lado.
//    · Requiere nivel de apoyo ≥ 2 (SistemaProgresion). El número de aliados
//      crece con el nivel (1 a 4). Enfriamiento de 60 s.
//    · Cada aliado busca y ataca a la policía cercana y luego te acompaña.
//
//  Si no tienes prefab de aliado, genera una cápsula provisional con AliadoApoyo.
//  Capa RUNTIME. Auto-arranque; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(92)]
public sealed class ComandoApoyo : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("ComandoApoyo");
        DontDestroyOnLoad(go);
        go.AddComponent<ComandoApoyo>();
    }

    const int   NIVEL_MIN = 2;
    const float ENFRIAMIENTO = 60f;

    float _cooldown;
    float _tAviso; string _aviso;

    void Update()
    {
        if (_cooldown > 0f) _cooldown -= Time.deltaTime;
        if (_tAviso > 0f) _tAviso -= Time.unscaledDeltaTime;

        var kb = Keyboard.current;
        if (kb == null || !kb.gKey.wasPressedThisFrame) return;

        if (SistemaProgresion.Nivel < NIVEL_MIN) { Avisar($"Necesitas nivel {NIVEL_MIN} de apoyo para pedir comando"); return; }
        if (_cooldown > 0f) { Avisar($"Comando agotado ({Mathf.CeilToInt(_cooldown)} s)"); return; }

        Invocar(Mathf.Clamp(SistemaProgresion.Nivel - 1, 1, 4));
    }

    void Invocar(int cantidad)
    {
        var jug = AltsasuCore.Jugador;
        if (jug == null) return;
        _cooldown = ENFRIAMIENTO;

        for (int i = 0; i < cantidad; i++)
        {
            float ang = (360f / cantidad) * i;
            Vector3 off = Quaternion.Euler(0, ang, 0) * Vector3.forward * 3f;
            Vector3 pos = jug.position + off + Vector3.up * 0.5f;

            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Aliado";
            go.transform.position = pos;
            go.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
            UtilMaterial.Tenir(go.GetComponent<MeshRenderer>(), new Color(0.2f, 0.5f, 0.85f));   // azul = de los tuyos
            var col = go.GetComponent<Collider>(); if (col != null) col.isTrigger = true;
            go.AddComponent<AliadoApoyo>();
        }
        Avisar($"¡Comando de apoyo! {cantidad} aliado(s) contigo");
    }

    void Avisar(string s) { _aviso = s; _tAviso = 3f; }
    void OnGUI()
    {
        if (_tAviso <= 0f) return;
        var st = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
        st.normal.textColor = Color.white;
        GUI.Box(new Rect(Screen.width * 0.5f - 200, 120, 400, 32), _aviso, st);
    }
}
