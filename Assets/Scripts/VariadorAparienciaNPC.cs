// Assets/Scripts/VariadorAparienciaNPC.cs
// ═══════════════════════════════════════════════════════════════════════════
//  VARIADOR DE APARIENCIA NPC — variedad tipo multitud real a partir de pocos
//  modelos base: cada NPC recibe altura/complexión y ritmo de movimiento
//  ligeramente distintos. Seguro: sólo toca escala del transform y velocidad.
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.AI;

public class VariadorAparienciaNPC : MonoBehaviour
{
    void Start()
    {
        float altura = Random.Range(0.90f, 1.10f);
        float ancho  = altura * Random.Range(0.95f, 1.06f);
        transform.localScale = new Vector3(ancho, altura, ancho);

        var agente = GetComponent<NavMeshAgent>();
        if (agente != null)
        {
            agente.speed        *= Random.Range(0.85f, 1.18f);
            agente.angularSpeed *= Random.Range(0.9f, 1.15f);
        }

        var anim = GetComponentInChildren<Animator>();
        if (anim != null && anim.runtimeAnimatorController != null)
            anim.Update(Random.Range(0f, 1.2f));
    }
}
