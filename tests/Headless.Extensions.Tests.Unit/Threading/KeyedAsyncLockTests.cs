using System.Reflection;
using Headless.Testing.Tests;
using Headless.Threading;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Threading;

// ReSharper disable AccessToDisposedClosure
public sealed class KeyedAsyncLockTests : TestBase
{
    [Fact]
    public async Task should_acquire_lock_for_new_key()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();

        // when
        using var releaser = await keyedLock.LockAsync("key1", AbortToken);

        // then
        releaser.Should().NotBeNull();
    }

    [Fact]
    public void try_lock_should_acquire_free_lock_and_observe_held_lock()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();

        // when — first try wins, second observes the lock as held
        using var first = keyedLock.TryLock("key1");
        using var second = keyedLock.TryLock("key1");

        // then
        first.Should().NotBeNull();
        second.Should().BeNull();
    }

    [Fact]
    public void try_lock_should_reacquire_after_release()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        var first = keyedLock.TryLock("key1");
        first.Should().NotBeNull();

        // when
        first!.Dispose();
        using var second = keyedLock.TryLock("key1");

        // then
        second.Should().NotBeNull();
    }

    [Fact]
    public async Task try_lock_should_observe_lock_held_by_async_acquisition()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        using var asyncHeld = await keyedLock.LockAsync("key1", AbortToken);

        // when
        using var attempt = keyedLock.TryLock("key1");

        // then
        attempt.Should().BeNull();
    }

    [Fact]
    public void try_lock_should_not_interfere_across_keys()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        using var first = keyedLock.TryLock("key1");

        // when
        using var other = keyedLock.TryLock("key2");

        // then
        other.Should().NotBeNull();
    }

    [Fact]
    public async Task should_block_concurrent_access_to_same_key()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        var executionOrder = new List<int>();
        var lock1Acquired = new TaskCompletionSource();
        var lock1Released = new TaskCompletionSource();

        // when
        var task1 = Task.Run(
            async () =>
            {
                using (await keyedLock.LockAsync("shared-key"))
                {
                    executionOrder.Add(1);
                    lock1Acquired.SetResult();
                    await lock1Released.Task;
                }
            },
            AbortToken
        );

        await lock1Acquired.Task;

        var task2 = Task.Run(
            async () =>
            {
                using (await keyedLock.LockAsync("shared-key"))
                {
                    executionOrder.Add(2);
                }
            },
            AbortToken
        );

        // Give task2 time to block
        await Task.Delay(50, AbortToken);

        // Release lock1
        lock1Released.SetResult();
        await Task.WhenAll(task1, task2);

        // then - task2 should only execute after task1 releases
        executionOrder.Should().ContainInOrder(1, 2);
    }

    [Fact]
    public async Task should_allow_concurrent_access_to_different_keys()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        var key1Acquired = new TaskCompletionSource();
        var key2Acquired = new TaskCompletionSource();
        var bothAcquired = new TaskCompletionSource();

        // when
        var task1 = Task.Run(
            async () =>
            {
                using (await keyedLock.LockAsync("key-a"))
                {
                    key1Acquired.SetResult();
                    await bothAcquired.Task;
                }
            },
            AbortToken
        );

        var task2 = Task.Run(
            async () =>
            {
                using (await keyedLock.LockAsync("key-b"))
                {
                    key2Acquired.SetResult();
                    await bothAcquired.Task;
                }
            },
            AbortToken
        );

        // then - both should acquire without blocking
        var bothTask = Task.WhenAll(key1Acquired.Task, key2Acquired.Task);
        var completed = await Task.WhenAny(bothTask, Task.Delay(1000, AbortToken));

        completed.Should().Be(bothTask, "both locks should be acquired concurrently");

        bothAcquired.SetResult();
        await Task.WhenAll(task1, task2);
    }

    [Fact]
    public async Task should_allow_reacquisition_after_release()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();

        // when
        using (await keyedLock.LockAsync("reuse-key", AbortToken))
        {
            // First acquisition
        }

        // Second acquisition should work
        using (await keyedLock.LockAsync("reuse-key", AbortToken))
        {
            // Should not deadlock
        }

        // then - if we got here, test passed
        true.Should().BeTrue();
    }

    [Fact]
    public async Task should_handle_many_concurrent_locks_on_same_key()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        const int taskCount = 10;
        var counter = 0;
        var tasks = new List<Task>();

        // when
        for (var i = 0; i < taskCount; i++)
        {
            tasks.Add(
                Task.Run(
                    async () =>
                    {
                        using (await keyedLock.LockAsync("counter-key", AbortToken))
                        {
                            var current = counter;
                            await Task.Yield(); // Simulate some work
                            counter = current + 1;
                        }
                    },
                    AbortToken
                )
            );
        }

        await Task.WhenAll(tasks);

        // then - no race condition, counter should be exactly taskCount
        counter.Should().Be(taskCount);
    }

    [Fact]
    public async Task should_handle_many_different_keys_concurrently()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        const int keyCount = 100;
        var tasks = new List<Task>();

        // when
        for (var i = 0; i < keyCount; i++)
        {
            var key = $"unique-key-{i}";
            tasks.Add(
                Task.Run(
                    async () =>
                    {
                        using (await keyedLock.LockAsync(key, AbortToken))
                        {
                            await Task.Yield();
                        }
                    },
                    AbortToken
                )
            );
        }

        // then - should complete without deadlock
        var allTasks = Task.WhenAll(tasks);
        var completed = await Task.WhenAny(allTasks, Task.Delay(5000, AbortToken));
        completed.Should().Be(allTasks, "all locks should complete within timeout");
    }

    [Fact]
    public async Task should_cleanup_semaphore_after_all_releasers_disposed()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();

        // when - sequential locks, not overlapping
        using (await keyedLock.LockAsync("cleanup-key-test", AbortToken))
        {
            // First lock
        }

        using (await keyedLock.LockAsync("cleanup-key-test", AbortToken))
        {
            // Second lock - should work because first was released and cleaned up
        }

        // Also test concurrent acquisition followed by cleanup
        var counter = 0;
        var tasks = Enumerable
            .Range(0, 5)
            .Select(_ =>
                Task.Run(async () =>
                {
                    using (await keyedLock.LockAsync("cleanup-key-concurrent"))
                    {
                        Interlocked.Increment(ref counter);
                    }
                })
            );

        await Task.WhenAll(tasks);

        // After all concurrent locks are released, should be able to acquire again
        using (await keyedLock.LockAsync("cleanup-key-concurrent", AbortToken))
        {
            // Should succeed - semaphore was cleaned up after all releasers disposed
        }

        // then
        counter.Should().Be(5);
    }

    [Fact]
    public async Task should_handle_exception_in_critical_section()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        bool lockReleased;

        // when
        try
        {
            using (await keyedLock.LockAsync("exception-key", AbortToken))
            {
                throw new InvalidOperationException("Test exception");
            }
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Try to acquire the same lock - should work if properly released
        using (await keyedLock.LockAsync("exception-key", AbortToken))
        {
            lockReleased = true;
        }

        // then
        lockReleased.Should().BeTrue("lock should be released even after exception");
    }

    [Fact]
    public async Task should_use_ordinal_string_comparison()
    {
        // given - keys that differ only in case should be different locks
        using var keyedLock = new KeyedAsyncLock();
        var key1Acquired = new TaskCompletionSource();
        var key2Acquired = new TaskCompletionSource();
        var bothAcquired = new TaskCompletionSource();

        // when
        var task1 = Task.Run(
            async () =>
            {
                using (await keyedLock.LockAsync("CaseSensitive"))
                {
                    key1Acquired.SetResult();
                    await bothAcquired.Task;
                }
            },
            AbortToken
        );

        var task2 = Task.Run(
            async () =>
            {
                using (await keyedLock.LockAsync("casesensitive"))
                {
                    key2Acquired.SetResult();
                    await bothAcquired.Task;
                }
            },
            AbortToken
        );

        // then - both should acquire without blocking (different keys)
        var bothTask = Task.WhenAll(key1Acquired.Task, key2Acquired.Task);
        var completed = await Task.WhenAny(bothTask, Task.Delay(1000, AbortToken));

        completed.Should().Be(bothTask, "case-different keys should be treated as different locks");

        bothAcquired.SetResult();
        await Task.WhenAll(task1, task2);
    }

    [Fact]
    public async Task should_support_cancellation()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        var lockAcquired = new TaskCompletionSource();
        using var cts = new CancellationTokenSource();

        // when - acquire lock, then try to acquire same key with cancellation
        _ = Task.Run(
            async () =>
            {
                using (await keyedLock.LockAsync("cancel-key", AbortToken))
                {
                    lockAcquired.SetResult();
                    await Task.Delay(5000, AbortToken); // Hold lock for a while
                }
            },
            AbortToken
        );

        await lockAcquired.Task;

        // Cancel before lock is acquired
        cts.CancelAfter(50);

        // then
        // ReSharper disable once AccessToDisposedClosure
        var act = async () => await keyedLock.LockAsync("cancel-key", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_acquire_timeout_lock_for_uncontended_key()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        var timeProvider = new FakeTimeProvider();

        // when
        using var releaser = await keyedLock.LockAsync(
            "timeout-key",
            TimeSpan.FromSeconds(5),
            timeProvider,
            AbortToken
        );

        // then
        releaser.Should().NotBeNull();
    }

    [Fact]
    public async Task should_acquire_timeout_lock_when_holder_releases_before_timeout()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        var timeProvider = new FakeTimeProvider();
        var holder = await keyedLock.LockAsync("contended-timeout-key", AbortToken);
        var waiter = keyedLock.LockAsync("contended-timeout-key", TimeSpan.FromSeconds(5), timeProvider, AbortToken);

        // when
        holder.Dispose();
        using var releaser = await waiter;

        // then
        releaser.Should().NotBeNull();
    }

    [Fact]
    public async Task should_return_null_when_timeout_elapses_before_lock_acquired()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        var timeProvider = new FakeTimeProvider();
        using var holder = await keyedLock.LockAsync("timeout-elapsed-key", AbortToken);

        // when
        var waiter = keyedLock.LockAsync("timeout-elapsed-key", TimeSpan.FromSeconds(5), timeProvider, AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        using var releaser = await waiter;

        // then
        releaser.Should().BeNull();
    }

    [Fact]
    public async Task should_cleanup_ref_count_when_timeout_elapses_before_lock_acquired()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        var timeProvider = new FakeTimeProvider();
        var holder = await keyedLock.LockAsync("timeout-cleanup-key", AbortToken);

        // when
        var waiter = keyedLock.LockAsync("timeout-cleanup-key", TimeSpan.FromSeconds(5), timeProvider, AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        using var timedOut = await waiter;
        holder.Dispose();

        // then
        timedOut.Should().BeNull();

        using (await keyedLock.LockAsync("timeout-cleanup-key", AbortToken))
        {
            // Success - timed-out waiter did not leak a ref count or semaphore acquisition.
        }
    }

    [Fact]
    public async Task should_wait_unbounded_when_timeout_is_infinite()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        var timeProvider = new FakeTimeProvider();
        var holder = await keyedLock.LockAsync("infinite-timeout-key", AbortToken);
        var waiter = keyedLock.LockAsync("infinite-timeout-key", Timeout.InfiniteTimeSpan, timeProvider, AbortToken);

        // when
        timeProvider.Advance(TimeSpan.FromHours(1));
        waiter.IsCompleted.Should().BeFalse();

        holder.Dispose();
        using var releaser = await waiter;

        // then
        releaser.Should().NotBeNull();
    }

    [Fact]
    public async Task should_throw_when_timeout_waiter_is_cancelled()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        var timeProvider = new FakeTimeProvider();
        using var holder = await keyedLock.LockAsync("timeout-cancel-key", AbortToken);
        using var cts = new CancellationTokenSource();

        // when
        var waiter = keyedLock.LockAsync("timeout-cancel-key", TimeSpan.FromSeconds(5), timeProvider, cts.Token);
        await cts.CancelAsync();

        // then
        var act = async () => await waiter;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_cleanup_ref_count_when_timeout_and_caller_cancellation_race()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        var timeProvider = new FakeTimeProvider();
        var holder = await keyedLock.LockAsync("timeout-cancel-race-key", AbortToken);
        using var cts = new CancellationTokenSource();

        // when
        var waiter = keyedLock.LockAsync("timeout-cancel-race-key", TimeSpan.FromSeconds(5), timeProvider, cts.Token);
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();

        try
        {
            _ = await waiter;
        }
        catch (OperationCanceledException)
        {
            // The assertion below is about lock cleanup; either timeout-null or caller cancellation is valid here.
        }

        holder.Dispose();

        // then
        _SemaphoreCount(keyedLock).Should().Be(0);
    }

    [Fact]
    public async Task should_cleanup_ref_count_on_cancellation()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        var lockAcquired = new TaskCompletionSource();
        var canRelease = new TaskCompletionSource();
        using var cts = new CancellationTokenSource();

        // when - hold lock, then cancel another waiter, then release and reacquire
        var holdingTask = Task.Run(
            async () =>
            {
                using (await keyedLock.LockAsync("cancel-cleanup-key", AbortToken))
                {
                    lockAcquired.SetResult();
                    await canRelease.Task;
                }
            },
            AbortToken
        );

        await lockAcquired.Task;

        // Try to acquire with cancellation
        cts.CancelAfter(50);
        try
        {
            using var _ = await keyedLock.LockAsync("cancel-cleanup-key", cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Release the first lock
        canRelease.SetResult();
        await holdingTask;

        // Should be able to acquire again (ref count properly cleaned up after cancellation)
        using (await keyedLock.LockAsync("cancel-cleanup-key", AbortToken))
        {
            // Success - ref count was properly decremented on cancellation
        }

        // then - if we got here, cleanup worked
        true.Should().BeTrue();
    }

    [Fact]
    public async Task should_throw_on_null_key()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();

        // when
        var act = async () => await keyedLock.LockAsync(null!);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_throw_on_empty_key()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();

        // when
        var act = async () => await keyedLock.LockAsync("");

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_handle_double_dispose_gracefully()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        var releaser = await keyedLock.LockAsync("double-dispose-key", AbortToken);

        // when - dispose twice
        releaser.Dispose();
        var act = releaser.Dispose;

        // then - should not throw
        act.Should().NotThrow();
    }

    [Fact]
    public async Task should_handle_double_dispose_without_corrupting_state()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        var releaser = await keyedLock.LockAsync("double-dispose-state-key", AbortToken);

        // when - dispose twice
        releaser.Dispose();
        releaser.Dispose();

        // Should be able to acquire again
        using (await keyedLock.LockAsync("double-dispose-state-key", AbortToken))
        {
            // Success
        }

        // then - if we got here, state is correct
        true.Should().BeTrue();
    }

    [Fact]
    public async Task should_use_instance_based_locking_not_global()
    {
        // given - two different instances with same key should NOT share locks
        using var lock1 = new KeyedAsyncLock();
        using var lock2 = new KeyedAsyncLock();

        var lock1Acquired = new TaskCompletionSource();
        var lock1CanRelease = new TaskCompletionSource();
        var lock2Acquired = new TaskCompletionSource();

        // when
        var task1 = Task.Run(
            async () =>
            {
                using (await lock1.LockAsync("shared-key"))
                {
                    lock1Acquired.SetResult();
                    await lock1CanRelease.Task;
                }
            },
            AbortToken
        );

        await lock1Acquired.Task;

        var task2 = Task.Run(
            async () =>
            {
                using (await lock2.LockAsync("shared-key"))
                {
                    lock2Acquired.SetResult();
                }
            },
            AbortToken
        );

        // then - lock2 should acquire immediately (instance-based locking, not global)
        var lock2AcquiredResult = await Task.WhenAny(lock2Acquired.Task, Task.Delay(500, AbortToken));
        lock2AcquiredResult
            .Should()
            .Be(lock2Acquired.Task, "lock2 should acquire immediately because lock2 has its own lock dictionary");

        lock1CanRelease.SetResult();
        await Task.WhenAll(task1, task2);
    }

    [Fact]
    public async Task should_dispose_all_semaphores_on_dispose()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();

        // Acquire some locks and release them (semaphores should be cleaned up)
        // But also keep a reference to verify disposal behavior
        var releaser1 = await keyedLock.LockAsync("key1", AbortToken);
        var releaser2 = await keyedLock.LockAsync("key2", AbortToken);

        releaser1.Dispose();
        releaser2.Dispose();

        // Acquire more and don't release - these will be cleaned up by Dispose
        // ReSharper disable once NotDisposedResource
        var releaser3 = await keyedLock.LockAsync("key3", AbortToken);

        // when
        var act = keyedLock.Dispose;

        // then - should not throw
        act.Should().NotThrow();

        // Clean up the releaser (should be safe even after parent disposed)
        var act2 = releaser3.Dispose;
        act2.Should().NotThrow();
    }

    [Fact]
    public async Task should_return_null_on_timeout_when_caller_token_is_not_cancellable()
    {
        // given — CancellationToken.None cannot be cancelled, so the single-CTS path is taken
        using var keyedLock = new KeyedAsyncLock();
        var timeProvider = new FakeTimeProvider();
        using var holder = await keyedLock.LockAsync("non-cancellable-timeout-key", AbortToken);

        // when
        var waiter = keyedLock.LockAsync(
            "non-cancellable-timeout-key",
            TimeSpan.FromSeconds(5),
            timeProvider,
            CancellationToken.None
        );

        timeProvider.Advance(TimeSpan.FromSeconds(5));
        using var releaser = await waiter;

        // then
        releaser.Should().BeNull();
    }

    [Fact]
    public async Task should_cleanup_ref_count_on_timeout_when_caller_token_is_not_cancellable()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        var timeProvider = new FakeTimeProvider();
        var holder = await keyedLock.LockAsync("non-cancellable-timeout-cleanup-key", AbortToken);

        // when
        var waiter = keyedLock.LockAsync(
            "non-cancellable-timeout-cleanup-key",
            TimeSpan.FromSeconds(5),
            timeProvider,
            CancellationToken.None
        );

        timeProvider.Advance(TimeSpan.FromSeconds(5));
        using var timedOut = await waiter;
        holder.Dispose();

        // then — no leaked ref count after the non-cancellable-token timeout path
        timedOut.Should().BeNull();
        _SemaphoreCount(keyedLock).Should().Be(0);

        using (await keyedLock.LockAsync("non-cancellable-timeout-cleanup-key", AbortToken))
        {
            // reacquisition succeeds — semaphore was properly cleaned up
        }
    }

    [Fact]
    public async Task should_throw_when_lock_async_called_after_dispose()
    {
        // given
        var keyedLock = new KeyedAsyncLock();
        keyedLock.Dispose();

        // when
        var act = async () => await keyedLock.LockAsync("post-dispose-key");

        // then
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_throw_when_timeout_lock_async_called_after_dispose()
    {
        // given
        var keyedLock = new KeyedAsyncLock();
        var timeProvider = new FakeTimeProvider();
        keyedLock.Dispose();

        // when
        var act = async () =>
            await keyedLock.LockAsync("post-dispose-timeout-key", TimeSpan.FromSeconds(5), timeProvider);

        // then
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void should_throw_when_try_lock_called_after_dispose()
    {
        // given
        var keyedLock = new KeyedAsyncLock();
        keyedLock.Dispose();

        // when
        var act = () => keyedLock.TryLock("post-dispose-try-key");

        // then
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_not_leak_semaphore_after_acquire_and_release()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();

        // when - acquire then release the same key
        using (await keyedLock.LockAsync("leak-check-key", AbortToken))
        {
            // critical section
        }

        // then - the internal map returns to empty (no leaked semaphore)
        _SemaphoreCount(keyedLock).Should().Be(0);
    }

    [Fact]
    public async Task should_not_grow_map_under_repeated_acquire_release()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();

        // when - many sequential acquire/release cycles across distinct keys
        for (var i = 0; i < 200; i++)
        {
            using (await keyedLock.LockAsync($"repeated-key-{i}", AbortToken))
            {
                // critical section
            }
        }

        // then - nothing accumulates; every key was evicted on release
        _SemaphoreCount(keyedLock).Should().Be(0);
    }

    [Fact]
    public void should_not_leak_semaphore_after_try_lock_release()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();

        // when
        var releaser = keyedLock.TryLock("try-leak-check-key");
        releaser.Should().NotBeNull();
        releaser!.Dispose();

        // then
        _SemaphoreCount(keyedLock).Should().Be(0);
    }

    private static int _SemaphoreCount(KeyedAsyncLock keyedLock)
    {
        var shardsField = typeof(KeyedAsyncLock).GetField(
            "_shards",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        var shards = (Array)shardsField!.GetValue(keyedLock)!;

        var total = 0;

        foreach (var shard in shards)
        {
            var mapField = shard!.GetType().GetField("_map", BindingFlags.Instance | BindingFlags.NonPublic);
            var map = (System.Collections.IDictionary)mapField!.GetValue(shard)!;
            total += map.Count;
        }

        return total;
    }
}
