using UnityEngine;
using UnityEngine.AI;
using UnityStandardAssets.Characters.ThirdPerson;

public class Health : MonoBehaviour
{
    [Header("Vida")]
    public float CurrentHealth = 100f;
    public float MaxHealth     = 100f;

    [Header("Muerte")]
    public GameObject DeathPrefab;
    public Transform  Pos;

    // ── Estado interno ─────────────────────────────────────────────────────
    bool _isDead;

    void Start()
    {
        CurrentHealth = MaxHealth;
    }

    void Update()
    {
        if (_isDead || CurrentHealth > 0) return;
        Morir();
    }

    public void RecibirDaño(float cantidad)
    {
        if (_isDead) return;
        CurrentHealth = Mathf.Max(0, CurrentHealth - cantidad);
    }

    public void Curar(float cantidad)
    {
        if (_isDead) return;
        CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + cantidad);
    }

    // Llamado por el GameManager al matar al jugador
    public void RestablecerVida()
    {
        _isDead       = false;
        CurrentHealth = MaxHealth;
    }

    void Morir()
    {
        _isDead = true;

        // Animación de muerte (sólo una vez)
        var anim = GetComponent<Animator>();
        if (anim != null) anim.Play("Death");

        // Instanciar prefab de muerte
        if (DeathPrefab != null && Pos != null)
            Instantiate(DeathPrefab, Pos.position, Pos.rotation);

        // Desactivar componentes de movimiento/IA
        DestruirSi<AICharacterControl>();
        DestruirSi<ThirdPersonCharacter>();

        // Desactivar física (el cadáver queda en el sitio)
        var rb = GetComponent<Rigidbody>();
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.isKinematic = true; }

        DestruirSi<NavMeshAgent>();

        // Notificar al GameManager
        var gm = GameManagerAltsasua.Instance;
        if (gm != null && CompareTag("Player")) gm.JugadorMuerto();

        // AutoDestroy si el objeto tiene ese componente
        var ad = GetComponent<AutoDestroy>();
        if (ad != null) ad.enabled = true;
        else Destroy(gameObject, 5f); // limpiar NPCs muertos en 5s
    }

    void DestruirSi<T>() where T : Component
    {
        var c = GetComponent<T>();
        if (c != null) Destroy(c);
    }
}
