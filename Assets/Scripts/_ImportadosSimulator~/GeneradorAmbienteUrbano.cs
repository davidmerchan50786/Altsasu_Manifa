// Assets/Scripts/GeneradorAmbienteUrbano.cs
using UnityEngine;
using System.Collections.Generic;

[AddComponentMenu("Alsasua V8/Generador de Ambiente Callejero")]
public class GeneradorAmbienteUrbano : MonoBehaviour
{
    [Header("Modelos 3D (Se autoasignan desde AutoSetup)")]
    public GameObject prefabPunk;
    public GameObject prefabPerro;
    public GameObject prefabRata;

    private void Start()
    {
        StartCoroutine(GenerarEntornoUrbanoAsincronamente());
    }

    private System.Collections.IEnumerator GenerarEntornoUrbanoAsincronamente()
    {
        yield return StartCoroutine(GenerarPunksYFaunaAsync());
        yield return StartCoroutine(GenerarCallejonesOscurosAsync()); 
    }

    private System.Collections.IEnumerator GenerarPunksYFaunaAsync()
    {
        // Generar Punks (Procedural Mohawk Assembly) con Humo (Consumo)
        for(int i = 0; i < 15; i++)
        {
            GameObject punk = prefabPunk != null ? Instantiate(prefabPunk) : EnsamblarPunkProcedural();
            punk.name = "NPC_Punk_Procedural_" + i;
            punk.transform.SetParent(this.transform); // V24 FIX: Mantener jerarquía limpia
            punk.transform.position = new Vector3(Random.Range(-200f, 200f), 550f, Random.Range(-200f, 200f));
            
            // Simulación de Humo (Sustancias)
            GameObject humo = new GameObject("Humo_Sustancia");
            humo.transform.SetParent(punk.transform);
            humo.transform.localPosition = new Vector3(0, 0.8f, 0.5f);
            ParticleSystem ps = humo.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 2f; main.startSpeed = 1f; main.startSize = 0.2f; main.startColor = new Color(0.8f, 0.8f, 0.8f, 0.5f);
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 10f;

            punk.AddComponent<GravedadCalles>();
            if (i % 5 == 0) yield return null;
        }

        // Generar Perros Callejeros
        for(int i = 0; i < 20; i++)
        {
            GameObject perro = prefabPerro != null ? Instantiate(prefabPerro) : GameObject.CreatePrimitive(PrimitiveType.Cube);
            perro.name = "Perro_Vagabundo_" + i;
            perro.transform.SetParent(this.transform); // V24 FIX
            perro.transform.localScale = new Vector3(0.4f, 0.6f, 0.8f);
            perro.transform.position = new Vector3(Random.Range(-200f, 200f), 550f, Random.Range(-200f, 200f));
            perro.GetComponent<Renderer>().material.color = new Color(0.4f, 0.3f, 0.2f); // Marrón sucio
            perro.AddComponent<GravedadCalles>();
            
            // V9: Inteligencia Artificial NavMesh en lugar de movimiento matemático
            var nav = perro.AddComponent<UnityEngine.AI.NavMeshAgent>();
            nav.speed = 3f;
            nav.radius = 0.3f;
            perro.AddComponent<NavegacionAnimalIA>();
            perro.AddComponent<SistemaReaccionVital>(); // V12
            if (i % 5 == 0) yield return null;
        }

        // Generar Ratas de Alcantarilla
        for(int i = 0; i < 40; i++)
        {
            GameObject rata = prefabRata != null ? Instantiate(prefabRata) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rata.name = "Rata_Alcantarilla_" + i;
            rata.transform.SetParent(this.transform); // V24 FIX
            rata.transform.localScale = new Vector3(0.2f, 0.2f, 0.4f);
            rata.transform.position = new Vector3(Random.Range(-200f, 200f), 550f, Random.Range(-200f, 200f));
            rata.GetComponent<Renderer>().material.color = Color.black;
            rata.AddComponent<GravedadCalles>();
            
            var nav = rata.AddComponent<UnityEngine.AI.NavMeshAgent>();
            nav.speed = 7f; // Corren rápido
            nav.radius = 0.1f;
            rata.AddComponent<NavegacionAnimalIA>();
            rata.AddComponent<SistemaReaccionVital>(); // V12
            if (i % 10 == 0) yield return null;
        }
    }

