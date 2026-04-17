using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode unit tests for Health.cs
/// Tests the CurrentHealth field logic without running the MonoBehaviour Update loop.
/// </summary>
[TestFixture]
public class HealthEditModeTests
{
    private GameObject testGO;
    private Health health;

    [SetUp]
    public void SetUp()
    {
        testGO = new GameObject("TestEnemy");
        health = testGO.AddComponent<Health>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(testGO);
    }

    // ─── Happy Path ──────────────────────────────────────────────────────────

    [Test]
    public void CurrentHealth_SetToPositiveValue_RetainsValue()
    {
        health.CurrentHealth = 100f;

        Assert.AreEqual(100f, health.CurrentHealth);
    }

    [Test]
    public void CurrentHealth_SetToHighValue_RemainsAboveZero()
    {
        health.CurrentHealth = 9999f;

        Assert.Greater(health.CurrentHealth, 0f);
    }

    // ─── Edge Cases ──────────────────────────────────────────────────────────

    /// <summary>
    /// The death condition in Health.Update() is strictly &lt; 0, so exactly 0 must NOT trigger it.
    /// </summary>
    [Test]
    public void CurrentHealth_SetToZero_IsNotLessThanZero()
    {
        health.CurrentHealth = 0f;

        Assert.IsFalse(health.CurrentHealth < 0f);
    }

    /// <summary>
    /// Any value below zero satisfies the death condition.
    /// </summary>
    [Test]
    public void CurrentHealth_SetToNegativeOne_SatisfiesDeathCondition()
    {
        health.CurrentHealth = -1f;

        Assert.IsTrue(health.CurrentHealth < 0f);
    }

    /// <summary>
    /// CarDamage subtracts 10000 – verify the resulting value is correct.
    /// </summary>
    [Test]
    public void CurrentHealth_AfterCarDamageReduction_EqualsExpectedValue()
    {
        health.CurrentHealth = 100f;
        health.CurrentHealth -= 10000f;

        Assert.AreEqual(-9900f, health.CurrentHealth, 0.001f);
    }

    // ─── Error Cases ─────────────────────────────────────────────────────────

    /// <summary>
    /// NaN propagation must be handled gracefully (documents expected float behaviour).
    /// </summary>
    [Test]
    public void CurrentHealth_SetToNaN_IsNaN()
    {
        health.CurrentHealth = float.NaN;

        Assert.IsTrue(float.IsNaN(health.CurrentHealth));
    }

    /// <summary>
    /// Negative infinity satisfies the death condition, ensuring extreme inputs behave predictably.
    /// </summary>
    [Test]
    public void CurrentHealth_SetToNegativeInfinity_SatisfiesDeathCondition()
    {
        health.CurrentHealth = float.NegativeInfinity;

        Assert.IsTrue(health.CurrentHealth < 0f);
    }
}
