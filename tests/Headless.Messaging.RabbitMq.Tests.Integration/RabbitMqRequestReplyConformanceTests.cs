// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.RequestReply;

namespace Tests;

/// <summary>The shared request/reply suite against a real RabbitMQ broker.</summary>
[Collection<RabbitMqFixture>]
public sealed class RabbitMqRequestReplyConformanceTests(RabbitMqFixture fixture) : TestBase
{
    [Fact]
    public Task should_return_the_typed_response_of_a_request() =>
        TransportRequestReplyConformance.AssertRoundTripAsync(_CreateDriver(), AbortToken);

    [Fact]
    public Task should_give_each_caller_only_its_own_replies() =>
        TransportRequestReplyConformance.AssertCallersReceiveOnlyTheirOwnRepliesAsync(_CreateDriver(), AbortToken);

    [Fact]
    public Task should_fault_a_request_whose_responder_fails() =>
        TransportRequestReplyConformance.AssertResponderFailureFaultsTheCallAsync(_CreateDriver(), AbortToken);

    [Fact]
    public Task should_fault_a_request_with_no_responder_without_running_the_plain_consumer() =>
        TransportRequestReplyConformance.AssertPlainConsumerFaultsWithNoResponderAsync(_CreateDriver(), AbortToken);

    [Fact]
    public Task should_time_out_a_request_whose_responder_stopped() =>
        TransportRequestReplyConformance.AssertTimeoutWithoutRunningResponderAsync(_CreateDriver(), AbortToken);

    [Fact]
    public Task should_drop_a_reply_that_arrives_after_its_call_timed_out() =>
        TransportRequestReplyConformance.AssertLateReplyIsDroppedAsync(_CreateDriver(), AbortToken);

    [Fact]
    public Task should_never_write_a_reply_to_a_foreign_reply_address() =>
        TransportRequestReplyConformance.AssertForeignReplyAddressIsNeverWrittenAsync(_CreateDriver(), AbortToken);

    [Fact]
    public Task should_never_deliver_a_reply_to_a_restarted_caller() =>
        TransportRequestReplyConformance.AssertRestartedCallerNeverReceivesOldRepliesAsync(_CreateDriver(), AbortToken);

    [Fact]
    public Task should_carry_the_tenant_from_caller_to_responder_and_back() =>
        TransportRequestReplyConformance.AssertTenantFlowsBothWaysAsync(_CreateDriver(), AbortToken);

    [Fact]
    public Task should_leave_no_reply_queue_after_the_caller_stops() =>
        TransportRequestReplyConformance.AssertStoppedCallerLeavesNoReplyObjectsAsync(_CreateDriver(), AbortToken);

    private RabbitMqProviderConformanceDriver _CreateDriver()
    {
        return new RabbitMqProviderConformanceDriver(fixture);
    }
}
