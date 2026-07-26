// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Tests.Capabilities;

namespace Tests;

internal sealed class AwsProviderConformanceDriver(LocalStackTestFixture fixture) : TransportProviderConformanceDriver
{
    private static readonly TransportConformanceProfile _Profile = TransportConformanceManifest.Providers[
        "AWS/LocalStack"
    ];

    public override string ProviderName => _Profile.Provider;

    public override TransportConformanceDriverCapabilities Capabilities { get; } =
        new(
            SupportsRawEnvelopeInjection: false,
            SupportsTerminalStateObservation: false,
            SupportsTopologyInspection: false,
            SupportsStartupSideEffectObservation: false,
            SupportsLegacyMigration: false
        );

    public override TransportMalformedEnvelopeBound MalformedEnvelopeBound => _Profile.MalformedEnvelopeBound!;

    public override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken
    )
    {
        var ownsQueue = string.Equals(endpoint.Replica, "replica-1", StringComparison.Ordinal);

        return endpoint.Lane switch
        {
            MessageLane.Bus => fixture.CreateBusSessionAsync(
                endpoint.LogicalName,
                endpoint.SubscriberGroup,
                cancellationToken,
                ownsQueue
            ),
            MessageLane.Queue => fixture.CreateConformanceSessionAsync(
                cancellationToken,
                endpoint.LogicalName,
                endpoint.SubscriberGroup,
                ownsQueue
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint.Lane, null),
        };
    }
}
