// Assets/Scripts/CristalDestructible.cs
using UnityEngine;

[AddComponentMenu("Alsasua V13/Muro de Cristal Frágil")]
public class CristalDestructible : MonoBehaviour
{
    private bool roto = false;

    // V13 Inyección desde Explosión
    public void RecibirOndaExpansiva(float dist, float radio)
    {
        if (dist <= radio && !roto)
        {
            HacerAñicos(transform.position);
        }
    }

    public void HacerAñicos(Vector3 epicentroFuerza)
    {
        if (roto) return;
        roto = true;

        SintetizadorAudioProcedural.PlayCristalRoto(transform.position);

        // V13: Simulamos que los cristales de las ventanas estallan, dejando el muro intacto
        for (int i = 0; i < 20; i++)
        {
            GameObject pedazo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pedazo.name = "Vidrio_Shatter";
            pedazo.transform.position = transform.position + new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-0.2f, 0.2f));
            pedazo.transform.localScale = new Vector3(Random.Range(0.1f, 0.4f), Random.Range(0.1f, 0.5f), 0.05f);
            
            var render = pedazo.GetComponent<Renderer>();
            render.material.color = new Color(0.8f, 0.9f, 1f, 0.4f); // Traslúcido celeste
            
            var rb = pedazo.AddComponent<Rigidbody>();
            rb.mass = 0.5f;
            rb.AddExplosionForce(Random.Range(100f, 400f), epicentroFuerza, 10f); // Salen volando

            // Auto limpieza de memoria (Culling físico)
            Destroy(pedazo, Random.Range(4f, 8f));
        }

        // NO destruimos el gameObject ya que está anclado al Edificio Base OSM
    }
}
