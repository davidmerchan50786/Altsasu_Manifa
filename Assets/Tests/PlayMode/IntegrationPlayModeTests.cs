using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Integration tests that exercise combinations of game scripts together.
///
/// Covered scenarios:
///   1. CarDamage → Health  (car collision causes death condition)
///   2. MakeMoney → GUISystem (multiple pickups accumulate correctly)
///   3. TestSpawner → AutoDestroy (spawned objects self-destruct)
/// </summary>
[TestFixture]
public class IntegrationPlayModeTests
{
    // ─── Integration 1: CarDamage + Health ───────────────────────────────────

    /// <summary>
    /// When CarDamage.OnTriggerEnter fires on an Enemy, the Health component's
    /// CurrentHealth must drop below zero, satisfying the death condition that
    /// Health.Update() checks each frame.
    /// </summary>
    [Test]
    public void CarDamageAndHealth_EnemyHit_HealthDropsBelowDeathThreshold()
    {
        var carGO     = new GameObject("Car");
        var carDamage = carGO.AddComponent<CarDamage>();

        var enemyGO    = new GameObject("Enemy");
        enemyGO.tag    = "Enemy";
        var health     = enemyGO.AddComponent<Health>();
        health.CurrentHealth = 100f;
        var enemyCollider    = enemyGO.AddComponent<BoxCollider>();

        // Invoke OnTriggerEnter via reflection
        typeof(CarDamage)
            .GetMethod("OnTriggerEnter", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(carDamage, new object[] { enemyCollider });

        Assert.Less(health.CurrentHealth, 0f,
            "Health must be below zero so the death branch in Health.Update() triggers.");

        Object.DestroyImmediate(carGO);
        Object.DestroyImmediate(enemyGO);
    }

    /// <summary>
    /// The combined effect of CarDamage.OnTriggerEnter (which subtracts 10000)
    /// on an enemy with 100 HP must yield exactly -9900.
    /// </summary>
    [Test]
    public void CarDamageAndHealth_EnemyAt100HP_HealthBecomesMinusTenThousandPlusInitial()
    {
        var carGO     = new GameObject("Car");
        var carDamage = carGO.AddComponent<CarDamage>();

        var enemyGO    = new GameObject("Enemy");
        enemyGO.tag    = "Enemy";
        var health     = enemyGO.AddComponent<Health>();
        health.CurrentHealth = 100f;
        var enemyCollider    = enemyGO.AddComponent<BoxCollider>();

        typeof(CarDamage)
            .GetMethod("OnTriggerEnter", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(carDamage, new object[] { enemyCollider });

        Assert.AreEqual(-9900f, health.CurrentHealth, 0.001f);

        Object.DestroyImmediate(carGO);
        Object.DestroyImmediate(enemyGO);
    }

    // ─── Integration 2: MakeMoney + GUISystem ────────────────────────────────

    /// <summary>
    /// Picking up several money objects in sequence must accumulate the correct
    /// total in GUISystem.Money.
    /// </summary>
    [Test]
    public void MakeMoneyAndGUISystem_ThreePickups_AccumulatesTotalCorrectly()
    {
        var playerGO      = new GameObject("Player");
        playerGO.tag      = "Player";
        var guiSystem     = playerGO.AddComponent<GUISystem>();
        guiSystem.Money   = 0;
        var playerCollider = playerGO.AddComponent<BoxCollider>();

        int[] values = { 10, 25, 65 };
        foreach (int val in values)
        {
            var pickupGO  = new GameObject("Pickup");
            var makeMoney = pickupGO.AddComponent<MakeMoney>();
            makeMoney.Value = val;

            typeof(MakeMoney)
                .GetMethod("OnTriggerEnter", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(makeMoney, new object[] { playerCollider });

            Object.DestroyImmediate(pickupGO);
        }

        Assert.AreEqual(100, guiSystem.Money,
            "Three pickups with values 10+25+65 must sum to 100.");

        Object.DestroyImmediate(playerGO);
    }

    /// <summary>
    /// Money starting at a non-zero balance must correctly add the pickup value.
    /// </summary>
    [Test]
    public void MakeMoneyAndGUISystem_WithExistingBalance_AddsPickupValueCorrectly()
    {
        var playerGO       = new GameObject("Player");
        playerGO.tag       = "Player";
        var guiSystem      = playerGO.AddComponent<GUISystem>();
        guiSystem.Money    = 200;
        var playerCollider = playerGO.AddComponent<BoxCollider>();

        var pickupGO  = new GameObject("Pickup");
        var makeMoney = pickupGO.AddComponent<MakeMoney>();
        makeMoney.Value = 50;

        typeof(MakeMoney)
            .GetMethod("OnTriggerEnter", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(makeMoney, new object[] { playerCollider });

        Assert.AreEqual(250, guiSystem.Money);

        Object.DestroyImmediate(playerGO);
        Object.DestroyImmediate(pickupGO);
    }

    // ─── Integration 3: TestSpawner + AutoDestroy ────────────────────────────

    /// <summary>
    /// A prefab that carries an AutoDestroy component must be destroyed after
    /// its timeout when spawned via TestSpawner.Spawn().
    /// </summary>
    [UnityTest]
    public IEnumerator TestSpawnerAndAutoDestroy_SpawnedPrefab_SelfDestructsAfterTimeout()
    {
        // Build a runtime "prefab" (plain inactive GameObject used as template)
        var prefabTemplate = new GameObject("SpawnedObj");
        prefabTemplate.AddComponent<AutoDestroy>().TimeToDestroy = 0.2f;
        prefabTemplate.SetActive(false); // keep inactive until spawned

        var spawnerGO   = new GameObject("Spawner");
        var spawner     = spawnerGO.AddComponent<TestSpawner>();
        spawner.Spawnee = prefabTemplate;

        var spawnPosGO        = new GameObject("SpawnPos");
        spawner.SpawnPos      = spawnPosGO.transform;
        spawner.SpawnPos.position = Vector3.zero;

        // Activate the prefab template AFTER assigning it so Awake runs on the clone
        prefabTemplate.SetActive(true);

        // Spawn
        spawner.Spawn();

        // Find the spawned clone by tag/name – Instantiate creates an active copy
        // The spawned copy should auto-destroy after 0.2 s
        yield return new WaitForSeconds(0.5f);

        // All "SpawnedObj(Clone)" objects should be gone
        var remaining = Object.FindFirstObjectByType<AutoDestroy>();
        // The template was re-activated above; destroy it manually to avoid interference
        Object.Destroy(prefabTemplate);

        // remaining could be the template itself if it wasn't destroyed yet.
        // After the Destroy call above we wait one frame to let it process.
        yield return null;

        Assert.IsNull(Object.FindFirstObjectByType<AutoDestroy>(),
            "All spawned AutoDestroy objects must have been destroyed after their timeout.");

        Object.Destroy(spawnerGO);
        Object.Destroy(spawnPosGO);
    }
}
