using Framework.Testing.Tests;
using Framework.Threading;

namespace Tests.Threading;

#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks
public sealed class AsyncDuplicateLockTests : TestBase
{
    [Fact]
    public void should_acquire_lock_for_new_key()
    {
        // when
        using var releaser = AsyncDuplicateLock.Lock("key1");

        // then
        releaser.Should().NotBeNull();
    }

    [Fact]
    public async Task should_acquire_lock_async_for_new_key()
    {
        // when
        using var releaser = await AsyncDuplicateLock.LockAsync("key1", AbortToken);

        // then
        releaser.Should().NotBeNull();
    }

    [Fact]
    public async Task should_block_concurrent_access_to_same_key()
    {
        // given
        var executionOrder = new List<int>();
        var lock1Acquired = new TaskCompletionSource();
        var lock1Released = new TaskCompletionSource();

        // when
        var task1 = Task.Run(
            async () =>
            {
                using (await AsyncDuplicateLock.LockAsync("shared-key"))
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
                using (await AsyncDuplicateLock.LockAsync("shared-key"))
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
        var key1Acquired = new TaskCompletionSource();
        var key2Acquired = new TaskCompletionSource();
        var bothAcquired = new TaskCompletionSource();

        // when
        var task1 = Task.Run(
            async () =>
            {
                using (await AsyncDuplicateLock.LockAsync("key-a"))
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
                using (await AsyncDuplicateLock.LockAsync("key-b"))
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
        // given & when
        using (await AsyncDuplicateLock.LockAsync("reuse-key", AbortToken))
        {
            // First acquisition
        }

        // Second acquisition should work
        using (await AsyncDuplicateLock.LockAsync("reuse-key", AbortToken))
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
                        using (await AsyncDuplicateLock.LockAsync("counter-key", AbortToken))
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
                        using (await AsyncDuplicateLock.LockAsync(key, AbortToken))
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
    public void should_work_with_sync_lock()
    {
        // given
        var counter = 0;
        var threads = new List<Thread>();

        // when
        for (var i = 0; i < 5; i++)
        {
            var thread = new Thread(() =>
            {
                using (AsyncDuplicateLock.Lock("sync-key"))
                {
                    var current = counter;
                    Thread.Sleep(10);
                    counter = current + 1;
                }
            });
            threads.Add(thread);
            thread.Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        // then
        counter.Should().Be(5);
    }

    [Fact]
    public async Task should_cleanup_semaphore_after_all_releasers_disposed()
    {
        // This test verifies that the semaphore is properly cleaned up after all releasers are disposed
        // and that a new lock can be acquired on the same key

        // given & when - sequential locks, not overlapping
        using (await AsyncDuplicateLock.LockAsync("cleanup-key-test", AbortToken))
        {
            // First lock
        }

        using (await AsyncDuplicateLock.LockAsync("cleanup-key-test", AbortToken))
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
                    using (await AsyncDuplicateLock.LockAsync("cleanup-key-concurrent"))
                    {
                        Interlocked.Increment(ref counter);
                    }
                })
            );

        await Task.WhenAll(tasks);

        // After all concurrent locks are released, should be able to acquire again
        using (await AsyncDuplicateLock.LockAsync("cleanup-key-concurrent", AbortToken))
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
        bool lockReleased;

        // when
        try
        {
            using (await AsyncDuplicateLock.LockAsync("exception-key", AbortToken))
            {
                throw new InvalidOperationException("Test exception");
            }
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Try to acquire the same lock - should work if properly released
        using (await AsyncDuplicateLock.LockAsync("exception-key", AbortToken))
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
        var key1Acquired = new TaskCompletionSource();
        var key2Acquired = new TaskCompletionSource();
        var bothAcquired = new TaskCompletionSource();

        // when
        var task1 = Task.Run(
            async () =>
            {
                using (await AsyncDuplicateLock.LockAsync("CaseSensitive"))
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
                using (await AsyncDuplicateLock.LockAsync("casesensitive"))
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
        var lockAcquired = new TaskCompletionSource();
        using var cts = new CancellationTokenSource();

        // when - acquire lock, then try to acquire same key with cancellation
        _ = Task.Run(
            async () =>
            {
                using (await AsyncDuplicateLock.LockAsync("cancel-key", AbortToken))
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
        var act = async () => await AsyncDuplicateLock.LockAsync("cancel-key", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_cleanup_ref_count_on_cancellation()
    {
        // given
        var lockAcquired = new TaskCompletionSource();
        var canRelease = new TaskCompletionSource();
        using var cts = new CancellationTokenSource();

        // when - hold lock, then cancel another waiter, then release and reacquire
        var holdingTask = Task.Run(
            async () =>
            {
                using (await AsyncDuplicateLock.LockAsync("cancel-cleanup-key"))
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
            using var _ = await AsyncDuplicateLock.LockAsync("cancel-cleanup-key", cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Release the first lock
        canRelease.SetResult();
        await holdingTask;

        // Should be able to acquire again (ref count properly cleaned up after cancellation)
        using (await AsyncDuplicateLock.LockAsync("cancel-cleanup-key", AbortToken))
        {
            // Success - ref count was properly decremented on cancellation
        }

        // then - if we got here, cleanup worked
        true.Should().BeTrue();
    }

    [Fact]
    public void should_throw_on_null_key_sync()
    {
        // when
        var act = () => AsyncDuplicateLock.Lock(null!);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_on_empty_key_sync()
    {
        // when
        var act = () => AsyncDuplicateLock.Lock("");

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task should_throw_on_null_key_async()
    {
        // when
        var act = async () => await AsyncDuplicateLock.LockAsync(null!);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_throw_on_empty_key_async()
    {
        // when
        var act = async () => await AsyncDuplicateLock.LockAsync("");

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_handle_double_dispose_gracefully()
    {
        // given
        var releaser = await AsyncDuplicateLock.LockAsync("double-dispose-key", AbortToken);

        // when - dispose twice
        releaser.Dispose();
        var act = () => releaser.Dispose();

        // then - should not throw
        act.Should().NotThrow();
    }

    [Fact]
    public async Task should_handle_double_dispose_without_corrupting_state()
    {
        // given
        var releaser = await AsyncDuplicateLock.LockAsync("double-dispose-state-key", AbortToken);

        // when - dispose twice
        releaser.Dispose();
        releaser.Dispose();

        // Should be able to acquire again
        using (await AsyncDuplicateLock.LockAsync("double-dispose-state-key", AbortToken))
        {
            // Success
        }

        // then - if we got here, state is correct
        true.Should().BeTrue();
    }

    [Fact]
    public async Task try_lock_should_acquire_lock_when_available()
    {
        // when
        using var releaser = await AsyncDuplicateLock.TryLockAsync("try-lock-key", TimeSpan.FromSeconds(1), AbortToken);

        // then
        releaser.Should().NotBeNull();
    }

    [Fact]
    public async Task try_lock_should_return_null_when_timeout_expires()
    {
        // given
        var lockAcquired = new TaskCompletionSource();

        _ = Task.Run(
            async () =>
            {
                using (await AsyncDuplicateLock.LockAsync("try-lock-timeout-key"))
                {
                    lockAcquired.SetResult();
                    await Task.Delay(5000); // Hold lock
                }
            },
            AbortToken
        );

        await lockAcquired.Task;

        // when - try to acquire with short timeout
        using var releaser = await AsyncDuplicateLock.TryLockAsync(
            "try-lock-timeout-key",
            TimeSpan.FromMilliseconds(50),
            AbortToken
        );

        // then
        releaser.Should().BeNull();
    }

    [Fact]
    public async Task try_lock_should_acquire_lock_when_released_before_timeout()
    {
        // given
        var lockAcquired = new TaskCompletionSource();
        var canRelease = new TaskCompletionSource();

        var holdingTask = Task.Run(
            async () =>
            {
                using (await AsyncDuplicateLock.LockAsync("try-lock-wait-key", AbortToken))
                {
                    lockAcquired.SetResult();
                    await canRelease.Task;
                }
            },
            AbortToken
        );

        await lockAcquired.Task;

        // when - start trying to acquire, then release the first lock
        var tryLockTask = AsyncDuplicateLock.TryLockAsync("try-lock-wait-key", TimeSpan.FromSeconds(5), AbortToken);

        await Task.Delay(50, AbortToken);
        canRelease.SetResult();
        await holdingTask;

        var releaser = await tryLockTask;

        // then
        releaser.Should().NotBeNull();
        releaser.Dispose();
    }

    [Fact]
    public async Task try_lock_should_cleanup_ref_count_on_timeout()
    {
        // given
        var lockAcquired = new TaskCompletionSource();
        var canRelease = new TaskCompletionSource();

        var holdingTask = Task.Run(
            async () =>
            {
                using (await AsyncDuplicateLock.LockAsync("try-lock-cleanup-key"))
                {
                    lockAcquired.SetResult();
                    await canRelease.Task;
                }
            },
            AbortToken
        );

        await lockAcquired.Task;

        // when - timeout on try lock
        using var releaser = await AsyncDuplicateLock.TryLockAsync(
            "try-lock-cleanup-key",
            TimeSpan.FromMilliseconds(50),
            AbortToken
        );

        releaser.Should().BeNull();

        // Release the holding lock
        canRelease.SetResult();
        await holdingTask;

        // then - should be able to acquire again (ref count properly cleaned up)
        using (await AsyncDuplicateLock.LockAsync("try-lock-cleanup-key", AbortToken))
        {
            // Success
        }
    }

    [Fact]
    public async Task try_lock_should_support_cancellation()
    {
        // given
        var lockAcquired = new TaskCompletionSource();
        using var cts = new CancellationTokenSource();

        var holdingTask = Task.Run(
            async () =>
            {
                using (await AsyncDuplicateLock.LockAsync("try-lock-cancel-key", AbortToken))
                {
                    lockAcquired.SetResult();
                    await Task.Delay(5000, AbortToken);
                }
            },
            AbortToken
        );

        await lockAcquired.Task;
        cts.CancelAfter(50);

        // when/then
        var act = async () =>
            await AsyncDuplicateLock.TryLockAsync("try-lock-cancel-key", TimeSpan.FromSeconds(10), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task try_lock_should_throw_on_null_key()
    {
        // when
        var act = async () => await AsyncDuplicateLock.TryLockAsync(null!, TimeSpan.FromSeconds(1));

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task try_lock_should_throw_on_empty_key()
    {
        // when
        var act = async () => await AsyncDuplicateLock.TryLockAsync("", TimeSpan.FromSeconds(1));

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
