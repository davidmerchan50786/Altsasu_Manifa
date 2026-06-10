// Assets/Scripts/AnimalIA.cs
// Comportamiento básico de animal autónomo.
// Se añade a los prefabs de fauna (cierva, conejo, lobo) creados por AssetConnector.
// SistemaFauna gestiona el pool; este componente maneja la animación y huida.

using UnityEngine;

[RequireComponent(typeof(Animator))]
public class AnimalIA : MonoBehaviour
{
    [Header("Movimiento")]
    public float velocidadPatrulla  = 1.5f;
    public float velocidadHuida     = 8f;
    public float radioDeteccionPlayer = 20f;
    public float radioHuida         = 40f;

    [Header("Patrulla")]
    public float tiempoEntrePasos   = 3f;

    Animator   _anim;
    Rigidbody  _rb;
    Transform  _jugador;
    float      _timerPatrulla;
    Vector3    _destino;
    bool       _huyendo;

    static readonly int H_Walk = Animator.StringToHash("Walk");
    static readonly int H_Run  = Animator.StringToHash("Run");
    static readonly int H_Die  = Animator.StringToHash("Die");

    void Awake()
    {
        _anim = GetComponent<Animator>();
        _rb   = GetComponent<Rigidbody>();
    }

    void Start()
    {
        _destino = transform.position;
        _jugador = GameObject.FindGameObjectWithTag("Player")?.transform;
    }

    void Update()
    {
        if (_jugador == null)
        {
            _jugador = GameObject.FindGameObjectWithTag("Player")?.transform;
            return;
        }

        float dist = Vector3.Distance(transform.position, _jugador.position);

        if (dist < radioDeteccionPlayer)
        {
            // Huir del jugador
            _huyendo = true;
            Vector3 dir = (transform.position - _jugador.position).normalized;
            _destino = transform.position + dir * radioHuida;
        }
        else if (dist > radioHuida * 1.5f)
        {
            _huyendo = false;
        }

        if (_huyendo)
        {
            MoverHacia(_destino, velocidadHuida);
            SetAnim(false, true);
        }
        else
        {
            // Patrulla aleatoria
            _timerPatrulla -= Time.deltaTime;
            if (_timerPatrulla <= 0f)
            {
                _destino = transform.position + new Vector3(
                    Random.Range(-15f, 15f), 0f, Random.Range(-15f, 15f));
                _timerPatrulla = tiempoEntrePasos;
            }

            float distDst = Vector3.Distance(transform.position, _destino);
            if (distDst > 1f)
            {
                MoverHacia(_destino, velocidadPatrulla);
                SetAnim(true, false);
            }
            else
            {
                SetAnim(false, false);
            }
        }
    }

    void MoverHacia(Vector3 destino, float vel)
    {
        Vector3 dir = (destino - transform.position);
        dir.y = 0;
        if (dir.magnitude < 0.1f) return;
        dir.Normalize();
        transform.position += dir * vel * Time.deltaTime;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 6f);
    }

    void SetAnim(bool walk, bool run)
    {
        if (_anim == null) return;
        _anim.SetBool(H_Walk, walk);
        _anim.SetBool(H_Run,  run);
    }

    public void Morir()
    {
        if (_anim != null) _anim.SetTrigger(H_Die);
        enabled = false;
        if (_rb != null) _rb.linearVelocity = Vector3.zero;
        Destroy(gameObject, 3f);
    }
}
