// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Headless.Testing.Tests;
using Headless.Threading;
using Microsoft.Extensions.Time.Testing;

#pragma warning disable REFL009 // The referenced member is not known to exist
namespace Tests.Threading;

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
    public void should_acquire_free_lock_and_observe_held_lock_when_try_lock()
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
    public void should_reacquire_after_release_when_try_lock()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        var first = keyedLock.TryLock("key1");
        first.Should().NotBeNull();

        // when
        first!.Value.Dispose();
        using var second = keyedLock.TryLock("key1");

        // then
        second.Should().NotBeNull();
    }

    [Fact]
    public async Task should_observe_lock_held_by_async_acquisition_when_try_lock()
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
    public void should_not_interfere_across_keys_when_try_lock()
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
        var executionOrder = new ConcurrentQueue<int>();
        var lock1Acquired = new TaskCompletionSource();
        var lock1Released = new TaskCompletionSource();

        // when
        var task1 = Task.Run(
            async () =>
            {
                using (await keyedLock.LockAsync("shared-key"))
                {
                    executionOrder.Enqueue(1);
                    lock1Acquired.SetResult();
                    await lock1Released.Task.Bounded();
                    // Recorded while still holding the lock, so a task2 that got in early lands before it.
                    executionOrder.Enqueue(3);
                }
            },
            AbortToken
        );

        await lock1Acquired.Task.Bounded();

        var task2 = Task.Run(
            async () =>
            {
                using (await keyedLock.LockAsync("shared-key"))
                {
                    executionOrder.Enqueue(2);
                }
            },
            AbortToken
        );

        // A negative window: without mutual exclusion task2 would enter and record 2 here.
        await Task.Delay(50, AbortToken);
        task2.IsCompleted.Should().BeFalse("task2 must wait while task1 holds the key");

        lock1Released.SetResult();
        await Task.WhenAll(task1, task2);

        // then - task2 entered only after task1 left the lock
        executionOrder.Should().Equal(1, 3, 2);
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
                    await bothAcquired.Task.Bounded();
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
                    await bothAcquired.Task.Bounded();
                }
            },
            AbortToken
        );

        // then - both should acquire without blocking
        var bothTask = Task.WhenAll(key1Acquired.Task, key2Acquired.Task);
        // A broken lock blocks forever instead of running late, so the bound only has to outlast a starved pool.
        var completed = await Task.WhenAny(bothTask, Task.Delay(TimeSpan.FromSeconds(30), AbortToken));

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
        // A broken lock blocks forever instead of running late, so the bound only has to outlast a starved pool.
        var completed = await Task.WhenAny(allTasks, Task.Delay(TimeSpan.FromSeconds(30), AbortToken));
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
                    await bothAcquired.Task.Bounded();
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
                    await bothAcquired.Task.Bounded();
                }
            },
            AbortToken
        );

        // then - both should acquire without blocking (different keys)
        var bothTask = Task.WhenAll(key1Acquired.Task, key2Acquired.Task);
        // A broken lock blocks forever instead of running late, so the bound only has to outlast a starved pool.
        var completed = await Task.WhenAny(bothTask, Task.Delay(TimeSpan.FromSeconds(30), AbortToken));

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

        await lockAcquired.Task.Bounded();

        // Cancel before lock is acquired
        cts.CancelAfter(50);

        // then
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
    public async Task should_throw_and_not_leak_entry_when_timeout_lock_is_free_but_token_already_cancelled()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () =>
            await keyedLock.LockAsync(
                "pre-cancelled-free-key",
                TimeSpan.FromSeconds(5),
                TimeProvider.System,
                cts.Token
            );

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        _SemaphoreCount(keyedLock).Should().Be(0);
    }

    [Fact]
    public async Task should_acquire_free_key_without_scheduling_timeout_timer()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        var timeProvider = new TimerCountingTimeProvider();

        // when
        using var releaser = await keyedLock.LockAsync(
            "free-fast-path-key",
            TimeSpan.FromSeconds(5),
            timeProvider,
            AbortToken
        );

        // then
        releaser.Should().NotBeNull();
        timeProvider.TimersCreated.Should().Be(0);
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
                    await canRelease.Task.Bounded();
                }
            },
            AbortToken
        );

        await lockAcquired.Task.Bounded();

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
                    await lock1CanRelease.Task.Bounded();
                }
            },
            AbortToken
        );

        await lock1Acquired.Task.Bounded();

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
        // A broken lock blocks forever instead of running late, so the bound only has to outlast a starved pool.
        var lock2AcquiredResult = await Task.WhenAny(
            lock2Acquired.Task,
            Task.Delay(TimeSpan.FromSeconds(30), AbortToken)
        );
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
        releaser!.Value.Dispose();

        // then
        _SemaphoreCount(keyedLock).Should().Be(0);
    }

    [Fact]
    public async Task should_pool_entry_on_release_and_reuse_it_on_next_acquire()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();

        // when
        using (await keyedLock.LockAsync("pooled-key", AbortToken)) { }

        // then - the entry leaves the live map but stays pooled for the next acquire
        _SemaphoreCount(keyedLock).Should().Be(0);
        _PooledSemaphores(keyedLock).Should().ContainSingle();

        using (await keyedLock.LockAsync("pooled-key", AbortToken))
        {
            _PooledSemaphores(keyedLock).Should().BeEmpty();
            _SemaphoreCount(keyedLock).Should().Be(1);
        }

        _SemaphoreCount(keyedLock).Should().Be(0);
    }

    [Fact]
    public async Task should_exclude_concurrent_holders_when_pooled_entry_is_reused()
    {
        // given - a released key leaves its entry in the pool
        using var keyedLock = new KeyedAsyncLock();

        using (await keyedLock.LockAsync("reused-key", AbortToken)) { }

        _PooledSemaphores(keyedLock).Should().ContainSingle();

        // when - the pooled entry is taken by a new holder
        var holder = await keyedLock.LockAsync("reused-key", AbortToken);
        var waiter = keyedLock.LockAsync("reused-key", AbortToken);

        // then - the reused semaphore still excludes other callers until the holder releases
        using (var attempt = keyedLock.TryLock("reused-key"))
        {
            attempt.Should().BeNull();
        }

        waiter.IsCompleted.Should().BeFalse();

        holder.Dispose();
        using (await waiter) { }

        _SemaphoreCount(keyedLock).Should().Be(0);
    }

    [Fact]
    public async Task should_not_release_new_holder_when_stale_releaser_is_disposed_again()
    {
        // given - the first holder releases, and the key's pooled entry goes to a second holder
        using var keyedLock = new KeyedAsyncLock();
        var first = await keyedLock.LockAsync("stale-releaser-key", AbortToken);
        first.Dispose();
        using var second = await keyedLock.LockAsync("stale-releaser-key", AbortToken);

        // when - the first releaser is disposed a second time
        first.Dispose();

        // then - the second holder still owns the lock
        using var attempt = keyedLock.TryLock("stale-releaser-key");
        attempt.Should().BeNull();
    }

    [Fact]
    public async Task should_dispose_pooled_entries_when_lock_is_disposed()
    {
        // given
        var keyedLock = new KeyedAsyncLock();

        using (await keyedLock.LockAsync("dispose-pooled-key", AbortToken)) { }

        var pooled = _PooledSemaphores(keyedLock).Should().ContainSingle().Subject;

        // when
        keyedLock.Dispose();

        // then
        _PooledSemaphores(keyedLock).Should().BeEmpty();
        var act = () => pooled.Wait(0);
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_bound_pooled_entries_per_shard()
    {
        // given - far more keys held at once than the pool keeps
        using var keyedLock = new KeyedAsyncLock();
        var releasers = new List<KeyedAsyncLock.Releaser>();

        for (var i = 0; i < 2_000; i++)
        {
            releasers.Add(await keyedLock.LockAsync($"bounded-pool-key-{i}", AbortToken));
        }

        // when
        foreach (var releaser in releasers)
        {
            releaser.Dispose();
        }

        // then - every key left the live map and no shard pools more than its cap
        _SemaphoreCount(keyedLock).Should().Be(0);
        _PoolCounts(keyedLock).Should().OnlyContain(count => count > 0 && count <= 16);
    }

    [Fact]
    public void should_do_nothing_when_default_releaser_is_disposed()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        var releaser = default(KeyedAsyncLock.Releaser);

        // when
        var act = releaser.Dispose;

        // then
        act.Should().NotThrow();
        _SemaphoreCount(keyedLock).Should().Be(0);
    }

    [Fact]
    public async Task should_not_release_new_holder_when_stale_copy_of_releaser_is_disposed()
    {
        // given - a copy of the first releaser outlives its release, and the key goes to a second holder
        using var keyedLock = new KeyedAsyncLock();
        var first = await keyedLock.LockAsync("stale-copy-key", AbortToken);
        var copy = first;
        first.Dispose();
        using var second = await keyedLock.LockAsync("stale-copy-key", AbortToken);

        // when
        copy.Dispose();

        // then - the second holder still owns the lock
        using var attempt = keyedLock.TryLock("stale-copy-key");
        attempt.Should().BeNull();
    }

    [Fact]
    public async Task should_complete_synchronously_when_key_is_free()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();

        // when
        var pending = keyedLock.LockAsync("sync-free-key", AbortToken);
        var pendingWithTimeout = keyedLock.LockAsync(
            "sync-free-timeout-key",
            TimeSpan.FromSeconds(5),
            TimeProvider.System,
            AbortToken
        );
        var completedSynchronously = pending.IsCompletedSuccessfully;
        var timeoutCompletedSynchronously = pendingWithTimeout.IsCompletedSuccessfully;

        using (await pending) { }

        using (await pendingWithTimeout) { }

        // then
        completedSynchronously.Should().BeTrue();
        timeoutCompletedSynchronously.Should().BeTrue();
        _SemaphoreCount(keyedLock).Should().Be(0);
    }

    [Fact]
    public async Task should_report_invalid_key_through_returned_task_instead_of_throwing()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();

        // when - the call itself must not throw
        var pending = keyedLock.LockAsync(null!, AbortToken);
        var pendingWithTimeout = keyedLock.LockAsync("", TimeSpan.FromSeconds(5), TimeProvider.System, AbortToken);

        // then
        pending.IsFaulted.Should().BeTrue();
        pendingWithTimeout.IsFaulted.Should().BeTrue();
        var awaitPending = async () => await pending;
        await awaitPending.Should().ThrowAsync<ArgumentException>();
        var awaitPendingWithTimeout = async () => await pendingWithTimeout;
        await awaitPendingWithTimeout.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_report_invalid_timeout_arguments_through_returned_task_instead_of_throwing()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();

        // when
        var negative = keyedLock.LockAsync(
            "bad-timeout-key",
            TimeSpan.FromSeconds(-1),
            TimeProvider.System,
            AbortToken
        );
        var noClock = keyedLock.LockAsync("bad-clock-key", TimeSpan.FromSeconds(5), null!, AbortToken);

        // then
        var awaitNegative = async () => await negative;
        await awaitNegative.Should().ThrowAsync<ArgumentOutOfRangeException>();
        var awaitNoClock = async () => await noClock;
        await awaitNoClock.Should().ThrowAsync<ArgumentNullException>();
        _SemaphoreCount(keyedLock).Should().Be(0);
    }

    [Fact]
    public async Task should_report_disposed_lock_through_returned_task_instead_of_throwing()
    {
        // given
        var keyedLock = new KeyedAsyncLock();
        keyedLock.Dispose();

        // when
        var pending = keyedLock.LockAsync("disposed-key", AbortToken);

        // then
        pending.IsFaulted.Should().BeTrue();
        var awaitPending = async () => await pending;
        await awaitPending.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_report_cancellation_and_not_leak_entry_when_token_already_cancelled_on_free_key()
    {
        // given
        using var keyedLock = new KeyedAsyncLock();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var pending = keyedLock.LockAsync("pre-cancelled-unbounded-key", cts.Token);

        // then
        pending.IsCanceled.Should().BeTrue();
        var awaitPending = async () => await pending;
        await awaitPending.Should().ThrowAsync<OperationCanceledException>();
        _SemaphoreCount(keyedLock).Should().Be(0);
    }

    [Fact]
    public async Task should_not_throw_when_holder_disposes_after_lock_is_disposed()
    {
        // given - a holder still owns a key when the lock is disposed
        var keyedLock = new KeyedAsyncLock();
        var holder = await keyedLock.LockAsync("disposed-while-held-key", AbortToken);

        // when
        keyedLock.Dispose();
        var act = holder.Dispose;

        // then - releasing after disposal is a no-op, not an ObjectDisposedException from the semaphore
        act.Should().NotThrow();
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

    private static Array _Shards(KeyedAsyncLock keyedLock)
    {
        var shardsField = typeof(KeyedAsyncLock).GetField(
            "_shards",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );

        return (Array)shardsField!.GetValue(keyedLock)!;
    }

    private static List<int> _PoolCounts(KeyedAsyncLock keyedLock)
    {
        var counts = new List<int>();

        foreach (var shard in _Shards(keyedLock))
        {
            var countField = shard!.GetType().GetField("_poolCount", BindingFlags.Instance | BindingFlags.NonPublic);
            counts.Add((int)countField!.GetValue(shard)!);
        }

        return counts;
    }

    private static List<SemaphoreSlim> _PooledSemaphores(KeyedAsyncLock keyedLock)
    {
        var semaphores = new List<SemaphoreSlim>();

        foreach (var shard in _Shards(keyedLock))
        {
            var shardType = shard!.GetType();
            var pool = (Array?)
                shardType.GetField("_pool", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shard);
            var count = (int)
                shardType.GetField("_poolCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shard)!;

            for (var i = 0; i < count; i++)
            {
                var entry = pool!.GetValue(i)!;
                var semaphore = entry.GetType().GetProperty("Semaphore")!.GetValue(entry);
                semaphores.Add((SemaphoreSlim)semaphore!);
            }
        }

        return semaphores;
    }

    private sealed class TimerCountingTimeProvider : TimeProvider
    {
        private int _timersCreated;

        public int TimersCreated => _timersCreated;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref _timersCreated);

            return base.CreateTimer(callback, state, dueTime, period);
        }
    }
}
