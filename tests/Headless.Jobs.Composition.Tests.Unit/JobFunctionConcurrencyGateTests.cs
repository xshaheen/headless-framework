using Headless.Jobs;

namespace Tests;

public sealed class JobFunctionConcurrencyGateTests
{
    private readonly JobFunctionConcurrencyGate _sut = new();

    [Fact]
    public void get_semaphore_or_null_returns_null_when_max_concurrency_is_zero()
    {
        var result = _sut.GetSemaphoreOrNull("fn-a", 0);

        result.Should().BeNull();
    }

    [Fact]
    public void get_semaphore_or_null_returns_null_when_max_concurrency_is_negative()
    {
        var result = _sut.GetSemaphoreOrNull("fn-a", -1);

        result.Should().BeNull();
    }

    [Fact]
    public void get_semaphore_or_null_returns_semaphore_when_max_concurrency_is_positive()
    {
        var result = _sut.GetSemaphoreOrNull("fn-a", 3);

        result.Should().NotBeNull();
        result!.CurrentCount.Should().Be(3);
    }

    [Fact]
    public void get_semaphore_or_null_returns_same_instance_for_same_function()
    {
        var first = _sut.GetSemaphoreOrNull("fn-a", 5);
        var second = _sut.GetSemaphoreOrNull("fn-a", 5);

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void get_semaphore_or_null_returns_different_instances_for_different_functions()
    {
        var a = _sut.GetSemaphoreOrNull("fn-a", 2);
        var b = _sut.GetSemaphoreOrNull("fn-b", 2);

        a.Should().NotBeSameAs(b);
    }

    [Fact]
    public void get_semaphore_or_null_uses_ordinal_comparison()
    {
        var lower = _sut.GetSemaphoreOrNull("SendEmail", 1);
        var upper = _sut.GetSemaphoreOrNull("sendemail", 1);

        lower.Should().NotBeSameAs(upper);
    }

    [Fact]
    public void get_semaphore_or_null_preserves_initial_max_concurrency_on_subsequent_calls()
    {
        // First call creates with maxConcurrency=2
        var first = _sut.GetSemaphoreOrNull("fn-a", 2);
        // Second call with different maxConcurrency should return cached (original) semaphore
        var second = _sut.GetSemaphoreOrNull("fn-a", 10);

        first.Should().BeSameAs(second);
        second!.CurrentCount.Should().Be(2);
    }

    [Fact]
    public void get_semaphore_or_null_is_thread_safe()
    {
        const int threadCount = 50;
        var semaphores = new SemaphoreSlim?[threadCount];
        using var barrier = new Barrier(threadCount);

        Parallel.For(
            0,
            threadCount,
            i =>
            {
                barrier.SignalAndWait();
                semaphores[i] = _sut.GetSemaphoreOrNull("fn-concurrent", 3);
            }
        );

        var distinct = semaphores.Distinct().ToArray();
        distinct.Should().ContainSingle();
        distinct[0]!.CurrentCount.Should().Be(3);
    }
}
