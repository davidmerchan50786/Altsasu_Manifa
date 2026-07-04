// Assets/Scripts/Runtime/PrisioneroAliado.cs
// ═══════════════════════════════════════════════════════════════════════════
//  RESCATE DE UN COMPAÑERO — libera a un aliado retenido (en el cuartel, etc.).
//
//  Componente IInteractable. Con [E] lo liberas: se une a ti como AliadoApoyo
//  (combate a tu lado), sube mucho el apoyo popular y, claro, la búsqueda.
//  Colócalo dentro del objetivo a asaltar. Un solo uso.
//  Capa RUNTIME. FICCIÓN.
// ═══════════════════════════════════════════════════════════════════════════
using UnityEngine;

public sealed class PrisioneroAliado : MonoBehaviour, IInteractable
{
    [SerializeField] string nombre = "compañero";
    bool _liberado;

    public string TextoInteraccion => _liberado ? $"{nombre} liberado" : $"[E] Liberar a {nombre}";
    public float  RadioInteraccion => 2.5f;
    public bool   PuedeInteractuar => !_liberado;

    public void OnInteractuar(ControladorJugador jugador)
    {
        if (_liberado) return;
        _liberado = true;

        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "Aliado_Rescatado";
        go.transform.position = transform.position + Vector3.up * 0.5f;
        go.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
        var col = go.GetComponent<Collider>(); if (col != null) col.isTrigger = true;
        UtilMaterial.Tenir(go.GetComponent<MeshRenderer>(), new Color(0.2f, 0.6f, 0.85f));
        go.AddComponent<AliadoApoyo>();

        SistemaApoyoPopular.Instance?.SumarApoyo(15f, "rescate");
        ServiceLocator.Get<IWantedSystem>()?.AumentarBusqueda(2);

        gameObject.SetActive(false);   // el prisionero sale contigo
    }
}
