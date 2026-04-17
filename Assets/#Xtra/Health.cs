using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityStandardAssets.Characters.ThirdPerson;
using UnityEngine.AI;
public class Health : MonoBehaviour
{
    public float CurrentHealth;
    public GameObject DeathPrefab;
    public Transform Pos;

    // Cached component references — avoids GetComponent allocations every frame
    private Animator _animator;
    private AICharacterControl _aiControl;
    private ThirdPersonCharacter _tpCharacter;
    private Rigidbody _rb;
    private CapsuleCollider _capsule;
    private NavMeshAgent _navAgent;
    private AutoDestroy _autoDestroy;

    // Guard so the death path runs exactly once
    private bool _isDead;

    void Start()
    {
        _animator    = GetComponent<Animator>();
        _aiControl   = GetComponent<AICharacterControl>();
        _tpCharacter = GetComponent<ThirdPersonCharacter>();
        _rb          = GetComponent<Rigidbody>();
        _capsule     = GetComponent<CapsuleCollider>();
        _navAgent    = GetComponent<NavMeshAgent>();
        _autoDestroy = GetComponent<AutoDestroy>();
    }

    void Update()
    {
        if (!_isDead && CurrentHealth < 0)
        {
            _isDead = true;

            // Use this if you want to instantiate a prefab at death
            // Instantiate(DeathPrefab, Pos.position, Pos.rotation);
            _animator.Play("Death");
            Destroy(_aiControl);
            Destroy(_tpCharacter);
            Destroy(_rb);
            Destroy(_capsule);
            Destroy(_navAgent);
            _autoDestroy.enabled = true;
        }
    }
}
