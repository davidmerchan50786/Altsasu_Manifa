// Assets/Scripts/Runtime/Zulo.cs
// ═══════════════════════════════════════════════════════════════════════════
//  ZULO — alijo escondido en el bosque (mecánica de juego ficticia).
//
//  Componente IInteractable que colocas en el monte. Con [E]:
//    · te reabasteces de munición y armas guardadas,
//    · te curas un poco,
//    · pierdes parte del rastro policial (estás fuera de la red urbana),
//    · bajas la paranoia.
//  Enfriamiento para que no sea munición infinita.
//
//  Capa RUNTIME. FICCIÓN — escondrijo de mundo abierto.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;

public sealed class Zulo : MonoBehaviour, IInteractable
{
    [SerializeField] string nombre = "zulo";
    [SerializeField] float  enfriamiento = 120f;   // s entre usos
    float _listoEn;

    public string TextoInteraccion => Time.time >= _listoEn
        ? $"[E] {nombre}: reabastecerte y perder el rastro"
        : $"{nombre} vacío (vuelve más tarde)";
    public float RadioInteraccion => 2.5f;
    public bool  PuedeInteractuar => Time.time >= _listoEn;

    public void OnInteractuar(ControladorJugador jugador)
    {
        if (Time.time < _listoEn) return;
        _listoEn = Time.time + enfriamiento;

        var armas = Object.FindObjectOfType<SistemaArmasExtendido>();
        armas?.RecogerArma(SistemaArmasExtendido.TipoArma.Pistola, 30);
        armas?.RecogerArma(SistemaArmasExtendido.TipoArma.Escopeta, 8);
        armas?.RecogerArma(SistemaArmasExtendido.TipoArma.Molotov, 5);

        jugador.GetComponent<IDamageable>()?.Curar(40);
        ServiceLocator.Get<IWantedSystem>()?.AumentarBusqueda(-3);   // off-grid: pierdes rastro
        SistemaApoyoPopular.Instance?.RestarParanoia(15f);
    }
}
