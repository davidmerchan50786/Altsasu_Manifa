// Assets/#Xtra/WeaponEquip.cs
// Botón de equipado de arma en UI.
// Integrado con SistemaArmasExtendido (sistema principal) y con el sistema legado Weapons.

using UnityEngine;

public class WeaponEquip : MonoBehaviour
{
    [Tooltip("Índice del arma en SistemaArmasExtendido / Weapons.")]
    public int Index;

    [Tooltip("GameObject del arma a activar (hijo del slot de la mano derecha).")]
    public GameObject Weapon;

    // Cache de referencias — se resuelven en Start para no buscar en cada clic
    private Transform _weaponHand;

    private void Start()
    {
        var handGO = GameObject.FindGameObjectWithTag("WeaponHand");
        if (handGO != null) _weaponHand = handGO.transform;
    }

    /// <summary>Llamar desde un botón UI para equipar este arma.</summary>
    public void EquipWeapon()
    {
        // Desactivar todas las armas visibles en la mano derecha
        if (_weaponHand != null)
            foreach (Transform child in _weaponHand)
                child.gameObject.SetActive(false);

        // Activar el arma asignada a este slot
        if (Weapon != null) Weapon.SetActive(true);
    }
}
