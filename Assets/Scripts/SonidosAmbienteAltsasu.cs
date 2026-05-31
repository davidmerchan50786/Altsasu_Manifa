// Assets/Scripts/SonidosAmbienteAltsasu.cs
// Gestiona el audio de ambiente: tráfico, sirena policial, radio.
// Se activa/desactiva según el nivel de búsqueda del GameManager.

using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class SonidosAmbienteAltsasu : MonoBehaviour
{
    [Header("Clips (asignados automáticamente por el setup)")]
    public AudioClip clipTrafico;
    public AudioClip clipSirena;
    public AudioClip clipRadio;
    public AudioClip clipFuego;

    [Header("Volúmenes")]
    [Range(0f, 1f)] public float volTrafico = 0.25f;
    [Range(0f, 1f)] public float volSirena  = 0.70f;
    [Range(0f, 1f)] public float volRadio   = 0.40f;

    AudioSource _srcTrafico;
    AudioSource _srcSirena;
    AudioSource _srcRadio;

    int _nivelAnterior = -1;

    void Start()
    {
        // Guard: evitar crear fuentes de audio múltiples si Start() se llama varias veces
        if (_srcTrafico != null) return;

        _srcTrafico = CrearFuente("Trafico",  clipTrafico, volTrafico, loop: true);
        _srcSirena  = CrearFuente("Sirena",   clipSirena,  0f,        loop: true);
        _srcRadio   = CrearFuente("Radio",    clipRadio,   0f,        loop: true);
    }

    void Update()
    {
        var gm = GameManagerAltsasua.Instance;
        if (gm == null) return;

        int nivel = gm.nivelBusqueda;
        if (nivel == _nivelAnterior) return;
        _nivelAnterior = nivel;

        // Tráfico: siempre audible, baja cuando hay sirena
        if (_srcTrafico != null)
            _srcTrafico.volume = nivel >= 2 ? volTrafico * 0.4f : volTrafico;

        // Sirena: aparece con nivel 1+, más alta con más estrellas
        if (_srcSirena != null)
        {
            float target = nivel > 0 ? volSirena * (0.4f + nivel * 0.12f) : 0f;
            _srcSirena.volume = Mathf.Clamp01(target);
            if (nivel > 0 && !_srcSirena.isPlaying) _srcSirena.Play();
            else if (nivel == 0) _srcSirena.Stop();
        }

        // Radio policial: nivel 2+
        if (_srcRadio != null)
        {
            _srcRadio.volume = nivel >= 2 ? volRadio : 0f;
            if (nivel >= 2 && !_srcRadio.isPlaying) _srcRadio.Play();
            else if (nivel < 2) _srcRadio.Stop();
        }
    }

    AudioSource CrearFuente(string nombre, AudioClip clip, float vol, bool loop)
    {
        if (clip == null) return null;
        var go = new GameObject($"Audio_{nombre}");
        go.transform.SetParent(transform);
        var src = go.AddComponent<AudioSource>();
        src.clip        = clip;
        src.volume      = vol;
        src.loop        = loop;
        src.spatialBlend = 0f; // 2D
        src.playOnAwake = vol > 0f;
        if (vol > 0f) src.Play();
        return src;
    }

    /// Llamado desde AltsasuCore cuando cambia el nivel de búsqueda
    public void ActualizarSegunNivel(int nivel) => _nivelAnterior = nivel - 1; // forzar update en próximo frame

    // Llamar desde explosiones, incendios
    public void ReproducirFuego(Vector3 posicion)
    {
        if (clipFuego != null)
            AudioSource.PlayClipAtPoint(clipFuego, posicion, 0.8f);
    }
}
