// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using NATS.Client.JetStream.Models;
using Tests.Capabilities;

namespace Tests;

/// <summary>Drives the shared transport and request/reply conformance suites against the shared NATS server.</summary>
/// <param name="fixture">The shared NATS server.</param>
/// <param name="provisionStreams">
/// Whether request/reply hosts provision their own streams. When false they run as an operator-managed deployment does:
/// provisioning is disabled and requests land on the stream <see cref="NatsFixture.EnsureOperatorStreamAsync"/> created.
/// </param>
internal sealed class NatsProviderConformanceDriver(NatsFixture fixture, bool provisionStreams = true)
    : TransportProviderConformanceDriver
{
    private static readonly TransportConformanceProfile _Profile = TransportConformanceManifest.Providers["NATS"];
    private readonly string _streamName = $"conformance-{Guid.NewGuid():N}"[..30];

    public override string ProviderName => _Profile.Provider;

    public override TransportMalformedEnvelopeBound MalformedEnvelopeBound => _Profile.MalformedEnvelopeBound!;

    public override bool SupportsEveryInstance => true;

    public override bool SupportsRequestReply => true;

    public override void ConfigureRequestReplyTransport(MessagingSetupBuilder setup)
    {
        setup.UseNats(options =>
        {
            options.Servers = fixture.ConnectionString;

            if (provisionStreams)
            {
                // A stream of this driver's own, so scenario runs never contribute subjects to one another's streams.
                // A responder host's consumer groups each add their own subject to it, which only Reconcile allows.
                options.StreamProvisioning = NatsStreamProvisioning.Reconcile;
                options.NormalizeStreamName = _ => _streamName;
                options.StreamOptions = config => config.Storage = StreamConfigStorage.Memory;
            }
            else
            {
                options.StreamProvisioning = NatsStreamProvisioning.Disabled;
                options.NormalizeStreamName = _ => NatsFixture.OperatorStreamKey;
            }
        });

        if (!provisionStreams)
        {
            // Moves every subject under the operator stream's wildcard, which no provisioned stream overlaps.
            setup.Options.MessageNamePrefix = NatsFixture.OperatorMessageNamePrefix;
        }
    }

    public override async ValueTask<bool> HasReplyObjectsAsync(string replyAddress, CancellationToken cancellationToken)
    {
        return await fixture.CountSubscriptionsAsync(replyAddress, cancellationToken) > 0;
    }

    public override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken
    )
    {
        if (endpoint.Kind is ConsumerSubscriptionKind.EveryInstance)
        {
            return fixture.CreateEndpointSessionAsync(endpoint, _streamName, cancellationToken);
        }

        return fixture.CreateLaneSessionAsync(
            endpoint.Lane,
            _streamName,
            endpoint.LogicalName,
            endpoint.SubscriptionName,
            cancellationToken
        );
    }
}
