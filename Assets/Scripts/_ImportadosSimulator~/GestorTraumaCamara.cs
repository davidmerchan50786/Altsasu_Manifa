// Assets/Scripts/GestorTraumaCamara.cs
using UnityEngine;

/// <summary>
/// Módulo Desacoplado de Trauma de Cámara (V15).
/// Responsabilidad Única: Calcular la distancia de impacto y aplicar vectores aleatorios Perlin Shake a la Main Camera.
/// </summary>
public static class GestorTraumaCamara
{
    public static void EvaluarTrauma(Vector3 epicentro, float radioAfectacion)
    {
        var jugador = Object.FindFirstObjectByType<ControladorJugador>();
        if (jugador == null) return;

        float dist = Vector3.Distance(epicentro, jugador.transform.position);
        if (dist > radioAfectacion * 4f) return;

        float intensidad = 1f - dist / (radioAfectacion * 4f);

        Camera camJugador = jugador.CamaraTP;
        if (camJugador == null) return;

        var sacudida = camJugador.GetComponent<SacudidaCamara>();
        if (sacudida == null) sacudida = camJugador.gameObject.AddComponent<SacudidaCamara>();
        
        sacudida.Sacudir(intensidad * 0.5f, 0.6f);
    }
}

public class SacudidaCamara : MonoBehaviour
{
    private float intensidad, duracion, timer;
    private Vector3 posOriginal;

    public void Sacudir(float intens, float dur)
    {
        if (timer <= 0f) posOriginal = transform.localPosition;
        
        intensidad = Mathf.Max(intensidad, intens);
        duracion   = Mathf.Max(duracion, dur);
        timer      = Mathf.Max(timer, dur);
    }

    private void Update()
    {
        if (timer <= 0) return;

        timer -= Time.deltaTime;
        float t = timer / duracion;
        transform.localPosition = posOriginal + Random.insideUnitSphere * intensidad * t;

        if (timer <= 0) transform.localPosition = posOriginal;
    }
}
