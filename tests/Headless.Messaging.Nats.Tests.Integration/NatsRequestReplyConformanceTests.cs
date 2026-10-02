// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Tests.RequestReply;

namespace Tests;

/// <summary>The shared request/reply suite against a real NATS server, with hosts that provision their own streams.</summary>
[Collection("Nats")]
public sealed class NatsRequestReplyConformanceTests(NatsFixture fixture) : NatsRequestReplyConformanceTestsBase
{
    protected override ValueTask<TransportProviderConformanceDriver> CreateDriverAsync()
    {
        return ValueTask.FromResult<TransportProviderConformanceDriver>(new NatsProviderConformanceDriver(fixture));
    }
}

/// <summary>
/// The shared request/reply suite with stream provisioning disabled, as in an operator-managed deployment: the reply
/// channel needs no stream, so the suite passes with only the operator's request stream in place.
/// </summary>
[Collection("Nats")]
public sealed class NatsRequestReplyWithoutStreamProvisioningTests(NatsFixture fixture)
    : NatsRequestReplyConformanceTestsBase
{
    protected override async ValueTask<TransportProviderConformanceDriver> CreateDriverAsync()
    {
        await fixture.EnsureOperatorStreamAsync();
        return new NatsProviderConformanceDriver(fixture, provisionStreams: false);
    }
}

public abstract class NatsRequestReplyConformanceTestsBase : TestBase
{
    [Fact]
    public async Task should_return_the_typed_response_of_a_request() =>
        await TransportRequestReplyConformance.AssertRoundTripAsync(await CreateDriverAsync(), AbortToken);

    [Fact]
    public async Task should_give_each_caller_only_its_own_replies() =>
        await TransportRequestReplyConformance.AssertCallersReceiveOnlyTheirOwnRepliesAsync(
            await CreateDriverAsync(),
            AbortToken
        );

    [Fact]
    public async Task should_fault_a_request_whose_responder_fails() =>
        await TransportRequestReplyConformance.AssertResponderFailureFaultsTheCallAsync(
            await CreateDriverAsync(),
            AbortToken
        );

    [Fact]
    public async Task should_fault_a_request_with_no_responder_without_running_the_plain_consumer() =>
        await TransportRequestReplyConformance.AssertPlainConsumerFaultsWithNoResponderAsync(
            await CreateDriverAsync(),
            AbortToken
        );

    [Fact]
    public async Task should_time_out_a_request_whose_responder_stopped() =>
        await TransportRequestReplyConformance.AssertTimeoutWithoutRunningResponderAsync(
            await CreateDriverAsync(),
            AbortToken
        );

    [Fact]
    public async Task should_drop_a_reply_that_arrives_after_its_call_timed_out() =>
        await TransportRequestReplyConformance.AssertLateReplyIsDroppedAsync(await CreateDriverAsync(), AbortToken);

    [Fact]
    public async Task should_never_write_a_reply_to_a_foreign_reply_address() =>
        await TransportRequestReplyConformance.AssertForeignReplyAddressIsNeverWrittenAsync(
            await CreateDriverAsync(),
            AbortToken
        );

    [Fact]
    public async Task should_never_deliver_a_reply_to_a_restarted_caller() =>
        await TransportRequestReplyConformance.AssertRestartedCallerNeverReceivesOldRepliesAsync(
            await CreateDriverAsync(),
            AbortToken
        );

    [Fact]
    public async Task should_carry_the_tenant_from_caller_to_responder_and_back() =>
        await TransportRequestReplyConformance.AssertTenantFlowsBothWaysAsync(await CreateDriverAsync(), AbortToken);

    [Fact]
    public async Task should_leave_no_reply_subscription_after_the_caller_stops() =>
        await TransportRequestReplyConformance.AssertStoppedCallerLeavesNoReplyObjectsAsync(
            await CreateDriverAsync(),
            AbortToken
        );

    protected abstract ValueTask<TransportProviderConformanceDriver> CreateDriverAsync();
}
