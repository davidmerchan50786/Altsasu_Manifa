using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// PlayMode tests for PlayerStats.cs
/// Validates that Hunger/Thirst decrease over time and that the UI bars
/// reflect the correct values.  Mock Slider and Text components are assigned
/// so that TextBarLink() does not throw NullReferenceException during Update().
/// </summary>
[TestFixture]
public class PlayerStatsPlayModeTests
{
    private GameObject playerGO;
    private PlayerStats stats;

    // UI mock GameObjects (cleaned up in TearDown)
    private GameObject hungerBarGO;
    private GameObject thirstBarGO;
    private GameObject healthBarGO;
    private GameObject healthTextGO;
    private GameObject hungerTextGO;
    private GameObject thirstTextGO;

    [SetUp]
    public void SetUp()
    {
        playerGO = new GameObject("Player");
        stats = playerGO.AddComponent<PlayerStats>();

        // Create minimal UI stubs so PlayerStats.Update() does not throw
        hungerBarGO  = new GameObject("HungerBar");
        thirstBarGO  = new GameObject("ThirstBar");
        healthBarGO  = new GameObject("HealthBar");
        healthTextGO = new GameObject("HealthText");
        hungerTextGO = new GameObject("HungerText");
        thirstTextGO = new GameObject("ThirstText");

        stats.HungerBar  = hungerBarGO.AddComponent<Slider>();
        stats.ThirstBar  = thirstBarGO.AddComponent<Slider>();
        stats.HealtBar   = healthBarGO.AddComponent<Slider>();
        stats.HealthText = healthTextGO.AddComponent<Text>();
        stats.HungerText = hungerTextGO.AddComponent<Text>();
        stats.ThirstText = thirstTextGO.AddComponent<Text>();

        stats.Health     = 100f;
        stats.MaxHealth  = 100f;
        stats.Hunger     = 100f;
        stats.Thirst     = 100f;
        stats.HungerRate = 10f;
        stats.ThirstRate = 5f;
    }

    [TearDown]
    public void TearDown()
    {
        Object.Destroy(playerGO);
        Object.Destroy(hungerBarGO);
        Object.Destroy(thirstBarGO);
        Object.Destroy(healthBarGO);
        Object.Destroy(healthTextGO);
        Object.Destroy(hungerTextGO);
        Object.Destroy(thirstTextGO);
    }

    // ─── Happy Path ──────────────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator Needs_HungerDecreasesOverTime_WithPositiveHungerRate()
    {
        float initialHunger = stats.Hunger;

        yield return new WaitForSeconds(0.3f);

        Assert.Less(stats.Hunger, initialHunger,
            "Hunger should decrease when HungerRate > 0.");
    }

    [UnityTest]
    public IEnumerator Needs_ThirstDecreasesOverTime_WithPositiveThirstRate()
    {
        float initialThirst = stats.Thirst;

        yield return new WaitForSeconds(0.3f);

        Assert.Less(stats.Thirst, initialThirst,
            "Thirst should decrease when ThirstRate > 0.");
    }

    // ─── Edge Cases ──────────────────────────────────────────────────────────

    [UnityTest]
    public IEnumerator Needs_ZeroHungerRate_HungerRemainsUnchanged()
    {
        stats.HungerRate = 0f;
        float initialHunger = stats.Hunger;

        yield return new WaitForSeconds(0.3f);

        Assert.AreEqual(initialHunger, stats.Hunger, 0.001f,
            "Hunger must not change when HungerRate is zero.");
    }

    [UnityTest]
    public IEnumerator Needs_ZeroThirstRate_ThirstRemainsUnchanged()
    {
        stats.ThirstRate = 0f;
        float initialThirst = stats.Thirst;

        yield return new WaitForSeconds(0.3f);

        Assert.AreEqual(initialThirst, stats.Thirst, 0.001f,
            "Thirst must not change when ThirstRate is zero.");
    }

    /// <summary>
    /// HealtBar.maxValue must be updated to MaxHealth on every frame via TextBarLink().
    /// </summary>
    [UnityTest]
    public IEnumerator TextBarLink_HealthBarMaxValue_ReflectsMaxHealth()
    {
        stats.MaxHealth = 250f;

        yield return null; // One frame for Update() to run

        Assert.AreEqual(250f, stats.HealtBar.maxValue, 0.001f,
            "Health bar maxValue must equal MaxHealth.");
    }

    // ─── Error Cases ─────────────────────────────────────────────────────────

    /// <summary>
    /// A negative HungerRate causes Hunger to INCREASE, documenting the
    /// unexpected but technically valid consequence of the formula.
    /// </summary>
    [UnityTest]
    public IEnumerator Needs_NegativeHungerRate_HungerIncreases()
    {
        stats.HungerRate = -10f;
        float initialHunger = stats.Hunger;

        yield return new WaitForSeconds(0.3f);

        Assert.Greater(stats.Hunger, initialHunger,
            "Negative HungerRate must cause Hunger to increase (inverted formula).");
    }

    /// <summary>
    /// HealtBar.value must reflect the current Health each frame.
    /// </summary>
    [UnityTest]
    public IEnumerator TextBarLink_HealthBarValue_MatchesCurrentHealth()
    {
        stats.Health = 75f;

        yield return null; // One frame for Update() to run

        Assert.AreEqual(75f, stats.HealtBar.value, 0.001f,
            "Health bar value must equal the current Health.");
    }
}
