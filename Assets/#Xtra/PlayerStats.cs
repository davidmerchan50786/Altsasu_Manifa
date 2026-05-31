// Assets/#Xtra/PlayerStats.cs
// HUD de estadísticas del jugador: salud, hambre, sed.
// Integrado con ControladorJugador para leer la salud real del sistema principal.
// Los textos y barras solo se actualizan cuando los valores cambian (sin allocs por frame).

using UnityEngine;
using UnityEngine.UI;

public class PlayerStats : MonoBehaviour
{
    [Header("Salud (sincronizada con ControladorJugador si está disponible)")]
    public float Health;
    public float MaxHealth = 100f;

    [Header("Necesidades")]
    public float Hunger    = 100f;
    public float Thirst    = 100f;
    public float HungerRate = 1f;
    public float ThirstRate = 1.5f;

    [Header("UI")]
    public Slider  HungerBar;
    public Slider  ThirstBar;
    public Slider  HealtBar;
    public Text    HealthText;
    public Text    HungerText;
    public Text    ThirstText;

    // Cache para evitar string allocs por frame
    private float  _lastHealth  = float.MinValue;
    private float  _lastHunger  = float.MinValue;
    private float  _lastThirst  = float.MinValue;

    private ControladorJugador _controlJugador;

    private void Start()
    {
        // Buscar ControladorJugador en el jugador principal
        var jugadorT = AltsasuCore.Jugador;
        if (jugadorT != null)
            _controlJugador = jugadorT.GetComponent<ControladorJugador>();

        if (HealtBar != null) HealtBar.maxValue = MaxHealth;
        if (HungerBar != null) { HungerBar.minValue = 0; HungerBar.maxValue = 100; }
        if (ThirstBar != null) { ThirstBar.minValue = 0; ThirstBar.maxValue = 100; }
    }

    private void Update()
    {
        // Sincronizar salud con el sistema principal si está disponible
        if (_controlJugador != null)
        {
            Health    = _controlJugador.Vida;
            MaxHealth = _controlJugador.VidaMax;
        }

        ActualizarNecesidades();
        ActualizarUI();
    }

    private void ActualizarNecesidades()
    {
        Hunger = Mathf.Max(0f, Hunger - HungerRate * Time.deltaTime);
        Thirst = Mathf.Max(0f, Thirst - ThirstRate * Time.deltaTime);
    }

    private void ActualizarUI()
    {
        // Solo reasignar texto/barra si el valor cambió — evita allocs de string por frame
        if (!Mathf.Approximately(Health, _lastHealth))
        {
            _lastHealth = Health;
            if (HealtBar   != null) { HealtBar.maxValue = MaxHealth; HealtBar.value = Health; }
            if (HealthText != null) HealthText.text = Mathf.CeilToInt(Health).ToString();
        }

        if (!Mathf.Approximately(Hunger, _lastHunger))
        {
            _lastHunger = Hunger;
            if (HungerBar  != null) HungerBar.value = Hunger;
            if (HungerText != null) HungerText.text  = Mathf.CeilToInt(Hunger).ToString();
        }

        if (!Mathf.Approximately(Thirst, _lastThirst))
        {
            _lastThirst = Thirst;
            if (ThirstBar  != null) ThirstBar.value = Thirst;
            if (ThirstText != null) ThirstText.text  = Mathf.CeilToInt(Thirst).ToString();
        }
    }
}
