// Assets/Scripts/MegaManifestacion.cs
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// Motor de Macro-Manifestaciones Optimizado (V14).
/// Arquitectura basada en Patrón Flocking Simplificado (Líder-Seguidor).
/// Utiliza Instanciación en Tarjeta Gráfica (GPU Instancing) para evadir los límites de Draw-Calls de Unity.
/// </summary>
[AddComponentMenu("Alsasua V14/Motor de Macro-Manifestaciones (1000 NPCs)")]
public class MegaManifestacion : MonoBehaviour
{
    private const int TOTAL_MANIFESTANTES = 1000;
    private const int NUM_LIDERES = 20;

    // Listas internas cacheadas para prevenir el Garbage Collection (GC Allocation = 0)
    private readonly List<Transform> lideres = new List<Transform>();
    private readonly Transform[] manifestantes = new Transform[TOTAL_MANIFESTANTES - NUM_LIDERES];
    private readonly float[] offsetsX = new float[TOTAL_MANIFESTANTES - NUM_LIDERES];
    private readonly float[] offsetsZ = new float[TOTAL_MANIFESTANTES - NUM_LIDERES];
    private readonly int[] liderAsignado = new int[TOTAL_MANIFESTANTES - NUM_LIDERES];

    private Material matCueroNegro;
    private Material matCrestaRosa;
    private Material matPiel;

    void Start()
    {
        // V20 AUDIT: Desactivar colisiones entre los cuerpos de los Punks (Layer 9) para mantener 60 FPS
        Physics.IgnoreLayerCollision(9, 9, true);

        // 1. Optimización GPU (GPU Instancing para 1000 modelos)
        matCueroNegro = new Material(Shader.Find("Standard"));
        matCueroNegro.color = new Color(0.1f, 0.1f, 0.1f);
        matCueroNegro.enableInstancing = true;

        matPiel = new Material(Shader.Find("Standard"));
        matPiel.color = new Color(0.9f, 0.8f, 0.7f);
        matPiel.enableInstancing = true;

        matCrestaRosa = new Material(Shader.Find("Standard"));
        matCrestaRosa.color = Color.magenta;
        matCrestaRosa.enableInstancing = true;

        GenerarManifestacion();
    }

    private void OnDestroy()
    {
        // V23 FIX: Prevenir Memory Leak de materiales de instancia de GPU al recargar la escena.
        if (matCueroNegro != null) Destroy(matCueroNegro);
        if (matPiel != null) Destroy(matPiel);
        if (matCrestaRosa != null) Destroy(matCrestaRosa);
    }

    /// <summary>
    /// Genera la manifestación estructurada.
    /// Crea un subconjunto de Líderes (NavMesh) y un Enjambre masivo (Followers Matemáticos).
    /// </summary>
    private void GenerarManifestacion()
    {
        Vector3 epicentroInicial = transform.position;

        // Proyección Topográfica: Buscar colisión contra el asfalto empleando Raycast O(1)
        if (Physics.Raycast(epicentroInicial + Vector3.up * 500f, Vector3.down, out RaycastHit hitFloor, 1000f))
        {
            epicentroInicial = hitFloor.point;
        }

        // Crear Líderes (NavMeshAgents Reales)
        for (int i = 0; i < NUM_LIDERES; i++)
        {
            GameObject lider = EnsamblarClonPunk(true);
            lider.name = "Lider_Manifestacion_" + i;
            lider.transform.SetParent(this.transform); // V23 FIX: Mantener jerarquía limpia
            Vector3 spawn = epicentroInicial + new Vector3(Random.Range(-30f, 30f), 0, Random.Range(-30f, 30f));
            lider.transform.position = spawn;

            var nav = lider.AddComponent<NavMeshAgent>();
            nav.speed = 1.5f; // Marcha lenta
            nav.radius = 0.5f;
            lider.AddComponent<LiderRutaIA>();
            lideres.Add(lider.transform);

            // 50% probabilidad de llevar pancarta generada proceduralmente
            if (Random.value > 0.5f) CrearPancarta(lider.transform);
        }

        // Crear el Enjambre Seguidor
        // Coste Computacional: Zero-CPU para Pathfinding, se limitarán a interpelaciones Lerp espaciales
        for (int i = 0; i < manifestantes.Length; i++)
        {
            GameObject clon = EnsamblarClonPunk(false);
            clon.name = "Manifestante_" + i;
            clon.transform.SetParent(this.transform); // V23 FIX: Mantener jerarquía limpia
            clon.transform.position = epicentroInicial + new Vector3(Random.Range(-50f, 50f), 0, Random.Range(-50f, 50f));
            manifestantes[i] = clon.transform;

            liderAsignado[i] = Random.Range(0, NUM_LIDERES);
            offsetsX[i] = Random.Range(-10f, 10f);
            offsetsZ[i] = Random.Range(-20f, -2f); // Siempre detrás del líder

            // 15% probabilidad de llevar bandera
            if (Random.value > 0.85f) CrearBandera(clon.transform);
        }

        Debug.Log("[V14 Macro-Turba] 1000 Punks generados en manifestación.");
        
        // Audio ambiental de turba gritando
        AudioSource audioTurba = gameObject.AddComponent<AudioSource>();
        audioTurba.spatialBlend = 1f;
        audioTurba.minDistance = 50f;
        audioTurba.maxDistance = 800f;
        audioTurba.loop = true;
        // Simulador procedural de ruido de masa
        SintetizadorAudioProcedural.PlayEstaticaTurba(audioTurba); 
    }

