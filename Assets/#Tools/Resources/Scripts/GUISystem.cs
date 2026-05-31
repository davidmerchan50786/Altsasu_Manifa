using UnityEngine;
using System.Collections;

public class GUISystem : MonoBehaviour
{
    public GUISkin CustomSkin;
    public bool MoneyShow = false;
    public bool CostShow  = false;
    public int  Money;
    public int  Cost;

    // ── Caché (evita Find/GetComponent cada frame) ────────────────────────
    GameObject _cachedPlayer;
    Weapons    _cachedWeapons;
    float      _refreshTimer;
    const float REFRESH_INTERVAL = 2f; // re-buscar jugador cada 2s (respawn)

    // ── Sincronización con GameManager ────────────────────────────────────
    void OnEnable()
    {
        GameManagerAltsasua.OnDineroCambia  += (v) => Money = v;
        GameManagerAltsasua.OnRespawn       += RefrescarCache;
    }
    void OnDisable()
    {
        GameManagerAltsasua.OnDineroCambia  -= (v) => Money = v;
        GameManagerAltsasua.OnRespawn       -= RefrescarCache;
    }

    void Start() => RefrescarCache();

    void Update()
    {
        _refreshTimer -= Time.deltaTime;
        if (_refreshTimer <= 0f) RefrescarCache();

        // Sincronizar dinero desde GameManager si no hay eventos
        var gm = GameManagerAltsasua.Instance;
        if (gm != null) Money = gm.dinero;
    }

    void RefrescarCache()
    {
        _refreshTimer  = REFRESH_INTERVAL;
        _cachedPlayer  = GameObject.FindGameObjectWithTag("Player");
        _cachedWeapons = _cachedPlayer != null ? _cachedPlayer.GetComponent<Weapons>() : null;
    }

    void OnTriggerEnter(Collider col) { if (col.gameObject.name == "MoneyShow") MoneyShow = true;  }
    void OnTriggerExit (Collider col) { if (col.gameObject.name == "MoneyShow") MoneyShow = false; }

    void OnGUI()
    {
        bool armaActiva = _cachedWeapons != null && _cachedWeapons.weaponIndex != 0;

        if (CustomSkin != null) GUI.skin = CustomSkin;
        GUILayout.BeginArea(new Rect(Screen.width - 110, 5, 105, 110));
        GUILayout.BeginVertical();

        if (MoneyShow) GUILayout.Label("$ " + Money, CustomSkin != null ? "MoneyStyle" : "label");
        if (CostShow)  GUILayout.Label("-$ " + Cost,  CustomSkin != null ? "CostStyle"  : "label");

        if (armaActiva)
        {
            var setup = _cachedWeapons.weaponsSetup[_cachedWeapons.weaponIndex];
            GUILayout.Label($"{setup.Bullets} / {setup.Magazine}",
                CustomSkin != null ? "WeaponInfo" : "label");
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}
