// Assets/Scripts/SistemaBalistico.cs
using UnityEngine;

[AddComponentMenu("Alsasua V13/Operador Balístico Táctico")]
public class SistemaBalistico : MonoBehaviour
{
    private Camera cam;
    public float rangoMaximo = 500f;
    public int danoBase = 60;
    private float fireRate = 0.15f;
    private float nextFire = 0f;

    void Start()
    {
        cam = Camera.main;
        if (cam == null) cam = GetComponentInChildren<Camera>(); // Fallback
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0) && Time.time >= nextFire && !Input.GetKey(KeyCode.LeftAlt)) // LeftAlt is Camera orbital
        {
            nextFire = Time.time + fireRate;
            DispararARMA();
        }
    }

    private void DispararARMA()
    {
        SintetizadorAudioProcedural.PlayGunshot(cam.transform.position);

        // Flash de cañón asimétrico (Muzzle Flash procedural)
        GameObject flash = new GameObject("MuzzleFlash_Light");
        flash.transform.position = cam.transform.position + cam.transform.forward * 1.5f;
        Light luz = flash.AddComponent<Light>();
        luz.type = LightType.Point;
        luz.color = new Color(1f, 0.8f, 0.3f);
        luz.intensity = 5f;
        luz.range = 20f;
        Destroy(flash, 0.05f); // Dura un frame prácticamente
        
        // Retroceso sutil de cámara (Recoil)
        cam.transform.localRotation *= Quaternion.Euler(-Random.Range(0.5f, 2f), Random.Range(-1f, 1f), 0);

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        // V22 AUDIT: Forzar 'QueryTriggerInteraction.Collide' para que la bala lea las cápsulas huecas (Followers).
        if (Physics.Raycast(ray, out RaycastHit hit, rangoMaximo, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            ProcesarImpacto(hit, ray.direction);
        }
    }

    private void ProcesarImpacto(RaycastHit hit, Vector3 direccion)
    {
        CristalDestructible vidrio = hit.collider.GetComponent<CristalDestructible>();
        if (vidrio != null) 
        {
            vidrio.HacerAñicos(hit.point);
            return; // Atraviesa el cristal visualmente pero lo rompe
        }

        SistemaReaccionVital vital = hit.collider.GetComponentInParent<SistemaReaccionVital>();
        if (vital != null)
        {
            // V13: Balística Visceral
            SintetizadorGore.EsparcirSangre(hit.point, 0.4f);
            vital.RecibirImpactoBalistico();
            
            Rigidbody rbEnemigo = vital.GetComponent<Rigidbody>();
            if (rbEnemigo != null && !rbEnemigo.isKinematic)
            {
                rbEnemigo.AddForceAtPosition(direccion * 400f, hit.point, ForceMode.Impulse);
            }
        }
        else if (hit.collider.gameObject.layer == 9)
        {
            // V22 AUDIT FIX: Los seguidores no tienen SistemaReaccion (para ahorrar RAM). 
            // Si la bala impacta su Trigger, emulamos una muerte hiper-ligera matemáticamente.
            SintetizadorGore.EsparcirSangre(hit.point, 0.4f);
            
            Transform punk = hit.collider.transform;
            punk.localRotation = Quaternion.Euler(90f, 0, 0); // Tirado
            punk.position += Vector3.down * 0.9f;
            
            // Destruir el collider y el mesh proceduralmente para liberar memoria
            Destroy(hit.collider.gameObject, Random.Range(1f, 4f));
        }
        else
        {
            // Impacto en Concreto / Coche (Chispa y Calcomanía)
            GameObject chispa = GameObject.CreatePrimitive(PrimitiveType.Quad);
            chispa.transform.position = hit.point + hit.normal * 0.02f;
            chispa.transform.rotation = Quaternion.LookRotation(hit.normal);
            chispa.transform.localScale = Vector3.one * Random.Range(0.1f, 0.2f);
            chispa.GetComponent<Renderer>().material.color = Color.black;
            Destroy(chispa.GetComponent<Collider>()); // Decal
            Destroy(chispa, 30f); // Se borran en 30 secs
            
            // Sonidos de Ricochet metálico si es un coche (Simulado aquí con audio genérico bajito)
            SintetizadorAudioProcedural.PlayGunshot(hit.point); // Reuse lower volume for ricochet thud
            
            if (hit.rigidbody != null)
            {
                hit.rigidbody.AddForceAtPosition(direccion * 1500f, hit.point, ForceMode.Impulse);
            }
        }
    }
}
