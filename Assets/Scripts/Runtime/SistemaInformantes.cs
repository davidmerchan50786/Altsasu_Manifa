// Assets/Scripts/Runtime/SistemaInformantes.cs
// ═══════════════════════════════════════════════════════════════════════════
//  RED DE INFORMANTES — con apoyo alto, el pueblo es TUS ojos (anti-chivatos).
//
//  A partir de nivel de apoyo ≥ 3 (SistemaProgresion), la gente te avisa de las
//  patrullas: aparece un aviso con la DISTANCIA y la DIRECCIÓN del agente más
//  cercano que te puede ver. El rango de aviso crece con el nivel. Es el reverso
//  de los chivatos: cuanto más te quiere el pueblo, más te cubre.
//
//  Capa RUNTIME. Auto-arranque; sin montaje en escena.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;

[DefaultExecutionOrder(119)]
public sealed class SistemaInformantes : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("SistemaInformantes");
        DontDestroyOnLoad(go);
        go.AddComponent<SistemaInformantes>();
    }

    const int   NIVEL_MIN = 3;
    const float INTERVALO = 0.5f;

    float _t;
    string _aviso; Vector3 _dirAviso;

    void Update()
    {
        _t -= Time.deltaTime;
        if (_t > 0f) return;
        _t = INTERVALO;
        _aviso = null;

        if (SistemaProgresion.Nivel < NIVEL_MIN) return;
        var jug = AltsasuCore.Jugador;
        if (jug == null) return;

        float rango = 35f + SistemaProgresion.Nivel * 12f;   // más apoyo, más lejos te avisan
        PoliciaForalIA cerca = null; float min = rango;
        foreach (var p in FindObjectsOfType<PoliciaForalIA>())
        {
            if (p == null || p.EstaMuerto) continue;
            float d = Vector3.Distance(jug.position, p.transform.position);
            if (d < min) { min = d; cerca = p; }
        }
        if (cerca == null) return;

        Vector3 v = cerca.transform.position - jug.position; v.y = 0f;
        _dirAviso = v.normalized;
        _aviso = $"Te avisan: agente a {Mathf.RoundToInt(min)} m al {Cardinal(v)}";
    }

    static string Cardinal(Vector3 v)
    {
        float ang = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;   // 0=N, 90=E
        if (ang < 0) ang += 360f;
        string[] dirs = { "N", "NE", "E", "SE", "S", "SO", "O", "NO" };
        return dirs[Mathf.RoundToInt(ang / 45f) % 8];
    }

    void OnGUI()
    {
        if (string.IsNullOrEmpty(_aviso)) return;
        var st = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
        st.normal.textColor = new Color(1f, 0.8f, 0.4f);
        GUI.Box(new Rect(Screen.width - 320, Screen.height - 130, 300, 30), "⚠ " + _aviso, st);
    }
}
