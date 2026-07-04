// Assets/Scripts/Runtime/PisoFranco.cs
// ═══════════════════════════════════════════════════════════════════════════
//  PISO FRANCO — refugio del movimiento, disponible según el APOYO POPULAR.
//
//  Componente IInteractable en un edificio. Si tu nivel (SistemaProgresion) llega
//  al requerido, con [E] entras: te curas, te escondes (pierdes la búsqueda
//  policial), bajas la paranoia y guardas partida. Los pisos de más nivel
//  requieren más apoyo (la red te cubre más cuanto más te quiere el pueblo).
//
//  Capa RUNTIME. FICCIÓN — refugio de mundo abierto.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;

public sealed class PisoFranco : MonoBehaviour, IInteractable
{
    [SerializeField] string nombre          = "piso franco";
    [SerializeField] int    nivelRequerido  = 1;   // depende del apoyo popular

    public string TextoInteraccion => SistemaProgresion.Nivel >= nivelRequerido
        ? $"[E] Entrar al {nombre} (curar · esconderte · guardar)"
        : $"{nombre} — necesitas nivel {nivelRequerido} de apoyo";
    public float RadioInteraccion => 2.5f;
    public bool  PuedeInteractuar => SistemaProgresion.Nivel >= nivelRequerido;

    public void OnInteractuar(ControladorJugador jugador)
    {
        if (SistemaProgresion.Nivel < nivelRequerido) return;
        jugador.GetComponent<IDamageable>()?.Curar(9999);          // salud llena
        ServiceLocator.Get<IWantedSystem>()?.AumentarBusqueda(-5); // pierdes a la poli (clampa a 0)
        SistemaApoyoPopular.Instance?.RestarParanoia(30f);
        SistemaGuardado.Instance?.GuardarEnSlot(0);
    }
}
