// Assets/Scripts/ControladorDisturbios.cs
using UnityEngine;
using System.Collections.Generic;

[AddComponentMenu("Alsasua V13/Flocking IA de Masas")]
public class ControladorDisturbios : MonoBehaviour
{
    private float crono = 0f;
    public float probDisturbio = 0.05f; // 5% de que los punks se agrupen por minuto

    void Update()
    {
        crono += Time.deltaTime;
        if (crono > 10f) // Checks cada 10 seg
        {
            crono = 0f;
            if (Random.value < probDisturbio)
            {
                IniciarDisturbioEnCalle();
            }
        }
    }

    private void IniciarDisturbioEnCalle()
    {
        // Encontrar punks estáticos
        var vitales = FindObjectsOfType<SistemaReaccionVital>();
        List<SistemaReaccionVital> punks = new List<SistemaReaccionVital>();
        
        foreach(var v in vitales)
        {
            if (v.name.Contains("Punk")) punks.Add(v);
        }

        if (punks.Count >= 3)
        {
            int pivot = Random.Range(0, punks.Count);
            Vector3 epicentro = punks[pivot].transform.position;

            // Bengala Volumétrica que señala el disturbio (Flare rojo)
            InstanciarBengala(epicentro);

            // Llamar a los demás
            for(int i=0; i < Mathf.Min(6, punks.Count); i++)
            {
                if (punks[i] == null) continue;
                Vector3 circuloLucha = epicentro + new Vector3(Mathf.Cos(i)*2f, 0, Mathf.Sin(i)*2f);
                var nav = punks[i].GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (nav != null && nav.isOnNavMesh)
                {
                    nav.SetDestination(circuloLucha);
                }
            }
            Debug.Log("[V13 Flocking] Ha comenzado un disturbio callejero en " + epicentro);
        }
    }

    private void InstanciarBengala(Vector3 pos)
    {
        GameObject bengala = new GameObject("Flare_Antifascista");
        bengala.transform.position = pos + Vector3.up * 0.2f;

        Light luz = bengala.AddComponent<Light>();
        luz.type = LightType.Point;
        luz.color = Color.red;
        luz.range = 60f;
        luz.intensity = 10f;

        ParticleSystem ps = bengala.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 60f; // Dura un minuto
        main.loop = false;
        main.startColor = new Color(1f, 0f, 0f, 0.4f); // Humo rojo
        main.startSize = new ParticleSystem.MinMaxCurve(2f, 8f);
        main.startLifetime = 4f;
        main.startSpeed = 2f;
        
        var em = ps.emission; em.rateOverTime = 30f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.radius = 0.5f; shape.angle = 20f;
        
        // El humo oscurece la zona
        Destroy(bengala, 60f);
    }
}