    private GameObject EnsamblarClonPunk(bool esLider)
    {
        // V15 CLEAN ARCHITECTURE: Delegamos síntesis estructural a Factoría, usando Materiales GPU instanciados cacheados
        GameObject punk = FabricaSintetica.EnsamblarPunkBase(matCueroNegro, matPiel, matCrestaRosa, optimizadoParaBoids: true);
        punk.name = "Cuerpo_Manifestante";

        // Comportamientos IA específicos
        if (esLider)
        {
            var col = punk.AddComponent<CapsuleCollider>();
            col.center = Vector3.up * 1f;
            col.height = 2f;
            punk.AddComponent<SistemaReaccionVital>(); // Solo los líderes reaccionan al gore por optimización masiva RAM
        }
        else
        {
            // V20 AUDIT: Kinematic Triggers para interceptar Balas Raycast (evita que las balas sean fantasmas)
            punk.layer = 9; 
            var col = punk.AddComponent<CapsuleCollider>();
            col.center = Vector3.up * 1f;
            col.height = 2f;
            col.isTrigger = true; // Actúa solo como barrera óptica para Raycasts
            
            var rb = punk.AddComponent<Rigidbody>();
            rb.isKinematic = true; // El physics solver no calcula esta masa a la hora de moverse, optimización extrema.
        }
        
        return punk;
    }

    // V15: La creación de Pancartas y Banderas ha sido transferida a la abstracta FabricaSintetica.
    // Los bloques estáticos de texturas siguen siendo inyectados temporalmente aquí o se usan nulos para blanco puro.
    private void CrearBandera(Transform portador)
    {
        GameObject bandera = FabricaSintetica.EnsamblarBandera(portador);
        bandera.name = "Bandera_Abertzale";

        // Mastil
        var palo = bandera.transform.Find("Mastil").GetComponent<Renderer>();
        palo.sharedMaterial = matCueroNegro;

        // Tela rotando al viento proceduralmente
        var tela = bandera.transform.Find("Tela").GetComponent<Renderer>();
        
        Material matTela = new Material(Shader.Find("Unlit/Color"));
        // 50% Roja (Comunista/Sindical), 50% Rojinegra (Anarquista) o Ikurriña abstracta (Verde/Roja)
        float r = Random.value;
        if(r < 0.3f) matTela.color = Color.red;
        else if(r < 0.6f) matTela.color = new Color(0.1f, 0.5f, 0.1f); // Verde
        else matTela.color = new Color(0.2f, 0f, 0f); // Rojinegra oscura
        
        Destroy(tela.GetComponent<Collider>());
    }

    private void CrearPancarta(Transform portador)
    {
        GameObject pancarta = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pancarta.name = "Pancarta_Tex";
        pancarta.transform.SetParent(portador);
        pancarta.transform.localPosition = new Vector3(0f, 3f, 0.3f);
        pancarta.transform.localScale = new Vector3(3f, 1f, 0.1f);
        Destroy(pancarta.GetComponent<Collider>());

        Material lona = new Material(Shader.Find("Standard"));
        lona.color = Color.white;
        pancarta.GetComponent<Renderer>().sharedMaterial = lona;

        // Texto
        GameObject goTexto = new GameObject("Texto_Pancarta");
        goTexto.transform.SetParent(pancarta.transform);
        goTexto.transform.localPosition = new Vector3(-1.3f, 0.2f, -0.06f);
        goTexto.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
        TextMesh tm = goTexto.AddComponent<TextMesh>();
        tm.text = Random.value > 0.5f ? "ALTSASUKOAK\nASKE!" : "GORA\nBORROKA!";
        tm.fontSize = 24;
        tm.color = Color.black;
        tm.fontStyle = FontStyle.Bold;
    }

