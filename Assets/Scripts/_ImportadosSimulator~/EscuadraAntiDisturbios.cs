// Assets/Scripts/EscuadraAntiDisturbios.cs
using UnityEngine;
using UnityEngine.AI;

[AddComponentMenu("Alsasua V13/Ley Marcial - Nivel de Búsqueda")]
public class EscuadraAntiDisturbios : MonoBehaviour
{
    public static EscuadraAntiDisturbios Instancia;
    private int nivelBusqueda = 0; // GTA Wanted Level
    private float cooldownDespliegue = 0f;

    void Awake() { Instancia = this; }

    public void ReportarAtentado(Vector3 epicentro)
    {
        nivelBusqueda++;
        if (Time.time > cooldownDespliegue)
        {
            DesplegarFurgon(epicentro);
            cooldownDespliegue = Time.time + 15f; // Solo 1 furgón cada 15 segundos
        }
    }

    private void DesplegarFurgon(Vector3 destino)
    {
        // 1. Encontrar borde de la ciudad asumiendo (0,0,0) centro, radio 300m
        Vector2 circulo = Random.insideUnitCircle.normalized * 300f;
        Vector3 origen = new Vector3(circulo.x, 500f, circulo.y);

        // Raycast para bajar a la carretera
        if (Physics.Raycast(origen, Vector3.down, out RaycastHit hitFloor, 1000f))
        {
            origen = hitFloor.point;
        }

        // 2. Ensamblar Furgón Blindado (Mesh Swapping Code reusado)
        GameObject furgon = GameObject.CreatePrimitive(PrimitiveType.Cube);
        furgon.name = "Furgoneta_AntiDisturbios_Ertzaintza";
        furgon.transform.position = origen + Vector3.up * 2f;
        furgon.transform.localScale = new Vector3(2.5f, 3f, 6f);
        furgon.GetComponent<Renderer>().material.color = new Color(0.1f, 0.1f, 0.4f); // Azul oscuro
        
        // V24 FIX: Autodestrucción paramétrica para purgar Fuga Masiva O(N) de Memoria (480 furgones / 2 horas)
        Destroy(furgon, 90f);
        
        // 3. Luces Rotativas Procedurales (Sirenas V13 Volumétricas)
        var luces = furgon.AddComponent<SirenaPolicialPolimetrica>();

        // 4. IA de Conducción NavMesh a Zona Cero
        var nav = furgon.AddComponent<NavMeshAgent>();
        nav.speed = 25f; // Muy veloces
        nav.acceleration = 15f;
        nav.angularSpeed = 120f;
        nav.radius = 2f;
        nav.height = 3f;
        nav.avoidancePriority = 10;

        // Fijar destino
        if (NavMesh.SamplePosition(destino, out NavMeshHit hitDest, 50f, NavMesh.AllAreas))
        {
            nav.SetDestination(hitDest.position);
        }

        // 5. Audio Doppler Sirena aúlla en la lejanía
        AudioSource src = furgon.AddComponent<AudioSource>();
        src.spatialBlend = 1f;
        src.minDistance = 20f;
        src.maxDistance = 600f;
        src.dopplerLevel = 2f; // Efecto Doppler realista The Division
        // Usamos la sirena del AudioManager global (PoliceSiren)
        src.clip = Resources.Load<AudioClip>("PoliceSiren"); 
        if(src.clip != null) src.Play();
    }
}

public class SirenaPolicialPolimetrica : MonoBehaviour
{
    private Light luzRoja;
    private Light luzAzul;
    private float tiempo;

    void Start()
    {
        luzRoja = CrearLuz(Color.red, Vector3.left * 1.2f + Vector3.up * 2f);
        luzAzul = CrearLuz(Color.blue, Vector3.right * 1.2f + Vector3.up * 2f);
    }

    Light CrearLuz(Color c, Vector3 offset)
    {
        GameObject go = new GameObject("Rotativo_" + c.ToString());
        go.transform.SetParent(transform);
        go.transform.localPosition = offset;
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = c;
        l.range = 30f;
        l.intensity = 8f;
        l.renderMode = LightRenderMode.ForcePixel; // Maxima calidad volumétrica
        return l;
    }

    void Update()
    {
        tiempo += Time.deltaTime * 15f;
        // Estroboscópico de policía realista
        luzRoja.intensity = Mathf.PingPong(tiempo, 1f) > 0.5f ? 15f : 0f;
        luzAzul.intensity = Mathf.PingPong(tiempo + 0.5f, 1f) > 0.5f ? 15f : 0f;
    }
}
