// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SqlCancellationTests : TestBase
{
    [Fact]
    public async Task should_report_a_database_fault_raised_after_the_cancel_as_the_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var fault = new FakeDbException(isTransient: false);

        var thrown = await FluentActions
            .Awaiting(() => SqlCancellation.ObserveAsync(Task.FromException<int>(fault), cancellation.Token))
            .Should()
            .ThrowAsync<OperationCanceledException>();

        thrown.Which.InnerException.Should().BeSameAs(fault);
        thrown.Which.CancellationToken.Should().Be(cancellation.Token);
    }

    [Fact]
    public async Task should_report_a_database_fault_from_a_value_task_as_the_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var fault = new FakeDbException(isTransient: false);

        (
            await FluentActions
                .Awaiting(() =>
                    SqlCancellation
                        .ObserveAsync(new ValueTask<int>(Task.FromException<int>(fault)), cancellation.Token)
                        .AsTask()
                )
                .Should()
                .ThrowAsync<OperationCanceledException>()
        )
            .Which.InnerException.Should()
            .BeSameAs(fault);
        (
            await FluentActions
                .Awaiting(() =>
                    SqlCancellation.ObserveAsync(new ValueTask(Task.FromException(fault)), cancellation.Token).AsTask()
                )
                .Should()
                .ThrowAsync<OperationCanceledException>()
        )
            .Which.InnerException.Should()
            .BeSameAs(fault);
    }

    [Fact]
    public async Task should_find_the_database_fault_inside_a_wrapper()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var wrapper = new InvalidOperationException("wrapper", new FakeDbException(isTransient: false));

        var thrown = await FluentActions
            .Awaiting(() => SqlCancellation.ObserveAsync(Task.FromException(wrapper), cancellation.Token))
            .Should()
            .ThrowAsync<OperationCanceledException>();

        thrown.Which.InnerException.Should().BeSameAs(wrapper);
    }

    [Fact]
    public async Task should_leave_a_database_fault_unchanged_while_the_token_is_live()
    {
        var fault = new FakeDbException(isTransient: false);

        (
            await FluentActions
                .Awaiting(() =>
                    SqlCancellation
                        .ObserveAsync(new ValueTask<int>(Task.FromException<int>(fault)), AbortToken)
                        .AsTask()
                )
                .Should()
                .ThrowAsync<FakeDbException>()
        )
            .Which.Should()
            .BeSameAs(fault);
    }

    [Fact]
    public async Task should_leave_a_fault_that_carries_no_database_exception_unchanged_after_the_cancel()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var fault = new InvalidOperationException("not a database fault");

        (
            await FluentActions
                .Awaiting(() =>
                    SqlCancellation.ObserveAsync(new ValueTask(Task.FromException(fault)), cancellation.Token).AsTask()
                )
                .Should()
                .ThrowAsync<InvalidOperationException>()
        )
            .Which.Should()
            .BeSameAs(fault);
    }

    [Fact]
    public async Task should_pass_an_operation_canceled_exception_through_without_wrapping_it_again()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var canceled = new OperationCanceledException("driver cancel", new FakeDbException(isTransient: false));

        (
            await FluentActions
                .Awaiting(() => SqlCancellation.ObserveAsync(Task.FromException<int>(canceled), cancellation.Token))
                .Should()
                .ThrowAsync<OperationCanceledException>()
        )
            .Which.Should()
            .BeSameAs(canceled);
    }

    [Fact]
    public async Task should_return_the_result_of_a_call_that_succeeds()
    {
        (await SqlCancellation.ObserveAsync(Task.FromResult(7), AbortToken)).Should().Be(7);
        (await SqlCancellation.ObserveAsync(new ValueTask<int>(7), AbortToken)).Should().Be(7);
        await SqlCancellation.ObserveAsync(Task.CompletedTask, AbortToken);
        await SqlCancellation.ObserveAsync(ValueTask.CompletedTask, AbortToken);
    }

    [Fact]
    public void should_refuse_a_null_exception()
    {
        FluentActions
            .Invoking(() => SqlCancellation.IsCancellation(null!, AbortToken))
            .Should()
            .Throw<ArgumentNullException>();
        FluentActions
            .Invoking(() => SqlCancellation.ToOperationCanceled(null!, AbortToken))
            .Should()
            .Throw<ArgumentNullException>();
    }
}
