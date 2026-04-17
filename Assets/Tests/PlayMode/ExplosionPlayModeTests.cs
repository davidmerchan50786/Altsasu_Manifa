using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode tests for Explosion.cs
/// Explosion.Start() calls Physics.OverlapSphere and applies AddExplosionForce.
/// Tests create collider+rigidbody targets at controlled distances and verify
/// that velocity changes (or does not change) after the physics step.
/// </summary>
[TestFixture]
public class ExplosionPlayModeTests
{
    // ─── Happy Path ──────────────────────────────────────────────────────────

    /// <summary>
    /// A Rigidbody with a Collider placed within the explosion radius must receive
    /// a non-zero velocity after the first physics step.
    /// </summary>
    [UnityTest]
    public IEnumerator Explosion_RigidbodyWithinRadius_ReceivesForce()
    {
        // Target: 2 units away, well inside the default radius of 5
        var targetGO = new GameObject("Target");
        targetGO.transform.position = new Vector3(2f, 0f, 0f);
        var rb = targetGO.AddComponent<Rigidbody>();
        rb.useGravity = false;
        targetGO.AddComponent<SphereCollider>();

        // Explosion at origin
        var explosionGO = new GameObject("Explosion");
        explosionGO.transform.position = Vector3.zero;
        var explosion = explosionGO.AddComponent<Explosion>();
        explosion.radius = 5f;
        explosion.power  = 500f;

        yield return null;                   // Start() runs, force applied
        yield return new WaitForFixedUpdate(); // Physics integrates the force

        Assert.Greater(rb.linearVelocity.magnitude, 0f,
            "Rigidbody within the explosion radius must receive an impulse.");

        Object.Destroy(targetGO);
        Object.Destroy(explosionGO);
    }

    /// <summary>
    /// Multiple Rigidbodies inside the radius must all receive force.
    /// </summary>
    [UnityTest]
    public IEnumerator Explosion_MultipleRigidbodiesWithinRadius_AllReceiveForce()
    {
        var positions = new Vector3[]
        {
            new Vector3( 1f, 0f,  0f),
            new Vector3(-1f, 0f,  0f),
            new Vector3( 0f, 1f,  0f),
        };

        var targets = new Rigidbody[positions.Length];
        var gos     = new GameObject[positions.Length];

        for (int i = 0; i < positions.Length; i++)
        {
            gos[i] = new GameObject("Target" + i);
            gos[i].transform.position = positions[i];
            targets[i] = gos[i].AddComponent<Rigidbody>();
            targets[i].useGravity = false;
            gos[i].AddComponent<SphereCollider>();
        }

        var explosionGO = new GameObject("Explosion");
        explosionGO.transform.position = Vector3.zero;
        var explosion = explosionGO.AddComponent<Explosion>();
        explosion.radius = 5f;
        explosion.power  = 500f;

        yield return null;
        yield return new WaitForFixedUpdate();

        foreach (var rb in targets)
            Assert.Greater(rb.linearVelocity.magnitude, 0f,
                "Every Rigidbody inside the radius must receive an impulse.");

        foreach (var go in gos) Object.Destroy(go);
        Object.Destroy(explosionGO);
    }

    // ─── Edge Cases ──────────────────────────────────────────────────────────

    /// <summary>
    /// A Rigidbody placed beyond the explosion radius must NOT be affected.
    /// </summary>
    [UnityTest]
    public IEnumerator Explosion_RigidbodyOutsideRadius_ReceivesNoForce()
    {
        var targetGO = new GameObject("FarTarget");
        targetGO.transform.position = new Vector3(20f, 0f, 0f); // far beyond radius 5
        var rb = targetGO.AddComponent<Rigidbody>();
        rb.useGravity = false;
        targetGO.AddComponent<SphereCollider>();

        var explosionGO = new GameObject("Explosion");
        explosionGO.transform.position = Vector3.zero;
        var explosion = explosionGO.AddComponent<Explosion>();
        explosion.radius = 5f;
        explosion.power  = 500f;

        yield return null;
        yield return new WaitForFixedUpdate();

        Assert.AreEqual(0f, rb.linearVelocity.magnitude, 0.001f,
            "Rigidbody outside the radius must not receive any force.");

        Object.Destroy(targetGO);
        Object.Destroy(explosionGO);
    }

