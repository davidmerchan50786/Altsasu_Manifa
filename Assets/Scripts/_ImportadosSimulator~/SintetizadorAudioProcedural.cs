// Assets/Scripts/SintetizadorAudioProcedural.cs
using UnityEngine;

[AddComponentMenu("Alsasua V13/Motor de Audio Sintético")]
public static class SintetizadorAudioProcedural
{
    public static void PlayGunshot(Vector3 pos)
    {
        int sampleRate = 44100;
        int length = sampleRate / 2; // 0.5 segundos
        float[] samples = new float[length];
        
        for (int i = 0; i < length; i++)
        {
            // Decaimiento exponencial radical para simular disparo seco
            float envelope = Mathf.Exp(-i / (sampleRate * 0.04f)); 
            // Añadir thump de baja frecuencia + ruido blanco metálico
            float thump = Mathf.Sin(i * 0.02f) * envelope * 0.5f;
            float noise = Random.Range(-1f, 1f) * envelope * 0.5f;
            samples[i] = thump + noise;
        }

        EjecutarBuffer("SynthGunshot", samples, sampleRate, pos, 0.8f);
    }

    public static void PlayCristalRoto(Vector3 pos)
    {
        int sampleRate = 44100;
        int length = sampleRate; // 1 segundo
        float[] samples = new float[length];
        
        for (int i = 0; i < length; i++)
        {
            // Múltiples picos y decaimientos (pedazos de cristal cayendo)
            float t = (float)i / sampleRate;
            float env = Mathf.Exp(-t * 8f) + Mathf.Exp(-Mathf.Abs(t - 0.2f) * 15f) * 0.5f + Mathf.Exp(-Mathf.Abs(t - 0.4f) * 20f) * 0.3f;
            
            // Frecuencias extremadamente altas para tintineo de vidrio
            float glassTone = Mathf.Sin(t * 8000f) + Mathf.Sin(t * 12000f) * 0.5f;
            float snap = Random.Range(-1f, 1f) * 0.2f; // Fractura
            
            samples[i] = (glassTone + snap) * env;
        }

        EjecutarBuffer("SynthGlass", samples, sampleRate, pos, 1f);
    }

    private static void EjecutarBuffer(string id, float[] data, int rate, Vector3 pos, float volume)
    {
        GameObject go = new GameObject("AudioSynth_" + id);
        go.transform.position = pos;
        AudioSource src = go.AddComponent<AudioSource>();
        src.spatialBlend = 1f; // 100% 3D
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = 5f;
        src.maxDistance = 500f;
        src.volume = volume;

        AudioClip clip = AudioClip.Create(id, data.Length, 1, rate, false);
        clip.SetData(data, 0);
        src.clip = clip;
        src.Play();

        float duracion = (float)data.Length / rate + 0.1f;
        Object.Destroy(go, duracion);
        // V21 AUDIT: FIX MEMORY LEAK. Destruir el clip en diferido para liberar la RAM pura instanciada
        Object.Destroy(clip, duracion);
    }

    // V14 / V21 AUDIT: Ruido blanco profundo filtrado (Simula voces ininteligibles de 1000 personas)
    public static void PlayEstaticaTurba(AudioSource src)
    {
        int sampleRate = 44100;
        int length = sampleRate * 2; // Loop de 2 segundos de murmullo constante
        float[] samples = new float[length];
        
        for (int i = 0; i < length; i++)
        {
            // Ruido marrón de multitudes (baja frecuencia oscilante temporal)
            float murmullo = Random.Range(-1f, 1f) * 0.2f;
            float modulacionMasiva = (Mathf.Sin(i * 0.0005f) * 0.5f + 0.5f);
            samples[i] = murmullo * modulacionMasiva;
        }

        AudioClip clip = AudioClip.Create("SynthCrowd", length, 1, sampleRate, false);
        clip.SetData(samples, 0);
        src.clip = clip;
        src.loop = true;
        src.volume = 0.6f;
        src.Play();
    }
}
