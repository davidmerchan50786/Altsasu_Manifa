// Assets/Scripts/Runtime/PuntoReparto.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PUNTO DE REPARTO — recoges aquí el paquete y SistemaReparto marca el destino.
//  Componente IInteractable. Asigna 'destino', tiempo y recompensa en el Inspector.
//  Capa RUNTIME.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;

public sealed class PuntoReparto : MonoBehaviour, IInteractable
{
    [SerializeField] Transform destino;
    [SerializeField] float     segundos   = 120f;
    [SerializeField] int       recompensa = 400;
    [SerializeField] float     apoyo      = 5f;

    public string TextoInteraccion => "[E] Recoger paquete (reparto)";
    public float  RadioInteraccion => 2.5f;
    public bool   PuedeInteractuar => destino != null && !SistemaReparto.Activo;

    public void OnInteractuar(ControladorJugador jugador)
    {
        if (destino == null || SistemaReparto.I == null) return;
        SistemaReparto.I.Iniciar(destino.position, segundos, recompensa, apoyo);
    }
}