    /// <summary>
    /// Update Funcional.
    /// Complejidad Asintótica O(N): Escala linealmente mediante 1000 multiplicadores de vectores.
    /// Mitiga severamente la degradación de FPS que causaría el A* en 1000 entidades simultáneas.
    /// </summary>
    void Update()
    {
        // V19 AUDIT: Chequeo O(1) Termonuclear Falsa Físcia para los 1000 Boids Inmortales
        bool hayBombaExplota = Time.time < EventoTermonuclear.TiempoBomba + 5f;

        for (int i = 0; i < manifestantes.Length; i++)
        {
            if (manifestantes[i] == null) continue;
            
            if (hayBombaExplota)
            {
                float distSq = (manifestantes[i].position - EventoTermonuclear.EpicentroUltimaBomba).sqrMagnitude;
                if (distSq < EventoTermonuclear.RadioDestruccion * EventoTermonuclear.RadioDestruccion)
                {
                    // Asesinato matemático masivo sin físicas (O(1))
                    manifestantes[i].localRotation = Quaternion.Euler(90f, 0, 0); // Tirado
                    manifestantes[i].position += Vector3.down * 0.9f;
                    Destroy(manifestantes[i].gameObject, Random.Range(1f, 4f)); // Borrar en diferido
                    manifestantes[i] = null;
                    continue;
                }
            }

            Transform lider = lideres[liderAsignado[i]];
            if (lider == null)
            {
                // V23 FIX (Zombie Followers): Si el líder fue destruido, los seguidores
                // se quedaban congelados como estatuas. Ahora buscan un nuevo líder vivo.
                bool reasignado = false;
                for (int k = 0; k < lideres.Count; k++)
                {
                    if (lideres[k] != null)
                    {
                        liderAsignado[i] = k;
                        lider = lideres[k];
                        reasignado = true;
                        break;
                    }
                }

                // Si no queda ningún líder vivo en toda la ciudad, se dispersan (Despawn)
                if (!reasignado)
                {
                    manifestantes[i].localRotation = Quaternion.Euler(0f, Random.Range(0, 360f), 0f); // Mirar a otro lado
                    Destroy(manifestantes[i].gameObject, Random.Range(0.5f, 2f));
                    manifestantes[i] = null;
                    continue;
                }
            }

            // Offset matricial: sitúa a los seguidores rígidamente detrás o a los lados del líder
            Vector3 objetivo = lider.position + lider.right * offsetsX[i] + lider.forward * offsetsZ[i];
            
            // Interpolación Suavizada (Lerp): Rompe la rigidez matricial simulando a personas empujándose orgánicamente
            manifestantes[i].position = Vector3.Lerp(manifestantes[i].position, objetivo, Time.deltaTime * 1.5f);
            
            // Vector de Rotación Continua
            Vector3 dir = (objetivo - manifestantes[i].position).normalized;
            if (dir != Vector3.zero)
            {
                manifestantes[i].rotation = Quaternion.Slerp(manifestantes[i].rotation, Quaternion.LookRotation(dir), Time.deltaTime * 5f);
            }
        }
    }
}

public class LiderRutaIA : MonoBehaviour
{
    private NavMeshAgent agente;
    void Start()
    {
        agente = GetComponent<NavMeshAgent>();
        AsignarNuevaRuta();
    }
    void Update()
    {
        if(agente.isOnNavMesh && agente.remainingDistance < 2f) AsignarNuevaRuta();
    }
    void AsignarNuevaRuta()
    {
        if (NavMesh.SamplePosition(transform.position + new Vector3(Random.Range(-100f, 100f), 0, Random.Range(-100f, 100f)), out NavMeshHit hit, 100f, NavMesh.AllAreas))
        {
            agente.SetDestination(hit.position);
        }
    }
}

public class OsciladorViento : MonoBehaviour
{
    private float offset;
    void Start() { offset = Random.Range(0f, 100f); }
    void Update()
    {
        // Rotación local rápida para simular tela y viento
        transform.localRotation = Quaternion.Euler(0, Mathf.Sin(Time.time * 8f + offset) * 15f, 0);
    }
}
