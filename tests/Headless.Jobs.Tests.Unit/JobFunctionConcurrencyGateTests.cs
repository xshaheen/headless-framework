using Headless.Jobs;

namespace Tests;

public sealed class JobFunctionConcurrencyGateTests
{
    private readonly JobFunctionConcurrencyGate _sut = new();

    [Fact]
    public void GetSemaphoreOrNull_Returns_Null_When_MaxConcurrency_Is_Zero()
    {
        var result = _sut.GetSemaphoreOrNull("fn-a", 0);

        result.Should().BeNull();
    }

    [Fact]
    public void GetSemaphoreOrNull_Returns_Null_When_MaxConcurrency_Is_Negative()
    {
        var result = _sut.GetSemaphoreOrNull("fn-a", -1);

        result.Should().BeNull();
    }

    [Fact]
    public void GetSemaphoreOrNull_Returns_Semaphore_When_MaxConcurrency_Is_Positive()
    {
        var result = _sut.GetSemaphoreOrNull("fn-a", 3);

        result.Should().NotBeNull();
        result!.CurrentCount.Should().Be(3);
    }

    [Fact]
    public void GetSemaphoreOrNull_Returns_Same_Instance_For_Same_Function()
    {
        var first = _sut.GetSemaphoreOrNull("fn-a", 5);
        var second = _sut.GetSemaphoreOrNull("fn-a", 5);

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void GetSemaphoreOrNull_Returns_Different_Instances_For_Different_Functions()
    {
        var a = _sut.GetSemaphoreOrNull("fn-a", 2);
        var b = _sut.GetSemaphoreOrNull("fn-b", 2);

        a.Should().NotBeSameAs(b);
    }

    [Fact]
    public void GetSemaphoreOrNull_Uses_Ordinal_Comparison()
    {
        var lower = _sut.GetSemaphoreOrNull("SendEmail", 1);
        var upper = _sut.GetSemaphoreOrNull("sendemail", 1);

        lower.Should().NotBeSameAs(upper);
    }

    [Fact]
    public void GetSemaphoreOrNull_Preserves_Initial_MaxConcurrency_On_Subsequent_Calls()
    {
        // First call creates with maxConcurrency=2
        var first = _sut.GetSemaphoreOrNull("fn-a", 2);
        // Second call with different maxConcurrency should return cached (original) semaphore
        var second = _sut.GetSemaphoreOrNull("fn-a", 10);

        first.Should().BeSameAs(second);
        second!.CurrentCount.Should().Be(2);
    }

    [Fact]
    public void GetSemaphoreOrNull_Is_Thread_Safe()
    {
        const int threadCount = 50;
        var semaphores = new SemaphoreSlim?[threadCount];
        var barrier = new Barrier(threadCount);

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
        distinct.Should().HaveCount(1);
        distinct[0]!.CurrentCount.Should().Be(3);
    }
}
