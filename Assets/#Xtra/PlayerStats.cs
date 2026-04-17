using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PlayerStats : MonoBehaviour
{

    public float Health;
    public float MaxHealth;

    public float Hunger;
    public float Thirst;

    public float HungerRate;
    public float ThirstRate;

    public Slider HungerBar;
    public Slider ThirstBar;
    public Slider HealtBar;

    public Text HealthText;
    public Text HungerText;
    public Text ThirstText;

    // Previous values — UI only redraws when something actually changed
    private float _prevHealth;
    private float _prevMaxHealth;
    private float _prevHunger;
    private float _prevThirst;

    void Start()
    {
        // Force first-frame UI refresh
        _prevHealth    = float.MinValue;
        _prevMaxHealth = float.MinValue;
        _prevHunger    = float.MinValue;
        _prevThirst    = float.MinValue;
    }


    void Update()
    {
        Needs();
        TextBarLink();
    }

    private void TextBarLink()
    {
        if (Health != _prevHealth || MaxHealth != _prevMaxHealth)
        {
            HealtBar.maxValue = MaxHealth;
            HealtBar.value    = Health;
            HealthText.text   = Health.ToString("f");
            _prevHealth    = Health;
            _prevMaxHealth = MaxHealth;
        }

        if (Thirst != _prevThirst)
        {
            ThirstBar.value  = Thirst;
            ThirstText.text  = Thirst.ToString("f");
            _prevThirst = Thirst;
        }

        if (Hunger != _prevHunger)
        {
            HungerBar.value  = Hunger;
            HungerText.text  = Hunger.ToString("f");
            _prevHunger = Hunger;
        }
    }


    private void Needs()
    {
        Hunger -= HungerRate * Time.deltaTime;
        Thirst -= ThirstRate * Time.deltaTime;
    }
}
