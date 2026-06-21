// Assets/Scripts/SistemaCicloDia.cs
using UnityEngine;
using System;

[AddComponentMenu("Alsasua/Ciclo Dia Sandbox")]
public class SistemaCicloDia : MonoBehaviour
{
    [Header("Reloj Sandbox")]
    [Tooltip("Sincroniza la hora virtual con tu PC local Exacta.")]
    public bool usarHoraRealWindows = false;
    
    [Range(0f, 24f)] 
    public float horaActualVirtual = 14f;
    
    [Tooltip("Aceleradora Temporal. 60f = 1 min real son 60 min de simulación.")]
    public float multiplicadorTiempo = 240f; 

    [Header("Sistemas Afectados")]
    public Light solPrincipal;
    
    private Light[] farolasCiudad;
    private bool estadoNocturnoCiudad = false;

    private void Start()
    {
        // Autorastreo del sol para Zero-Click setup
        if (solPrincipal == null)
        {
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional)
                {
                    solPrincipal = l;
                    break;
                }
            }
        }

        if (usarHoraRealWindows)
        {
            SincronizarRelojReal();
        }
    }

    private void Update()
    {
        if (usarHoraRealWindows)
        {
            SincronizarRelojReal();
        }
        else
        {
            // Reloj Simulado Acelerado
            horaActualVirtual += (Time.deltaTime * multiplicadorTiempo) / 3600f;
            if (horaActualVirtual >= 24f) horaActualVirtual -= 24f;
        }

        ProcesarOrbitacionSolar();
        EvaluarLucesUrbanas();
    }

    private void SincronizarRelojReal()
    {
        DateTime ahora = DateTime.Now;
        horaActualVirtual = ahora.Hour + (ahora.Minute / 60f) + (ahora.Second / 3600f);
    }

    private void ProcesarOrbitacionSolar()
    {
        if (solPrincipal == null) return;

        // Transformación esférica de rotación solar (Amanecer a las 6:30, Anochecer a las 19:30)
        float ecuacionRotacionX = (horaActualVirtual - 6.5f) * 15f; 
        solPrincipal.transform.rotation = Quaternion.Euler(ecuacionRotacionX, 170f, 0f);

        // Control Fotométrico Dinámico PBR
        if (horaActualVirtual > 6.0f && horaActualVirtual < 19.5f)
        {
            // Es de día
            float senoidalDia = Mathf.Sin((horaActualVirtual - 6f) / 13.5f * Mathf.PI);
            solPrincipal.intensity = Mathf.Lerp(0.1f, 1.8f, senoidalDia);
            
            // Alteración kelvin simulada por color
            solPrincipal.color = Color.Lerp(new Color(1f, 0.5f, 0.3f), new Color(1f, 0.95f, 0.9f), senoidalDia);
            RenderSettings.ambientIntensity = Mathf.Lerp(0.2f, 1f, senoidalDia);
        }
        else
        {
            // Efecto Luna (Nocturno militar realista)
            solPrincipal.intensity = 0.05f; 
            solPrincipal.color = new Color(0.2f, 0.3f, 0.6f); // Luz de luna azulada táctica
            RenderSettings.ambientIntensity = 0.1f;
        }
    }

    private void EvaluarLucesUrbanas()
    {
        // Las farolas se encienden a las 19:00 PM y se apagan a las 7:00 AM
        bool ciudadDebeEstarOscura = (horaActualVirtual >= 19f || horaActualVirtual <= 7f);
        
        if (estadoNocturnoCiudad != ciudadDebeEstarOscura)
        {
            estadoNocturnoCiudad = ciudadDebeEstarOscura;
            DesatarOrdenEncendidoFarolas(estadoNocturnoCiudad);
        }
    }

    private void DesatarOrdenEncendidoFarolas(bool encender)
    {
        if (farolasCiudad == null)
            farolasCiudad = FindObjectsByType<Light>(FindObjectsSortMode.None);

        foreach (Light farola in farolasCiudad)
        {
            // Solo manipular PointLights que sean Farolas procedimentales (V7)
            if (farola != null && farola.type == LightType.Point && farola.gameObject.name.Contains("StreetLamp"))
            {
                farola.enabled = encender;
            }
        }
    }
}
