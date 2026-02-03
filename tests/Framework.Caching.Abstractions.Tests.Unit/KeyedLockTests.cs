using Framework.Caching;
using Xunit;
using Xunit.v3;

namespace Framework.Caching.Tests.Unit;

#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks
public sealed class KeyedLockTests
{
    private static CancellationToken AbortToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task should_block_concurrent_access_to_same_key()
    {
        // given
        var sut = new KeyedLock();
        var executionOrder = new List<int>();
        var lock1Acquired = new TaskCompletionSource();
        var lock1Released = new TaskCompletionSource();

        // when
        var task1 = Task.Run(
            async () =>
            {
                using (await sut.LockAsync("shared-key", AbortToken))
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
                using (await sut.LockAsync("shared-key", AbortToken))
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
        var sut = new KeyedLock();
        var key1Acquired = new TaskCompletionSource();
        var key2Acquired = new TaskCompletionSource();
        var bothAcquired = new TaskCompletionSource();

        // when
        var task1 = Task.Run(
            async () =>
            {
                using (await sut.LockAsync("key-a", AbortToken))
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
                using (await sut.LockAsync("key-b", AbortToken))
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
    public async Task should_cleanup_semaphore_when_refcount_reaches_zero()
    {
        // given & when - sequential locks, not overlapping
        var sut = new KeyedLock();

        using (await sut.LockAsync("cleanup-key", AbortToken))
        {
            // First lock
        }

        using (await sut.LockAsync("cleanup-key", AbortToken))
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
                    using (await sut.LockAsync("cleanup-key-concurrent", AbortToken))
                    {
                        Interlocked.Increment(ref counter);
                    }
                })
            );

        await Task.WhenAll(tasks);

        // After all concurrent locks are released, should be able to acquire again
        using (await sut.LockAsync("cleanup-key-concurrent", AbortToken))
        {
            // Should succeed - semaphore was cleaned up after all releasers disposed
        }

        // then
        counter.Should().Be(5);
    }

    [Fact]
    public async Task should_handle_cancellation_during_wait()
    {
        // given
        var sut = new KeyedLock();
        var lockAcquired = new TaskCompletionSource();
        using var cts = new CancellationTokenSource();

        // when - acquire lock, then try to acquire same key with cancellation
        _ = Task.Run(
            async () =>
            {
                using (await sut.LockAsync("cancel-key", AbortToken))
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
        var act = async () => await sut.LockAsync("cancel-key", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_handle_many_concurrent_locks_on_same_key()
    {
        // given
        var sut = new KeyedLock();
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
                        using (await sut.LockAsync("counter-key", AbortToken))
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
    public async Task should_release_lock_on_exception_in_protected_code()
    {
        // given
        var sut = new KeyedLock();
        bool lockReleased;

        // when
        try
        {
            using (await sut.LockAsync("exception-key", AbortToken))
            {
                throw new InvalidOperationException("Test exception");
            }
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Try to acquire the same lock - should work if properly released
        using (await sut.LockAsync("exception-key", AbortToken))
        {
            lockReleased = true;
        }

        // then
        lockReleased.Should().BeTrue("lock should be released even after exception");
    }

    [Fact]
    public async Task should_prevent_double_dispose()
    {
        // given
        var sut = new KeyedLock();
        var releaser = await sut.LockAsync("double-dispose-key", AbortToken);

        // when - dispose twice
        releaser.Dispose();
        var act = () => releaser.Dispose();

        // then - should not throw
        act.Should().NotThrow();
    }

    [Fact]
    public async Task should_not_corrupt_state_on_double_dispose()
    {
        // given
        var sut = new KeyedLock();
        var releaser = await sut.LockAsync("double-dispose-state-key", AbortToken);

        // when - dispose twice
        releaser.Dispose();
        releaser.Dispose();

        // Should be able to acquire again
        using (await sut.LockAsync("double-dispose-state-key", AbortToken))
        {
            // Success
        }

        // then - if we got here, state is correct
        true.Should().BeTrue();
    }

    [Fact]
    public async Task should_cleanup_ref_count_on_cancellation()
    {
        // given
        var sut = new KeyedLock();
        var lockAcquired = new TaskCompletionSource();
        var canRelease = new TaskCompletionSource();
        using var cts = new CancellationTokenSource();

        // when - hold lock, then cancel another waiter, then release and reacquire
        var holdingTask = Task.Run(
            async () =>
            {
                using (await sut.LockAsync("cancel-cleanup-key", AbortToken))
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
            using var _ = await sut.LockAsync("cancel-cleanup-key", cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Release the first lock
        canRelease.SetResult();
        await holdingTask;

        // Should be able to acquire again (ref count properly cleaned up after cancellation)
        using (await sut.LockAsync("cancel-cleanup-key", AbortToken))
        {
            // Success - ref count was properly decremented on cancellation
        }

        // then - if we got here, cleanup worked
        true.Should().BeTrue();
    }

    [Fact]
    public async Task should_use_ordinal_string_comparison()
    {
        // given - keys that differ only in case should be different locks
        var sut = new KeyedLock();
        var key1Acquired = new TaskCompletionSource();
        var key2Acquired = new TaskCompletionSource();
        var bothAcquired = new TaskCompletionSource();

        // when
        var task1 = Task.Run(
            async () =>
            {
                using (await sut.LockAsync("CaseSensitive", AbortToken))
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
                using (await sut.LockAsync("casesensitive", AbortToken))
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
    public async Task should_allow_reacquisition_after_release()
    {
        // given & when
        var sut = new KeyedLock();

        using (await sut.LockAsync("reuse-key", AbortToken))
        {
            // First acquisition
        }

        // Second acquisition should work
        using (await sut.LockAsync("reuse-key", AbortToken))
        {
            // Should not deadlock
        }

        // then - if we got here, test passed
        true.Should().BeTrue();
    }

    [Fact]
    public async Task should_handle_many_different_keys_concurrently()
    {
        // given
        var sut = new KeyedLock();
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
                        using (await sut.LockAsync(key, AbortToken))
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
}
