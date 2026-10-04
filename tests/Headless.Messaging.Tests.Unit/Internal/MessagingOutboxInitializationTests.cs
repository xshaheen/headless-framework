// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.Helpers;

namespace Tests.Internal;

/// <summary>
/// An additional outbox initializes its schema on demand: callers share one attempt, a failed attempt leaves it
/// uninitialized for the next caller to retry, and one caller canceling does not fail the others.
/// </summary>
public sealed class MessagingOutboxInitializationTests : TestBase
{
    [Fact]
    public async Task should_share_one_attempt_between_concurrent_callers()
    {
        // given
        var sut = AdditionalOutboxDoubles.CreateOutbox("billing", initialized: false);
        var initialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.Initializer.InitializeAsync(Arg.Any<CancellationToken>()).Returns(initialization.Task);

        // when
        var first = sut.EnsureInitializedAsync(AbortToken);
        var second = sut.EnsureInitializedAsync(AbortToken);
        sut.IsInitialized.Should().BeFalse();
        initialization.SetResult();
        await Task.WhenAll(first, second);

        // then
        sut.IsInitialized.Should().BeTrue();
        await sut.Initializer.Received(1).InitializeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_report_a_failed_attempt_and_retry_on_the_next_call()
    {
        // given
        var sut = AdditionalOutboxDoubles.CreateOutbox("billing", initialized: false);
        var failure = new TimeoutException("billing is down");
        sut.Initializer.InitializeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(failure), Task.CompletedTask);

        // when
        var act = () => sut.EnsureInitializedAsync(AbortToken);

        // then
        (await act.Should().ThrowAsync<TimeoutException>())
            .Which.Should()
            .BeSameAs(failure);
        sut.IsInitialized.Should().BeFalse();

        await sut.EnsureInitializedAsync(AbortToken);
        sut.IsInitialized.Should().BeTrue();
        await sut.Initializer.Received(2).InitializeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_start_its_own_attempt_when_the_shared_one_is_canceled_by_another_caller()
    {
        // given — the first attempt runs until its caller cancels it
        var sut = AdditionalOutboxDoubles.CreateOutbox("billing", initialized: false);
        sut.Initializer.InitializeAsync(Arg.Any<CancellationToken>())
            .Returns(
                call => Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>()),
                _ => Task.CompletedTask
            );
        using var starterCancellation = new CancellationTokenSource();
        var starter = sut.EnsureInitializedAsync(starterCancellation.Token);
        var joiner = sut.EnsureInitializedAsync(AbortToken);

        // when
        await starterCancellation.CancelAsync();

        // then
        await starter.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
        await joiner;
        sut.IsInitialized.Should().BeTrue();
        await sut.Initializer.Received(2).InitializeAsync(Arg.Any<CancellationToken>());
    }
}
