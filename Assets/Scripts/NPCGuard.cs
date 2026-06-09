// Assets/Scripts/Gameplay/NPC/NPCGuard.cs
// ═══════════════════════════════════════════════════════════════════════════
//  NPC GUARDIA — ejemplo de NPC nuevo añadido con la nueva arquitectura
//
//  FASE 4 — VALIDACIÓN:
//  Para añadir este NPC nuevo solo fue necesario tocar:
//    1. Este archivo (NPCGuard.cs) — la clase concreta
//    2. Un prefab en el editor (asignar el componente)
//  Nada más. No fue necesario tocar:
//    - AltsasuCore (EnsureOn no lo referencia, se añade por prefab)
//    - GameManagerAltsasua
//    - SistemaZonas / GeneradorMundoOSM
//    - Ningún sistema existente
//
//  Esto confirma que la capa Gameplay/NPC está correctamente desacoplada.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class NPCGuard : NPCBase
{
    [Header("Guardia")]
    public float radioVigilancia = 15f;

    enum Estado { Vigilando, Alertado }
    Estado _estado = Estado.Vigilando;

    // Figura de autoridad → modelo de Guardia Civil real si prefabModelo está vacío.
    protected override GameObject ObtenerModeloPorDefecto()
        => SistemaAssets.Instance != null
            ? SistemaAssets.Instance.GuardiaAleatorio()
            : null;

    protected override void AlActivarAgente()
    {
        _agente.speed = velocidadBase;
        AlsasuaLogger.Info("NPCGuard", $"{name}: en posición");
    }

    protected override void ActualizarComportamiento()
    {
        switch (_estado)
        {
            case Estado.Vigilando:
                // Buscar jugador en radio
                if (_jugador != null &&
                    Vector3.Distance(transform.position, _jugador.position) < radioVigilancia)
                {
                    _estado = Estado.Alertado;
                    ServiceLocator.Get<IWantedSystem>()?.AumentarBusqueda(1);
                }
                break;

            case Estado.Alertado:
                if (_jugador != null)
                    _agente.SetDestination(_jugador.position);
                break;
        }
    }

    public override void Alertar(Vector3 origen)
        => _estado = Estado.Alertado;

    protected override void CrearCuerpoFallback()
    {
        // Cuerpo mínimo procedural — bloque verde
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0, 0.9f, 0);
        go.GetComponent<Renderer>().material.color = new Color(0.2f, 0.5f, 0.2f);
        var col = go.GetComponent<Collider>();
        if (col) Object.Destroy(col);
    }
}
