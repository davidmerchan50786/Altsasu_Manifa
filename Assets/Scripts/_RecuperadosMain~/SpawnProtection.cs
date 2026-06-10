// Assets/Scripts/SpawnProtection.cs
// Protege al jugador de caer al vacío al iniciar el juego.
// Hace kinematic el Rigidbody hasta que detecte suelo bajo el jugador.
// Si después de 3 segundos sigue sin suelo, fuerza al jugador al terrain activo.

using System.Collections;
using UnityEngine;

public class SpawnProtection : MonoBehaviour
{
    [Tooltip("Distancia máxima del raycast hacia abajo")]
    public float distanciaCheck = 200f;
    [Tooltip("Margen sobre el suelo al teletransportar")]
    public float margenSuelo = 1.5f;
    [Tooltip("Tiempo de protección antes de activar física")]
    public float tiempoProteccion = 1.5f;

    Rigidbody _rb;

    IEnumerator Start()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb == null) { Destroy(this); yield break; }

        // Modo kinematic hasta que el terreno esté listo
        _rb.isKinematic = true;
        _rb.linearVelocity = Vector3.zero;

        // Esperar a que el terrain tenga físicas
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        // Buscar el suelo con raycast desde arriba
        Vector3 origenRay = new Vector3(transform.position.x, transform.position.y + 100f, transform.position.z);
        if (Physics.Raycast(origenRay, Vector3.down, out RaycastHit hit, distanciaCheck))
        {
            transform.position = new Vector3(hit.point.x, hit.point.y + margenSuelo, hit.point.z);
            Debug.Log($"[SpawnProtection] ✓ Suelo encontrado en Y={hit.point.y:F1}. Jugador colocado.");
        }
        else if (Terrain.activeTerrain != null)
        {
            // Fallback: sample directo del terrain
            float h = Terrain.activeTerrain.SampleHeight(transform.position);
            transform.position = new Vector3(transform.position.x, h + margenSuelo, transform.position.z);
            Debug.Log($"[SpawnProtection] ✓ Terrain.SampleHeight = {h:F1}. Jugador colocado.");
        }
        else
        {
            Debug.LogWarning("[SpawnProtection] ⚠ No se encontró suelo. Posición default.");
            transform.position = new Vector3(1918f, 245f, 8570f);
        }

        // Esperar la protección y activar física
        yield return new WaitForSeconds(tiempoProteccion);
        _rb.isKinematic = false;
        _rb.linearVelocity = Vector3.zero;
        Debug.Log("[SpawnProtection] ✓ Física activada. Controla con WASD.");

        // Guard adicional: si después de 1s sigue cayendo > 10m, teletransportar
        Vector3 posInicial = transform.position;
        yield return new WaitForSeconds(1f);
        if (transform.position.y < posInicial.y - 20f)
        {
            Debug.LogWarning("[SpawnProtection] ⚠ Jugador cayendo al vacío — teletransportando al terrain.");
            _rb.isKinematic = true;
            if (Terrain.activeTerrain != null)
            {
                float h = Terrain.activeTerrain.SampleHeight(new Vector3(1918f, 0, 8570f));
                transform.position = new Vector3(1918f, h + 2f, 8570f);
            }
            yield return new WaitForFixedUpdate();
            _rb.isKinematic = false;
            _rb.linearVelocity = Vector3.zero;
        }

        Destroy(this); // ya no se necesita
    }
}
