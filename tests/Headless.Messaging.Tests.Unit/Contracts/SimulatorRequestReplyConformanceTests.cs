// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.RequestReply;

namespace Tests.Contracts;

/// <summary>
/// The shared request/reply suite against hosts on one simulated broker, its reply listeners registered at the broker
/// the way a real broker holds one reply queue or channel per process.
/// </summary>
[Collection(Tests.RequestReply.RequestReplyCollection.Name)]
public sealed class SimulatorRequestReplyConformanceTests : TransportRequestReplyConformanceTestsBase
{
    private readonly SimulatorConformance _simulator = new();

    protected override ValueTask<TransportProviderConformanceDriver> CreateDriverAsync() =>
        ValueTask.FromResult(_simulator.Driver);

    protected override async ValueTask DisposeAsyncCore()
    {
        await _simulator.DisposeAsync();
        await base.DisposeAsyncCore();
    }
}
