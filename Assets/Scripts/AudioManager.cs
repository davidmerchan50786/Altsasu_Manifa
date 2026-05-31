// Assets/Scripts/AudioManager.cs
// Gestor de audio centralizado. Stub compatible con ControladorJugador y SistemaDisparo.

using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager I { get; private set; }

    public enum Clip
    {
        Disparo, Recarga, PasoNormal, PasoCorrer,
        ImpactoSuelo, ImpactoSangre, ImpactoMetal,
        Explosion, Sirena, Ambiente
    }

    [System.Serializable]
    public struct ClipEntry { public Clip tipo; public AudioClip clip; }
    public ClipEntry[] clips;

    AudioSource _src;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        _src = gameObject.AddComponent<AudioSource>();
        _src.spatialBlend = 0f;
    }

    public void Play(Clip tipo, Vector3 pos = default)
    {
        if (clips == null) return;
        foreach (var e in clips)
        {
            if (e.tipo == tipo && e.clip != null)
            {
                if (pos == default || pos == Vector3.zero)
                    _src.PlayOneShot(e.clip);
                else
                    AudioSource.PlayClipAtPoint(e.clip, pos);
                return;
            }
        }
    }
}
