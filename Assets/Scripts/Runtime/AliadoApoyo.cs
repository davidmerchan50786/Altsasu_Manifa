// Assets/Scripts/Runtime/AliadoApoyo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ALIADO DE APOYO — combatiente del comando que invoca el jugador.
//
//  Sencillo y temporal: busca al policía más cercano, va hacia él y le hace
//  daño periódico; si no hay enemigo, acompaña al jugador. Se desvanece tras
//  su tiempo de vida. No es IDamageable (los aliados no se pueden fijar/abatir).
//
//  Lo crea ComandoApoyo. Capa RUNTIME.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;

public sealed class AliadoApoyo : MonoBehaviour
{
    const float VEL = 4.5f, RANGO_ATAQUE = 2.5f, BUSQUEDA = 40f, CADENCIA = 1.0f, DANO = 18f, VIDA = 60f;

    Transform _jugador;
    float _tBusca, _tAtaque, _tVida = VIDA;
    PoliciaForalIA _objetivo;

    void Start() { _jugador = AltsasuCore.Jugador; }

    void Update()
    {
        float dt = Time.deltaTime;
        _tVida -= dt;
        if (_tVida <= 0f) { Destroy(gameObject); return; }

        _tBusca -= dt;
        if (_tBusca <= 0f) { _tBusca = 0.8f; _objetivo = BuscarPolicia(); }

        if (_objetivo != null && !_objetivo.EstaMuerto)
        {
            Vector3 a = _objetivo.transform.position; a.y = transform.position.y;
            float d = Vector3.Distance(transform.position, a);
            if (d > RANGO_ATAQUE) Mover(a);
            else
            {
                _tAtaque -= dt;
                if (_tAtaque <= 0f) { _tAtaque = CADENCIA; ((IDamageable)_objetivo).RecibirDano((int)DANO, transform.position, TipoDano.Bala); }
            }
        }
        else if (_jugador != null)
        {
            Vector3 destino = _jugador.position - (_jugador.forward * 2f);
            if (Vector3.Distance(transform.position, destino) > 3f) Mover(destino);
        }
    }

    void Mover(Vector3 destino)
    {
        Vector3 p = Vector3.MoveTowards(transform.position, destino, VEL * Time.deltaTime);
        if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 6f)) p.y = hit.point.y + 0.1f;
        Vector3 mira = destino - transform.position; mira.y = 0f;
        if (mira.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(mira), Time.deltaTime * 8f);
        transform.position = p;
    }

    PoliciaForalIA BuscarPolicia()
    {
        PoliciaForalIA mejor = null; float min = BUSQUEDA;
        foreach (var p in FindObjectsOfType<PoliciaForalIA>())
        {
            if (p == null || p.EstaMuerto) continue;
            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d < min) { min = d; mejor = p; }
        }
        return mejor;
    }
}
