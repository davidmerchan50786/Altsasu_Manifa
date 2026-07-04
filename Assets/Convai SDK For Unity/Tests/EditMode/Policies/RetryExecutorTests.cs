using System;
using System.Threading;
using System.Threading.Tasks;
using Convai.Runtime.Core.Policies;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Policies
{
    /// <summary>
    ///     Minimal unit tests for RetryExecutor - 5 key scenarios only.
    /// </summary>
    [TestFixture]
    public class RetryExecutorTests
    {
        private RetryExecutor _executor;

        [SetUp]
        public void SetUp()
        {
            _executor = new RetryExecutor(new ExponentialBackoffPolicy());
        }

        [Test]
        public async Task ExecuteAsync_SucceedsOnFirstAttempt_ReturnsResult()
        {
            int result = await _executor.ExecuteAsync(
                (_, _) => Task.FromResult(42),
                CancellationToken.None);

            Assert.AreEqual(42, result);
        }

        [Test]
        public async Task ExecuteAsync_FailsThenSucceeds_RetriesAndReturns()
        {
            int attemptCount = 0;

            int result = await _executor.ExecuteAsync(
                (attempt, _) =>
                {
                    attemptCount++;
                    if (attempt < 2)
                        throw new TimeoutException("Simulated timeout");
                    return Task.FromResult(99);
                },
                CancellationToken.None);

            Assert.AreEqual(99, result);
            Assert.AreEqual(3, attemptCount);
        }

        [Test]
        public void ExecuteAsync_ExhaustsRetries_ThrowsLastException()
        {
            // Unity NUnit: ThrowsAsync<T> returns T (runs async delegate to completion), not Task<T>.
            TimeoutException ex = Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await _executor.ExecuteAsync<int>(
                    (attempt, _) => throw new TimeoutException($"Attempt {attempt}"),
                    CancellationToken.None);
            });

            Assert.That(ex.Message, Does.Contain("Attempt 3"));
        }

        [Test]
        public void ExecuteAsync_NonTransientError_DoesNotRetry()
        {
            int attemptCount = 0;

            Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await _executor.ExecuteAsync<int>(
                    (_, _) =>
                    {
                        attemptCount++;
                        throw new ArgumentException("Bad argument");
                    },
                    CancellationToken.None);
            });

            Assert.AreEqual(1, attemptCount);
        }

        [Test]
        public void ExecuteAsync_Cancelled_ThrowsOperationCanceled()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await _executor.ExecuteAsync(
                    (_, _) => Task.FromResult(1),
                    cts.Token);
            });
        }
    }
}

