// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests.Helpers;

public sealed class HeadlessTestTaskExtensionsTests : TestBase
{
    [Fact]
    public async Task should_complete_when_the_task_completes_within_the_limit()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var wait = signal.Task.Bounded();
        signal.SetResult();

        await wait;
        wait.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_the_result_when_the_task_completes_within_the_limit()
    {
        var signal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var wait = signal.Task.Bounded();
        signal.SetResult(42);

        (await wait).Should().Be(42);
    }

    [Fact]
    public async Task should_throw_a_timeout_naming_the_awaited_expression_when_the_task_does_not_complete()
    {
        var never = new TaskCompletionSource();

        var act = () => never.Task.Bounded(TimeSpan.FromMilliseconds(50));

        (await act.Should().ThrowAsync<TimeoutException>())
            .WithMessage("Waited 0.05 s for 'never.Task', which did not complete.")
            .WithInnerException<TimeoutException>();
    }

    [Fact]
    public async Task should_throw_a_timeout_naming_the_awaited_expression_when_a_task_of_t_does_not_complete()
    {
        var never = new TaskCompletionSource<string>();

        var act = () => never.Task.Bounded(TimeSpan.FromMilliseconds(50));

        await act.Should().ThrowAsync<TimeoutException>().WithMessage("*'never.Task'*");
    }

    [Fact]
    public async Task should_keep_a_timeout_the_task_itself_threw()
    {
        var failed = Task.FromException(new TimeoutException("from the task"));

        var act = () => failed.Bounded();

        await act.Should().ThrowExactlyAsync<TimeoutException>().WithMessage("from the task");
    }

    [Fact]
    public async Task should_keep_a_timeout_a_task_of_t_itself_threw()
    {
        var failed = Task.FromException<int>(new TimeoutException("from the task"));

        var act = () => failed.Bounded();

        await act.Should().ThrowExactlyAsync<TimeoutException>().WithMessage("from the task");
    }

    [Fact]
    public async Task should_propagate_the_task_failure()
    {
        var failed = Task.FromException<int>(new InvalidOperationException("boom"));

        var act = () => failed.Bounded();

        await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task should_bound_a_value_task()
    {
        var never = new TaskCompletionSource();
        var pending = new ValueTask(never.Task);

        var act = () => pending.Bounded(TimeSpan.FromMilliseconds(50));

        await act.Should().ThrowAsync<TimeoutException>().WithMessage("*'pending'*");
    }

    [Fact]
    public async Task should_return_the_result_of_a_value_task_of_t()
    {
        var completed = ValueTask.FromResult(7);

        (await completed.Bounded()).Should().Be(7);
    }

    [Fact]
    public void should_default_to_a_thirty_second_limit()
    {
        HeadlessTestTaskExtensions.DefaultTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }
}
