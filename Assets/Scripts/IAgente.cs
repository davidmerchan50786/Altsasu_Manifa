// Assets/Scripts/IAgente.cs
// Contrato mínimo compartido entre todos los agentes IA del juego:
//   NPCCivil, PoliciaForalIA (vía NPCBase) y ManifestanteIA.
//
// Permite a SistemaIA y a las misiones trabajar con agentes genéricos
// sin depender de FindObjectsByType<T>() costoso en runtime.

using UnityEngine;

public interface IAgente
{
    Vector3 Posicion   { get; }
    bool    EstaActivo { get; }

    /// <summary>Reacción al sonido de disparo / explosión cerca.</summary>
    void Alertar(Vector3 origen);
}
