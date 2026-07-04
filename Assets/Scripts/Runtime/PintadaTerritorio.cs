// Assets/Scripts/Runtime/PintadaTerritorio.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PINTADA QUE RECLAMA TERRITORIO — grafiti político que marca el barrio.
//
//  Componente IInteractable (ponlo en muros). Con [E] pintas (unos segundos):
//  sube el apoyo popular, registra una pintada en SistemaTerritorio (que aumenta
//  el control del barrio) y levanta un poco de búsqueda. Un solo uso por muro.
//  Capa RUNTIME. FICCIÓN.
// ═══════════════════════════════════════════════════════════════════════════
using System.Collections;
using UnityEngine;

public sealed class PintadaTerritorio : MonoBehaviour, IInteractable
{
    [SerializeField] float apoyo    = 3f;
    [SerializeField] float duracion = 2f;
    bool _pintado, _enCurso;

    public string TextoInteraccion => _pintado ? "Pintado" : _enCurso ? "Pintando…" : "[E] Pintar (reclamar barrio)";
    public float  RadioInteraccion => 2.5f;
    public bool   PuedeInteractuar => !_pintado && !_enCurso;

    public void OnInteractuar(ControladorJugador jugador)
    {
        if (_pintado || _enCurso) return;
        StartCoroutine(Pintar());
    }

    IEnumerator Pintar()
    {
        _enCurso = true;
        yield return new WaitForSeconds(duracion);

        SistemaApoyoPopular.Instance?.SumarApoyo(apoyo, "pintada");
        SistemaTerritorio.RegistrarPintada(transform.position);
        ServiceLocator.Get<IWantedSystem>()?.AumentarBusqueda(1);

        // marca visual: tiñe el primer renderer hijo
        var r = GetComponentInChildren<Renderer>();
        if (r != null) foreach (var m in r.materials) if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.65f, 0.1f, 0.1f));

        _pintado = true; _enCurso = false;
    }
}
