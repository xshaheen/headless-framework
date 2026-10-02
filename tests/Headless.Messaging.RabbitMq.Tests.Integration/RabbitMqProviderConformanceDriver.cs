// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Transport;
using Tests.Capabilities;

namespace Tests;

internal sealed class RabbitMqProviderConformanceDriver(RabbitMqFixture fixture) : TransportProviderConformanceDriver
{
    private static readonly TransportConformanceProfile _Profile = TransportConformanceManifest.Providers["RabbitMQ"];
    private readonly string _exchangeName = $"conformance-{Guid.NewGuid():N}";

    public override string ProviderName => _Profile.Provider;

    public override TransportMalformedEnvelopeBound MalformedEnvelopeBound => _Profile.MalformedEnvelopeBound!;

    public override bool SupportsEveryInstance => true;

    public override bool SupportsRequestReply => true;

    public override void ConfigureRequestReplyTransport(MessagingSetupBuilder setup)
    {
        setup.UseRabbitMq(options =>
        {
            options.HostName = fixture.HostName;
            options.Port = fixture.Port;
            options.UserName = fixture.UserName;
            options.Password = fixture.Password;
            options.ExchangeName = _exchangeName;
        });
    }

    public override ValueTask<bool> HasReplyObjectsAsync(string replyAddress, CancellationToken cancellationToken)
    {
        return fixture.QueueExistsAsync(replyAddress, cancellationToken);
    }

    public override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken
    )
    {
        if (endpoint.Kind is ConsumerSubscriptionKind.EveryInstance)
        {
            return fixture.CreateEndpointSessionAsync(endpoint, _exchangeName, cancellationToken);
        }

        return fixture.CreateLaneSessionAsync(
            endpoint.Lane,
            _exchangeName,
            endpoint.LogicalName,
            endpoint.SubscriptionName,
            cancellationToken
        );
    }
}