    private System.Collections.IEnumerator GenerarCallejonesOscurosAsync()
    {
        for (int i=0; i < 15; i++)
        {
            // Encontrar punto aleatorio para el callejón decadente
            Vector3 pos = new Vector3(Random.Range(-180f, 180f), 550f, Random.Range(-180f, 180f));
            if (Physics.Raycast(pos, Vector3.down, out RaycastHit hit, 1000f))
            {
                pos = hit.point + Vector3.up * 0.1f;
            }

            // Cuerpo del NPC Desplomado (Adicción/Decaimiento)
            GameObject adicto = EnsamblarPunkProcedural();
            adicto.name = "NPC_Decadente_Desplomado";
            adicto.transform.SetParent(this.transform); // V24 FIX
            adicto.transform.position = pos + Vector3.up * 0.5f;
            
            // Inmovilizamos y lo tumbamos en el suelo
            Destroy(adicto.GetComponent<SistemaReaccionVital>());
            Destroy(adicto.GetComponent<GravedadCalles>());
            adicto.transform.localRotation = Quaternion.Euler(0, 0, 90f); // Postura fetal colapsada

            // Parafernalia delegada a FabricaSintetica
            GameObject jeringa = FabricaSintetica.InstanciarJeringuillaFisica(pos + new Vector3(0.5f, 0, 0.5f));
            jeringa.transform.SetParent(this.transform); // V24 FIX
            
            // Sangre delegada a FabricaSintetica
            GameObject charco = FabricaSintetica.InstanciarManchaGore(pos + Vector3.up * 0.02f, Random.Range(0.8f, 1.5f), new Color(0.2f, 0.02f, 0.02f));
            charco.name = "Mancha_SangreSeca_Callejon";
            charco.transform.SetParent(this.transform); // V24 FIX

            // Ratas atraídas al cuerpo
            for(int r=0; r<2; r++)
            {
                GameObject rata = prefabRata != null ? Instantiate(prefabRata) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rata.transform.SetParent(this.transform); // V24 FIX
                rata.transform.position = pos + new Vector3(Random.Range(-1.5f, 1.5f), 0.5f, Random.Range(-1.5f, 1.5f));
                rata.transform.localScale = new Vector3(0.2f, 0.2f, 0.4f);
                rata.AddComponent<GravedadCalles>();
                rata.GetComponent<Renderer>().material.color = Color.black;
            }
            
            yield return null; // Pausa 1 frame por cada callejón instanciado
        }
    }

    private GameObject EnsamblarPunkProcedural()
    {
        // Materiales locales no instanciados (uso básico)
        Material mCuero = new Material(Shader.Find("Standard")) { color = new Color(0.1f, 0.1f, 0.1f) };
        Material mPiel = new Material(Shader.Find("Standard")) { color = new Color(0.9f, 0.8f, 0.7f) };
        Material mCresta = new Material(Shader.Find("Standard")) { color = Color.magenta };

        // V15 CLEAN ARCHITECTURE: Delegar a Factoría Externa
        GameObject punk = FabricaSintetica.EnsamblarPunkBase(mCuero, mPiel, mCresta, optimizadoParaBoids: false);
        punk.name = "Estructura_Punk";
        
        // Colisionador Maestro y Rutinas de Supervivencia Exclusivas
        var col = punk.AddComponent<CapsuleCollider>();
        col.center = Vector3.up * 1f;
        col.height = 2f;
        
        punk.AddComponent<SistemaReaccionVital>();
        punk.AddComponent<GravedadCalles>();

        return punk;
    }
}

public class GravedadCalles : MonoBehaviour
{
    private int intentosV24 = 0;

    private void Update()
    {
        if (Physics.Raycast(transform.position + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 2000f))
        {
            transform.position = hit.point + Vector3.up * (transform.localScale.y / 2f);
            Destroy(this); // Anclado permanente
        }
        else
        {
            // V24 AUDIT FIX: Mitigación Fallo Estructural. Evitar estrangular la CPU en bucle infinito
            // si un animal "spawneó" en una región sin colisionador (fuera del mapa/caída libre).
            intentosV24++;
            if (intentosV24 > 50)
            {
                Destroy(gameObject);
            }
        }
    }
}

public class NavegacionAnimalIA : MonoBehaviour
{
    private UnityEngine.AI.NavMeshAgent agente;
    private float tiempoSiguienteRuta = 0f;

    private void Start()
    {
        agente = GetComponent<UnityEngine.AI.NavMeshAgent>();
    }

    private void Update()
    {
        if (agente == null || !agente.isOnNavMesh) return;

        if (Time.time > tiempoSiguienteRuta || agente.remainingDistance < 1f)
        {
            // Buscar un punto al azar en 40 metros
            Vector3 puntoAleatorio = transform.position + new Vector3(Random.Range(-40f, 40f), 0, Random.Range(-40f, 40f));
            if (UnityEngine.AI.NavMesh.SamplePosition(puntoAleatorio, out UnityEngine.AI.NavMeshHit hit, 40f, UnityEngine.AI.NavMesh.AllAreas))
            {
                agente.SetDestination(hit.position);
                tiempoSiguienteRuta = Time.time + Random.Range(5f, 15f);
            }
        }
    }
}
