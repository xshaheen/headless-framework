// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Tests.Capabilities;
using Tests.RequestReply;

namespace Tests;

/// <summary>
/// Startup rejection against the real Azure Service Bus provider wiring. Each scenario fails at bootstrap, before the
/// provider opens a connection, so it needs neither the protected namespace credential nor the emulator and its license.
/// </summary>
public sealed class AzureServiceBusStartupRejectionTests : TestBase
{
    // A well-formed connection string nothing listens behind: every scenario here fails before a connection opens.
    private const string _UnreachableConnectionString =
        "Endpoint=sb://localhost:5672;SharedAccessKeyName=RootManageSharedAccessKey;"
        + "SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    /// <summary>
    /// Both profiles' configuration: no sessions, so a Queue route has no native key mapping and requiring routing
    /// affinity on it fails startup.
    /// </summary>
    [Fact]
    public Task should_reject_a_route_requiring_routing_affinity_at_startup_before_any_side_effect() =>
        TransportStartupRejectionConformance.AssertRoutingAffinityRejectedAsync(
            setup => setup.UseAzureServiceBus(options => options.ConnectionString = _UnreachableConnectionString),
            AbortToken
        );

    [Fact]
    public Task should_reject_requests_and_responders_at_startup_on_a_transport_without_request_reply() =>
        TransportRequestReplyConformance.AssertRejectedAtStartupAsync(new EmulatorProfileStartupDriver(), AbortToken);

    // The emulator profile runs the provider at its defaults on entities Emulator/Config.json declares.
    private static void _ConfigureEmulatorProfileTransport(MessagingSetupBuilder setup) =>
        setup.UseAzureServiceBus(options => options.ConnectionString = _UnreachableConnectionString);

    /// <summary>Selects the emulator profile's transport for the request/reply startup proof; it opens no session.</summary>
    private sealed class EmulatorProfileStartupDriver : TransportProviderConformanceDriver
    {
        private static readonly TransportConformanceProfile _Profile = TransportConformanceManifest.Providers[
            AzureServiceBusEmulatorFixture.ProviderName
        ];

        public override string ProviderName => _Profile.Provider;

        public override TransportMalformedEnvelopeBound MalformedEnvelopeBound => _Profile.MalformedEnvelopeBound!;

        public override void ConfigureRequestReplyTransport(MessagingSetupBuilder setup) =>
            _ConfigureEmulatorProfileTransport(setup);

        public override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
            TransportConformanceEndpoint endpoint,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("The startup rejection proofs open no session.");
    }
}
