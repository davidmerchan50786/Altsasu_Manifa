using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode unit tests for CarDamage.cs
/// OnTriggerEnter is a private Unity message, so it is invoked via reflection
/// to avoid the need for a full physics simulation.
/// </summary>
[TestFixture]
public class CarDamageEditModeTests
{
    private GameObject carGO;
    private GameObject enemyGO;
    private CarDamage carDamage;
    private Health enemyHealth;
    private Collider enemyCollider;

    [SetUp]
    public void SetUp()
    {
        carGO = new GameObject("Car");
        carDamage = carGO.AddComponent<CarDamage>();

        enemyGO = new GameObject("Enemy");
        enemyGO.tag = "Enemy";
        enemyHealth = enemyGO.AddComponent<Health>();
        enemyHealth.CurrentHealth = 100f;
        enemyCollider = enemyGO.AddComponent<BoxCollider>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(carGO);
        Object.DestroyImmediate(enemyGO);
    }

    /// <summary>Invokes the private OnTriggerEnter via reflection.</summary>
    private void InvokeOnTriggerEnter(CarDamage component, Collider col)
    {
        typeof(CarDamage)
            .GetMethod("OnTriggerEnter", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(component, new object[] { col });
    }

    // ─── Happy Path ──────────────────────────────────────────────────────────

    [Test]
    public void OnTriggerEnter_WithEnemy_ReducesHealthBy10000()
    {
        float initialHealth = enemyHealth.CurrentHealth;
        InvokeOnTriggerEnter(carDamage, enemyCollider);

        Assert.AreEqual(initialHealth - 10000f, enemyHealth.CurrentHealth, 0.001f);
    }

    [Test]
    public void OnTriggerEnter_WithEnemy_HealthDropsBelowZero()
    {
        InvokeOnTriggerEnter(carDamage, enemyCollider);

        Assert.Less(enemyHealth.CurrentHealth, 0f);
    }

    // ─── Edge Cases ──────────────────────────────────────────────────────────

    [Test]
    public void OnTriggerEnter_WithEnemyAtZeroHealth_BecomesMassivelyNegative()
    {
        enemyHealth.CurrentHealth = 0f;
        InvokeOnTriggerEnter(carDamage, enemyCollider);

        Assert.AreEqual(-10000f, enemyHealth.CurrentHealth, 0.001f);
    }

    [Test]
    public void OnTriggerEnter_WithAlreadyDeadEnemy_HealthDecreasesFurther()
    {
        enemyHealth.CurrentHealth = -5000f;
        InvokeOnTriggerEnter(carDamage, enemyCollider);

        Assert.AreEqual(-15000f, enemyHealth.CurrentHealth, 0.001f);
    }

    /// <summary>
    /// Non-enemy colliders must be ignored; the Health component on the neutral
    /// object must remain untouched.
    /// </summary>
    [Test]
    public void OnTriggerEnter_WithNonEnemyCollider_HealthUnchanged()
    {
        var neutralGO = new GameObject("Neutral");
        neutralGO.tag = "Untagged";
        var neutralHealth = neutralGO.AddComponent<Health>();
        neutralHealth.CurrentHealth = 100f;
        var neutralCollider = neutralGO.AddComponent<BoxCollider>();

        InvokeOnTriggerEnter(carDamage, neutralCollider);

        Assert.AreEqual(100f, neutralHealth.CurrentHealth, 0.001f);
        Object.DestroyImmediate(neutralGO);
    }

    // ─── Error Cases ─────────────────────────────────────────────────────────

    /// <summary>
    /// Documents a known bug: CarDamage.OnTriggerEnter has no null-guard for the
    /// Health component, so it throws when the component is missing.
    /// </summary>
    [Test]
    public void OnTriggerEnter_EnemyWithoutHealthComponent_ThrowsNullReferenceException()
    {
        var badEnemyGO = new GameObject("BadEnemy");
        badEnemyGO.tag = "Enemy";
        var badCollider = badEnemyGO.AddComponent<BoxCollider>();

        var method = typeof(CarDamage).GetMethod(
            "OnTriggerEnter", BindingFlags.NonPublic | BindingFlags.Instance);

        var ex = Assert.Throws<TargetInvocationException>(
            () => method.Invoke(carDamage, new object[] { badCollider }));

        Assert.IsInstanceOf<System.NullReferenceException>(ex.InnerException);
        Object.DestroyImmediate(badEnemyGO);
    }

    /// <summary>
    /// float.MaxValue minus 10000 still yields a very large positive value due to
    /// floating-point precision – the component must not crash or overflow to infinity.
    /// </summary>
    [Test]
    public void OnTriggerEnter_WithEnemyAtFloatMaxHealth_DoesNotCrash()
    {
        enemyHealth.CurrentHealth = float.MaxValue;
        Assert.DoesNotThrow(() => InvokeOnTriggerEnter(carDamage, enemyCollider));
        Assert.IsFalse(float.IsNaN(enemyHealth.CurrentHealth));
    }
}
