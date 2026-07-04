// Assets/Scripts/Runtime/Sabotaje.cs
// ═══════════════════════════════════════════════════════════════════════════
//  SABOTAJE — objetivo saboteable (coche patrulla, negocio rival, infraestructura).
//
//  Componente IInteractable. Con [E] inicias el sabotaje (unos segundos); al
//  completarlo: te llevas dinero, sube el apoyo popular y… la búsqueda policial.
//  El objeto queda inutilizado (renderers apagados / ennegrecido). Un solo uso.
//
//  Capa RUNTIME. FICCIÓN — acción de mundo abierto.
// ═══════════════════════════════════════════════════════════════════════════
using System.Collections;
using UnityEngine;

public sealed class Sabotaje : MonoBehaviour, IInteractable
{
    [SerializeField] string nombre          = "objetivo";
    [SerializeField] int    recompensaDinero = 200;
    [SerializeField] float  recompensaApoyo  = 4f;
    [SerializeField] int    subeBusqueda     = 2;
    [SerializeField] float  duracion         = 2.5f;

    bool _saboteado, _enCurso;

    public string TextoInteraccion => _saboteado ? $"{nombre} saboteado"
                                     : _enCurso   ? "Saboteando…"
                                     :              $"[E] Sabotear {nombre}";
    public float RadioInteraccion => 2.5f;
    public bool  PuedeInteractuar => !_saboteado && !_enCurso;

    public void OnInteractuar(ControladorJugador jugador)
    {
        if (_saboteado || _enCurso) return;
        StartCoroutine(Sabotear());
    }

    IEnumerator Sabotear()
    {
        _enCurso = true;
        yield return new WaitForSeconds(duracion);

        ServiceLocator.Get<IEconomyService>()?.GanarDinero(recompensaDinero);
        SistemaApoyoPopular.Instance?.SumarApoyo(recompensaApoyo, "sabotaje");
        ServiceLocator.Get<IWantedSystem>()?.AumentarBusqueda(subeBusqueda);

        // Visual: ennegrecer y apagar; si es un vehículo NPC, intentar destruirlo.
        foreach (var r in GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials) if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.1f, 0.1f, 0.1f));

        _saboteado = true; _enCurso = false;
    }
}
