using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode tests for AutoDestroy.cs
/// Verifies that objects are scheduled for destruction at the correct time.
/// </summary>
[TestFixture]
public class AutoDestroyPlayModeTests
{
    // ─── Happy Path ──────────────────────────────────────────────────────────

    /// <summary>Object must still exist well before the timeout expires.</summary>
    [UnityTest]
    public IEnumerator AutoDestroy_BeforeTimeout_ObjectStillExists()
    {
        var go = new GameObject("AutoDestroyTest");
        var autoDestroy = go.AddComponent<AutoDestroy>();
        autoDestroy.TimeToDestroy = 5f;

        yield return new WaitForSeconds(0.1f);

        Assert.IsTrue(go != null, "Object should still exist before timeout.");

        Object.Destroy(go);
        yield return null;
    }

    /// <summary>Object must be destroyed after the configured timeout has elapsed.</summary>
    [UnityTest]
    public IEnumerator AutoDestroy_AfterTimeout_ObjectIsDestroyed()
    {
        var go = new GameObject("AutoDestroyTest");
        var autoDestroy = go.AddComponent<AutoDestroy>();
        autoDestroy.TimeToDestroy = 0.2f;

        yield return new WaitForSeconds(0.5f);

        // Unity overrides == operator for destroyed objects
        Assert.IsTrue(go == null, "Object should have been destroyed.");
    }

    // ─── Edge Cases ──────────────────────────────────────────────────────────

    /// <summary>
    /// TimeToDestroy = 0 → Unity destroys at end of the current frame.
    /// After one WaitForSeconds the object must be gone.
    /// </summary>
    [UnityTest]
    public IEnumerator AutoDestroy_TimeToDestroyZero_ObjectDestroyedOnFirstFrame()
    {
        var go = new GameObject("AutoDestroyTest");
        var autoDestroy = go.AddComponent<AutoDestroy>();
        autoDestroy.TimeToDestroy = 0f;

        yield return new WaitForSeconds(0.1f);

        Assert.IsTrue(go == null, "Object with TimeToDestroy=0 should be destroyed.");
    }

    /// <summary>Very short but non-zero timeout must also destroy the object.</summary>
    [UnityTest]
    public IEnumerator AutoDestroy_VeryShortTimeout_ObjectDestroyed()
    {
        var go = new GameObject("AutoDestroyTest");
        var autoDestroy = go.AddComponent<AutoDestroy>();
        autoDestroy.TimeToDestroy = 0.05f;

        yield return new WaitForSeconds(0.3f);

        Assert.IsTrue(go == null, "Object with a very short timeout should be destroyed.");
    }

    /// <summary>
    /// Disabling the component AFTER Start() has already scheduled destruction must
    /// NOT cancel the scheduled Destroy call.
    /// </summary>
    [UnityTest]
    public IEnumerator AutoDestroy_ComponentDisabledAfterStart_ObjectStillDestroyed()
    {
        var go = new GameObject("AutoDestroyTest");
        var autoDestroy = go.AddComponent<AutoDestroy>();
        autoDestroy.TimeToDestroy = 0.3f;

        yield return null; // Start() runs, Destroy(gameObject, 0.3f) is scheduled

        autoDestroy.enabled = false; // Too late to cancel the scheduled destruction

        yield return new WaitForSeconds(0.5f);

        Assert.IsTrue(go == null,
            "Scheduled destruction must not be cancellable by disabling the component.");
    }

    // ─── Error Cases ─────────────────────────────────────────────────────────

    /// <summary>
    /// Negative TimeToDestroy is handled without throwing an exception.
    /// Unity treats negative delay the same as 0 (destroys at end of frame).
    /// </summary>
    [UnityTest]
    public IEnumerator AutoDestroy_NegativeTimeToDestroy_NoExceptionAndObjectDestroyed()
    {
        var go = new GameObject("AutoDestroyTest");
        var autoDestroy = go.AddComponent<AutoDestroy>();
        autoDestroy.TimeToDestroy = -1f;

        yield return new WaitForSeconds(0.2f);

        // No exception must have been thrown; object should be gone
        Assert.IsTrue(go == null,
            "Negative TimeToDestroy should behave like 0 and destroy the object.");
    }

    /// <summary>
    /// Two AutoDestroy components on the same object must not cause a double-destroy crash.
    /// </summary>
    [UnityTest]
    public IEnumerator AutoDestroy_MultipleComponentsOnSameObject_NoException()
    {
        var go = new GameObject("AutoDestroyDouble");
        go.AddComponent<AutoDestroy>().TimeToDestroy = 0.1f;
        go.AddComponent<AutoDestroy>().TimeToDestroy = 0.2f;

        // Adding two components with separate TimeToDestroy values should not throw
        bool exceptionThrown = false;
        try
        {
            yield return new WaitForSeconds(0.5f);
        }
        catch (System.Exception)
        {
            exceptionThrown = true;
        }

        Assert.IsFalse(exceptionThrown,
            "Multiple AutoDestroy components must not cause an exception.");
    }
}
