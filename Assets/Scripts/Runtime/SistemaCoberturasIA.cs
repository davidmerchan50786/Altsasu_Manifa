// Assets/Scripts/Runtime/SistemaCoberturasIA.cs
// ═══════════════════════════════════════════════════════════════════════════
//  COBERTURAS IA — consultas tácticas para la IA de combate (policía, etc.).
//
//  Utilidad ESTÁTICA, sin estado y sin mover a nadie (cero conflicto):
//    · MejorCobertura(agente, amenaza, radio, out pos) → punto en NavMesh,
//      cercano al agente, donde un muro corta la línea de visión con la
//      amenaza (te cubre del jugador).
//    · PosicionFlanqueo(amenaza, desde, radio, out pos) → punto en NavMesh a un
//      costado de la amenaza, para rodearla.
//
//  La consume PoliciaForalIA para elegir cobertura dinámica cuando no tiene una
//  asignada a mano. Capa RUNTIME.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;
using UnityEngine.AI;

public static class SistemaCoberturasIA
{
    /// <summary>Mejor punto cubierto (rompe LoS con la amenaza) y alcanzable, cerca del agente.</summary>
    public static bool MejorCobertura(Vector3 agente, Vector3 amenaza, float radio, out Vector3 pos)
    {
        pos = agente;
        Vector3 lejos = agente - amenaza; lejos.y = 0f;
        if (lejos.sqrMagnitude < 0.01f) lejos = Vector3.forward;
        lejos.Normalize();

        float mejor = float.MaxValue;
        bool found = false;

        for (int i = 0; i < 12; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 30f, 0f) * lejos;
            for (float d = radio * 0.4f; d <= radio; d += radio * 0.3f)
            {
                Vector3 cand = agente + dir * d;
                if (!NavMesh.SamplePosition(cand, out var nh, 2f, NavMesh.AllAreas)) continue;
                Vector3 c = nh.position;
                // cubierto = algo bloquea la línea amenaza→punto (a altura de pecho)
                if (!Physics.Linecast(amenaza + Vector3.up * 1.4f, c + Vector3.up * 1.2f)) continue;
                float score = Vector3.Distance(agente, c);
                if (score < mejor) { mejor = score; pos = c; found = true; }
            }
        }
        return found;
    }

    /// <summary>Posición de flanqueo a un costado de la amenaza, en NavMesh.</summary>
    public static bool PosicionFlanqueo(Vector3 amenaza, Vector3 desde, float radio, out Vector3 pos)
    {
        pos = desde;
        Vector3 aA = amenaza - desde; aA.y = 0f;
        if (aA.sqrMagnitude < 0.01f) return false;
        aA.Normalize();
        Vector3 lado = Vector3.Cross(Vector3.up, aA);

        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 cand = amenaza + lado * (s * radio) - aA * (radio * 0.3f);
            if (NavMesh.SamplePosition(cand, out var nh, 3f, NavMesh.AllAreas)) { pos = nh.position; return true; }
        }
        return false;
    }
}
