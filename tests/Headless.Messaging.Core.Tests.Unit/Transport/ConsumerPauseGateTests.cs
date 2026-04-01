// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;

namespace Tests.Transport;

public sealed class ConsumerPauseGateTests
{
    [Fact]
    public async Task wait_if_paused_async_should_complete_immediately_when_not_paused()
    {
        // given
        var gate = new ConsumerPauseGate();

        // when
        var act = async () => await gate.WaitIfPausedAsync(CancellationToken.None);

        // then
        await act.Should().NotThrowAsync();
        gate.IsPaused.Should().BeFalse();
    }

    [Fact]
    public async Task pause_async_should_block_waiters_until_resume_async_is_called()
    {
        // given
        var gate = new ConsumerPauseGate();
        await gate.PauseAsync().AsTask();

        // when
        var waitTask = gate.WaitIfPausedAsync(CancellationToken.None).AsTask();
        await Task.Delay(50);

        // then
        waitTask.IsCompleted.Should().BeFalse();
        gate.IsPaused.Should().BeTrue();

        await gate.ResumeAsync().AsTask();
        await waitTask.WaitAsync(TimeSpan.FromSeconds(1));
        gate.IsPaused.Should().BeFalse();
    }

    [Fact]
    public async Task pause_and_resume_should_be_idempotent()
    {
        // given
        var gate = new ConsumerPauseGate();

        // when
        var firstPause = await gate.PauseAsync();
        var secondPause = await gate.PauseAsync();
        var firstResume = await gate.ResumeAsync();
        var secondResume = await gate.ResumeAsync();

        // then
        firstPause.Should().BeTrue();
        secondPause.Should().BeFalse();
        firstResume.Should().BeTrue();
        secondResume.Should().BeFalse();
    }

    [Fact]
    public async Task wait_if_paused_async_should_observe_cancellation()
    {
        // given
        var gate = new ConsumerPauseGate();
        await gate.PauseAsync();
        using var cts = new CancellationTokenSource();

        // when
        var waitTask = gate.WaitIfPausedAsync(cts.Token).AsTask();
        await cts.CancelAsync();

        // then
        var act = async () => await waitTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task release_should_unblock_waiters_and_prevent_future_transitions()
    {
        // given
        var gate = new ConsumerPauseGate();
        await gate.PauseAsync();
        var waitTask = gate.WaitIfPausedAsync(CancellationToken.None).AsTask();

        // when
        gate.Release();

        // then
        await waitTask.WaitAsync(TimeSpan.FromSeconds(1));
        (await gate.PauseAsync()).Should().BeFalse();
        (await gate.ResumeAsync()).Should().BeFalse();
        gate.IsPaused.Should().BeFalse();
    }
}
