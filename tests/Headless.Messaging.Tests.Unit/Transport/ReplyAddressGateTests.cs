// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;
using Headless.Testing.Tests;

namespace Tests.Transport;

/// <summary>
/// The reply address a listener hands out exists only while its channel is live: a caller waits for it, a lost channel
/// withdraws it, and a closed listener fails the wait instead of leaving the caller hanging.
/// </summary>
public sealed class ReplyAddressGateTests : TestBase
{
    private const string _Address = "headless.reply.0f8fad5bd9cb469fa16570867728950e";
    private const string _NextAddress = "headless.reply.7c9e6679742540de944be07fc1f90ae7";

    [Fact]
    public async Task should_keep_a_wait_pending_until_the_address_is_published()
    {
        // given
        var gate = new ReplyAddressGate();
        var wait = gate.WaitAsync(AbortToken).AsTask();
        wait.IsCompleted.Should().BeFalse();

        // when
        gate.Publish(_Address);

        // then
        (await wait)
            .Should()
            .Be(_Address);
        (await gate.WaitAsync(AbortToken)).Should().Be(_Address);
    }

    [Fact]
    public void should_withhold_the_address_when_the_channel_is_not_live()
    {
        // given
        var gate = new ReplyAddressGate();

        // when
        gate.Publish(_Address, isLive: () => false);

        // then
        gate.WaitAsync(AbortToken).IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task should_hand_out_the_address_when_the_channel_is_live()
    {
        // given
        var gate = new ReplyAddressGate();

        // when
        gate.Publish(_Address, isLive: () => true);

        // then
        (await gate.WaitAsync(AbortToken))
            .Should()
            .Be(_Address);
    }

    [Fact]
    public async Task should_make_later_callers_wait_for_the_next_address_after_a_retraction()
    {
        // given
        var gate = new ReplyAddressGate();
        gate.Publish(_Address);

        // when
        gate.Retract();
        var wait = gate.WaitAsync(AbortToken).AsTask();

        // then
        wait.IsCompleted.Should().BeFalse();
        gate.Publish(_NextAddress);
        (await wait).Should().Be(_NextAddress);
    }

    [Fact]
    public async Task should_keep_the_address_when_the_channel_is_still_live_at_retraction()
    {
        // given
        var gate = new ReplyAddressGate();
        gate.Publish(_Address);

        // when
        gate.Retract(isStillLive: () => true);

        // then
        (await gate.WaitAsync(AbortToken))
            .Should()
            .Be(_Address);
    }

    [Fact]
    public void should_withdraw_the_address_when_the_channel_is_down_at_retraction()
    {
        // given
        var gate = new ReplyAddressGate();
        gate.Publish(_Address);

        // when
        gate.Retract(isStillLive: () => false);

        // then
        gate.WaitAsync(AbortToken).IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_a_waiting_caller_with_object_disposed_when_the_listener_closes()
    {
        // given
        var gate = new ReplyAddressGate();
        var wait = gate.WaitAsync(AbortToken).AsTask();

        // when
        gate.FailOnDispose("TestListener");

        // then
        await wait.Awaiting(x => x).Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_not_open_a_new_wait_when_a_retraction_follows_the_close()
    {
        // given — the listener closed while its channel was live, then its loop retracts on the way out
        var gate = new ReplyAddressGate();
        gate.Publish(_Address);
        gate.FailOnDispose("TestListener");

        // when
        gate.Retract();
        gate.Retract(isStillLive: () => false);

        // then — a later caller does not block on a wait nobody will ever complete
        (await gate.WaitAsync(AbortToken))
            .Should()
            .Be(_Address);
    }

    [Fact]
    public async Task should_fail_a_later_caller_after_a_close_before_any_address()
    {
        // given
        var gate = new ReplyAddressGate();
        gate.FailOnDispose("TestListener");

        // when
        gate.Retract();
        var act = () => gate.WaitAsync(AbortToken).AsTask();

        // then
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_end_a_wait_when_its_token_is_canceled()
    {
        // given
        var gate = new ReplyAddressGate();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var wait = gate.WaitAsync(cancellation.Token).AsTask();

        // when
        await cancellation.CancelAsync();

        // then
        await wait.Awaiting(x => x).Should().ThrowAsync<OperationCanceledException>();
    }
}
