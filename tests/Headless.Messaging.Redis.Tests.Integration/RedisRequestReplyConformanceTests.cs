// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.RequestReply;

namespace Tests;

/// <summary>The shared request/reply suite against a real Redis server, with replies on pub/sub channels.</summary>
[Collection<RedisMessagingFixture>]
public sealed class RedisRequestReplyConformanceTests(RedisMessagingFixture fixture) : TestBase
{
    [Fact]
    public async Task should_return_the_typed_response_of_a_request() =>
        await TransportRequestReplyConformance.AssertRoundTripAsync(_CreateDriver(), AbortToken);

    [Fact]
    public async Task should_give_each_caller_only_its_own_replies() =>
        await TransportRequestReplyConformance.AssertCallersReceiveOnlyTheirOwnRepliesAsync(
            _CreateDriver(),
            AbortToken
        );

    [Fact]
    public async Task should_fault_a_request_whose_responder_fails() =>
        await TransportRequestReplyConformance.AssertResponderFailureFaultsTheCallAsync(_CreateDriver(), AbortToken);

    [Fact]
    public async Task should_fault_a_request_with_no_responder_without_running_the_plain_consumer() =>
        await TransportRequestReplyConformance.AssertPlainConsumerFaultsWithNoResponderAsync(
            _CreateDriver(),
            AbortToken
        );

    [Fact]
    public async Task should_time_out_a_request_whose_responder_stopped() =>
        await TransportRequestReplyConformance.AssertTimeoutWithoutRunningResponderAsync(_CreateDriver(), AbortToken);

    [Fact]
    public async Task should_drop_a_reply_that_arrives_after_its_call_timed_out() =>
        await TransportRequestReplyConformance.AssertLateReplyIsDroppedAsync(_CreateDriver(), AbortToken);

    [Fact]
    public async Task should_never_write_a_reply_to_a_foreign_reply_address() =>
        await TransportRequestReplyConformance.AssertForeignReplyAddressIsNeverWrittenAsync(
            _CreateDriver(),
            AbortToken
        );

    [Fact]
    public async Task should_never_deliver_a_reply_to_a_restarted_caller() =>
        await TransportRequestReplyConformance.AssertRestartedCallerNeverReceivesOldRepliesAsync(
            _CreateDriver(),
            AbortToken
        );

    [Fact]
    public async Task should_carry_the_tenant_from_caller_to_responder_and_back() =>
        await TransportRequestReplyConformance.AssertTenantFlowsBothWaysAsync(_CreateDriver(), AbortToken);

    [Fact]
    public async Task should_leave_no_reply_subscription_or_key_after_the_caller_stops() =>
        await TransportRequestReplyConformance.AssertStoppedCallerLeavesNoReplyObjectsAsync(
            _CreateDriver(),
            AbortToken
        );

    private RedisProviderConformanceDriver _CreateDriver() => new(fixture);
}
