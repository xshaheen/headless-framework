// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.RequestReply;

namespace Tests;

/// <summary>The shared request/reply suite against a real NATS server, with hosts that provision their own streams.</summary>
[Collection("Nats")]
public sealed class NatsRequestReplyConformanceTests(NatsFixture fixture) : TransportRequestReplyConformanceTestsBase
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
    : TransportRequestReplyConformanceTestsBase
{
    protected override async ValueTask<TransportProviderConformanceDriver> CreateDriverAsync()
    {
        await fixture.EnsureOperatorStreamAsync();
        return new NatsProviderConformanceDriver(fixture, provisionStreams: false);
    }
}
