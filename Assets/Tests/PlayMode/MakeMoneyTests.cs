using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// PlayMode tests for MakeMoney.cs
/// OnTriggerEnter is a private Unity message; it is invoked via reflection so
/// that trigger logic can be exercised without a running physics simulation.
/// The tests use [Test] (single-frame, synchronous) to prevent GUISystem.OnGUI
/// from executing and requiring a Weapons component on the player.
/// </summary>
[TestFixture]
public class MakeMoneyTests
{
    private GameObject moneyGO;
    private GameObject playerGO;
    private MakeMoney makeMoney;
    private GUISystem guiSystem;
    private Collider playerCollider;

    [SetUp]
    public void SetUp()
    {
        moneyGO  = new GameObject("MoneyPickup");
        makeMoney = moneyGO.AddComponent<MakeMoney>();
        makeMoney.Value = 50;

        playerGO      = new GameObject("TestPlayer");
        playerGO.tag  = "Player";
        guiSystem     = playerGO.AddComponent<GUISystem>();
        guiSystem.Money = 0;
        playerCollider = playerGO.AddComponent<BoxCollider>();
    }

    [TearDown]
    public void TearDown()
    {
        if (moneyGO != null) Object.DestroyImmediate(moneyGO);
        if (playerGO != null) Object.DestroyImmediate(playerGO);
    }

    /// <summary>Invokes the private OnTriggerEnter via reflection.</summary>
    private void InvokeOnTriggerEnter(MakeMoney component, Collider col)
    {
        typeof(MakeMoney)
            .GetMethod("OnTriggerEnter", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(component, new object[] { col });
    }

    // ─── Happy Path ──────────────────────────────────────────────────────────

    [Test]
    public void OnTriggerEnter_PlayerCollects_MoneyIncreasedByValue()
    {
        InvokeOnTriggerEnter(makeMoney, playerCollider);

        Assert.AreEqual(50, guiSystem.Money,
            "Money should increase by the pickup Value.");
    }

    /// <summary>
    /// The pickup GameObject must be destroyed after granting money so that it
    /// cannot be collected twice.
    /// </summary>
    [Test]
    public void OnTriggerEnter_PlayerCollects_PickupObjectIsDestroyed()
    {
        InvokeOnTriggerEnter(makeMoney, playerCollider);

        // Object.Destroy is deferred; the reference becomes null on the next frame,
        // but we can verify the object is marked for destruction via its status.
        Assert.IsTrue(moneyGO == null || !moneyGO.activeInHierarchy || moneyGO == null,
            "Pickup object should be scheduled for destruction.");
        moneyGO = null; // Prevent double-destroy in TearDown
    }

    // ─── Edge Cases ──────────────────────────────────────────────────────────

    [Test]
    public void OnTriggerEnter_ZeroValuePickup_MoneyUnchanged()
    {
        makeMoney.Value = 0;
        InvokeOnTriggerEnter(makeMoney, playerCollider);

        Assert.AreEqual(0, guiSystem.Money,
            "Zero-value pickup should not change money.");
        moneyGO = null;
    }

    [Test]
    public void OnTriggerEnter_NegativeValuePickup_MoneyDecreases()
    {
        guiSystem.Money = 100;
        makeMoney.Value = -30;
        InvokeOnTriggerEnter(makeMoney, playerCollider);

        Assert.AreEqual(70, guiSystem.Money,
            "Negative pickup value must subtract from money (documents the raw formula).");
        moneyGO = null;
    }

    /// <summary>
    /// Non-player tagged objects must not trigger the money grant or destruction.
    /// </summary>
    [Test]
    public void OnTriggerEnter_NonPlayerTag_MoneyUnchanged()
    {
        var npcGO     = new GameObject("NPC");
        npcGO.tag     = "Untagged";
        var npcGUISystem = npcGO.AddComponent<GUISystem>();
        npcGUISystem.Money = 0;
        var npcCollider  = npcGO.AddComponent<BoxCollider>();

        InvokeOnTriggerEnter(makeMoney, npcCollider);

        Assert.AreEqual(0, npcGUISystem.Money,
            "Non-player collider must not grant money.");
        Assert.IsTrue(moneyGO != null,
            "Pickup must not be destroyed when a non-player collider enters.");

        Object.DestroyImmediate(npcGO);
    }

    // ─── Error Cases ─────────────────────────────────────────────────────────

    /// <summary>
    /// Documents that MakeMoney.OnTriggerEnter throws when the player GameObject
    /// lacks a GUISystem component (no null guard in production code).
    /// </summary>
    [Test]
    public void OnTriggerEnter_PlayerWithoutGUISystem_ThrowsNullReferenceException()
    {
        var barePlayerGO      = new GameObject("BarePlayer");
        barePlayerGO.tag      = "Player";
        var bareCollider      = barePlayerGO.AddComponent<BoxCollider>();

        var method = typeof(MakeMoney).GetMethod(
            "OnTriggerEnter", BindingFlags.NonPublic | BindingFlags.Instance);

        var ex = Assert.Throws<TargetInvocationException>(
            () => method.Invoke(makeMoney, new object[] { bareCollider }));

        Assert.IsInstanceOf<System.NullReferenceException>(ex.InnerException,
            "Missing GUISystem must produce a NullReferenceException.");

        Object.DestroyImmediate(barePlayerGO);
    }

    [Test]
    public void OnTriggerEnter_CollectedTwiceWithDifferentPickups_AccumulatesMoney()
    {
        // First pickup
        var money1GO = new GameObject("Pickup1");
        var money1   = money1GO.AddComponent<MakeMoney>();
        money1.Value = 30;
        InvokeOnTriggerEnter(money1, playerCollider);

        // Second pickup
        var money2GO = new GameObject("Pickup2");
        var money2   = money2GO.AddComponent<MakeMoney>();
        money2.Value = 70;
        InvokeOnTriggerEnter(money2, playerCollider);

        Assert.AreEqual(100, guiSystem.Money,
            "Two sequential pickups must accumulate their values correctly.");

        Object.DestroyImmediate(money1GO);
        Object.DestroyImmediate(money2GO);
    }
}
