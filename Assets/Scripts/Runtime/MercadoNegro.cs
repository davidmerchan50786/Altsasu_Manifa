// Assets/Scripts/Runtime/MercadoNegro.cs
// ═══════════════════════════════════════════════════════════════════════════
//  MERCADO NEGRO — contacto que vende armamento y munición "sin preguntas".
//
//  Componente IInteractable (ponlo en un contacto/trastienda). Con [E] abre la
//  tienda con un CATÁLOGO ILEGAL: armas y munición a precio de contrabando, pero
//  cada compra sube algo la PARANOIA (te estás señalando). Aprovecha el
//  descuento por apoyo (SistemaProgresion) igual que la tienda normal.
//
//  Capa RUNTIME. FICCIÓN — economía de contrabando de mundo abierto.
// ═══════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using UnityEngine;

public sealed class MercadoNegro : MonoBehaviour, IInteractable
{
    [SerializeField] string nombre = "Mercado negro";

    public string TextoInteraccion => $"[E] {nombre} (armas sin preguntas)";
    public float  RadioInteraccion => 3f;
    public bool   PuedeInteractuar => SistemaTienda.I != null;

    public void OnInteractuar(ControladorJugador jugador)
    {
        if (SistemaTienda.I == null) return;
        SistemaTienda.I.Abrir(Catalogo());
    }

    static List<Articulo> Catalogo()
    {
        var armas = Object.FindObjectOfType<SistemaArmasExtendido>();
        void Paranoia() => SistemaApoyoPopular.Instance?.SumarParanoia(6f);

        return new List<Articulo>
        {
            new Articulo("Pistola + 30",        250,  () => { armas?.RecogerArma(SistemaArmasExtendido.TipoArma.Pistola, 30);  Paranoia(); }),
            new Articulo("Escopeta + 16",       650,  () => { armas?.RecogerArma(SistemaArmasExtendido.TipoArma.Escopeta, 16); Paranoia(); }),
            new Articulo("Fusil + 60",          1300, () => { armas?.RecogerArma(SistemaArmasExtendido.TipoArma.Fusil, 60);    Paranoia(); }),
            new Articulo("Munición surtida",    160,  () => {
                armas?.RecogerArma(SistemaArmasExtendido.TipoArma.Pistola, 20);
                armas?.RecogerArma(SistemaArmasExtendido.TipoArma.Escopeta, 8);
                armas?.RecogerArma(SistemaArmasExtendido.TipoArma.Fusil, 30); Paranoia(); }),
            new Articulo("Explosivos (lapa x3)",900,  () => { armas?.RecogerArma(SistemaArmasExtendido.TipoArma.BombaLapa, 3); Paranoia(); }),
        };
    }
}