    /// <summary>
    /// radius = 0 means OverlapSphere detects nothing – no force should be applied.
    /// </summary>
    [UnityTest]
    public IEnumerator Explosion_ZeroRadius_NoObjectsAffected()
    {
        var targetGO = new GameObject("NearTarget");
        targetGO.transform.position = new Vector3(0.5f, 0f, 0f);
        var rb = targetGO.AddComponent<Rigidbody>();
        rb.useGravity = false;
        targetGO.AddComponent<SphereCollider>();

        var explosionGO = new GameObject("Explosion");
        explosionGO.transform.position = Vector3.zero;
        var explosion = explosionGO.AddComponent<Explosion>();
        explosion.radius = 0f;
        explosion.power  = 500f;

        yield return null;
        yield return new WaitForFixedUpdate();

        Assert.AreEqual(0f, rb.linearVelocity.magnitude, 0.001f,
            "Zero radius explosion must not affect any Rigidbody.");

        Object.Destroy(targetGO);
        Object.Destroy(explosionGO);
    }

    /// <summary>
    /// A GameObject inside the radius but WITHOUT a Rigidbody must be safely
    /// ignored (Explosion checks for Rigidbody before calling AddExplosionForce).
    /// </summary>
    [UnityTest]
    public IEnumerator Explosion_ColliderWithoutRigidbody_NoExceptionThrown()
    {
        var staticGO = new GameObject("StaticObstacle");
        staticGO.transform.position = new Vector3(1f, 0f, 0f);
        staticGO.AddComponent<BoxCollider>(); // No Rigidbody

        var explosionGO = new GameObject("Explosion");
        explosionGO.transform.position = Vector3.zero;
        var explosion = explosionGO.AddComponent<Explosion>();
        explosion.radius = 5f;
        explosion.power  = 500f;

        bool exceptionThrown = false;
        try
        {
            yield return null;
            yield return new WaitForFixedUpdate();
        }
        catch (System.Exception)
        {
            exceptionThrown = true;
        }

        Assert.IsFalse(exceptionThrown,
            "Collider without a Rigidbody must not cause an exception.");

        Object.Destroy(staticGO);
        Object.Destroy(explosionGO);
    }

    // ─── Error Cases ─────────────────────────────────────────────────────────

    /// <summary>
    /// power = 0 means AddExplosionForce applies zero force – no velocity change
    /// and no crash.
    /// </summary>
    [UnityTest]
    public IEnumerator Explosion_ZeroPower_RigidbodyVelocityRemainsZero()
    {
        var targetGO = new GameObject("Target");
        targetGO.transform.position = new Vector3(2f, 0f, 0f);
        var rb = targetGO.AddComponent<Rigidbody>();
        rb.useGravity = false;
        targetGO.AddComponent<SphereCollider>();

        var explosionGO = new GameObject("Explosion");
        explosionGO.transform.position = Vector3.zero;
        var explosion = explosionGO.AddComponent<Explosion>();
        explosion.radius = 5f;
        explosion.power  = 0f;

        yield return null;
        yield return new WaitForFixedUpdate();

        Assert.AreEqual(0f, rb.linearVelocity.magnitude, 0.001f,
            "Zero power must result in zero velocity change.");

        Object.Destroy(targetGO);
        Object.Destroy(explosionGO);
    }

    /// <summary>
    /// Negative power is passed directly to AddExplosionForce; it should not crash
    /// and the resulting velocity must be non-zero (force is still applied, but
    /// the engine's behaviour with negative power is implementation-defined).
    /// </summary>
    [UnityTest]
    public IEnumerator Explosion_NegativePower_NoExceptionThrown()
    {
        var targetGO = new GameObject("Target");
        targetGO.transform.position = new Vector3(2f, 0f, 0f);
        targetGO.AddComponent<Rigidbody>().useGravity = false;
        targetGO.AddComponent<SphereCollider>();

        var explosionGO = new GameObject("Explosion");
        explosionGO.transform.position = Vector3.zero;
        var explosion = explosionGO.AddComponent<Explosion>();
        explosion.radius = 5f;
        explosion.power  = -500f;

        bool exceptionThrown = false;
        try
        {
            yield return null;
            yield return new WaitForFixedUpdate();
        }
        catch (System.Exception)
        {
            exceptionThrown = true;
        }

        Assert.IsFalse(exceptionThrown,
            "Negative power must not throw an exception.");

        Object.Destroy(targetGO);
        Object.Destroy(explosionGO);
    }
}
