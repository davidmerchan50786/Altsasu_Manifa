// Assets/#Xtra/Health.cs
// Componente legado de salud para NPCs simples (no-jugador).
// Integrado con NPCBase/ControladorJugador via IDamageable cuando está disponible.
// Para el jugador usa ControladorJugador.Danar() directamente.

using UnityEngine;
using UnityEngine.AI;
using UnityStandardAssets.Characters.ThirdPerson;

public class Health : MonoBehaviour, IDamageable
{
    [Tooltip("Salud actual del personaje.")]
    public float CurrentHealth = 100f;

    [Tooltip("Prefab opcional que se instancia al morir (explosión, efecto, etc.).")]
    public GameObject DeathPrefab;

    [Tooltip("Posición donde instanciar DeathPrefab. Si null, usa transform.")]
    public Transform Pos;

    private bool _muerto;

    // ── IDamageable ──────────────────────────────────────────────────────────
    public void Danar(int cantidad)
    {
        if (_muerto) return;
        CurrentHealth -= cantidad;
        if (CurrentHealth <= 0f) Morir();
    }

    // ── Lógica de muerte ─────────────────────────────────────────────────────
    private void Update()
    {
        if (!_muerto && CurrentHealth <= 0f)
            Morir();
    }

    private void Morir()
    {
        if (_muerto) return;
        _muerto = true;

        if (DeathPrefab != null)
        {
            var spawnPos = Pos != null ? Pos.position : transform.position;
            var spawnRot = Pos != null ? Pos.rotation  : transform.rotation;
            Instantiate(DeathPrefab, spawnPos, spawnRot);
        }

        var anim = GetComponent<Animator>();
        if (anim != null) anim.Play("Death");

        // Desactivar componentes de control de movimiento de forma segura
        var aiCtrl = GetComponent<AICharacterControl>();
        if (aiCtrl != null) Destroy(aiCtrl);

        var tpc = GetComponent<ThirdPersonCharacter>();
        if (tpc != null) Destroy(tpc);

        var rb = GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);

        var col = GetComponent<CapsuleCollider>();
        if (col != null) Destroy(col);

        var agent = GetComponent<NavMeshAgent>();
        if (agent != null) Destroy(agent);

        // Activar auto-destrucción si existe
        var autoDestroy = GetComponent<AutoDestroy>();
        if (autoDestroy != null) autoDestroy.enabled = true;
    }
}
