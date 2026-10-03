// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using StackExchange.Redis;
using Tests.Capabilities;

namespace Tests;

/// <summary>Drives the shared transport and request/reply conformance suites against the shared Redis server.</summary>
internal sealed class RedisProviderConformanceDriver(RedisMessagingFixture fixture) : TransportProviderConformanceDriver
{
    private static readonly TransportConformanceProfile _Profile = TransportConformanceManifest.Providers["Redis"];

    public override string ProviderName => _Profile.Provider;

    public override TransportMalformedEnvelopeBound MalformedEnvelopeBound => _Profile.MalformedEnvelopeBound!;

    public override bool SupportsEveryInstance => true;

    public override bool SupportsRequestReply => true;

    public override void ConfigureRequestReplyTransport(MessagingSetupBuilder setup)
    {
        setup.UseRedis(options => options.Configuration = ConfigurationOptions.Parse(fixture.ConnectionString));
    }

    /// <summary>
    /// A reply channel exists on the server only while a client subscribes to it. A reply channel must also never
    /// leave a key behind, so a key named after the address counts as a reply object too.
    /// </summary>
    public override async ValueTask<bool> HasReplyObjectsAsync(string replyAddress, CancellationToken cancellationToken)
    {
        return await fixture.CountSubscribersAsync(replyAddress, cancellationToken) > 0
            || (await fixture.ScanKeysAsync($"*{replyAddress}*", cancellationToken)).Count > 0;
    }

    public override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateSessionAsync(
            endpoint.Lane,
            endpoint.LogicalName,
            endpoint.SubscriptionName,
            cancellationToken,
            ownsStream: string.Equals(endpoint.Replica, "replica-1", StringComparison.Ordinal),
            request: endpoint.ToRequest()
        );
    }
}
