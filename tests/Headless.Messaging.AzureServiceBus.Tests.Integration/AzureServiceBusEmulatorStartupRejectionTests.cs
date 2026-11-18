// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Tests.Capabilities;
using Tests.RequestReply;

namespace Tests;

/// <summary>
/// Startup rejection with the configuration the emulator profile runs: provisioning off. Each scenario fails at
/// bootstrap, before the provider opens a connection, so it runs against the real provider wiring with no emulator
/// started and no license to accept.
/// </summary>
public sealed class AzureServiceBusEmulatorStartupRejectionTests : TestBase
{
    // The emulator's connection string shape. Nothing connects to it: every scenario here fails before a connection opens.
    private const string _UnreachableEmulatorConnectionString =
        "Endpoint=sb://localhost:5672;SharedAccessKeyName=RootManageSharedAccessKey;"
        + "SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    [Fact]
    public Task should_reject_an_every_instance_consumer_at_startup_when_provisioning_is_off() =>
        TransportStartupRejectionConformance.AssertEveryInstanceRejectedAsync(_ConfigureTransport, AbortToken);

    [Fact]
    public Task should_reject_requests_and_responders_at_startup_on_a_transport_without_request_reply() =>
        TransportRequestReplyConformance.AssertRejectedAtStartupAsync(new StartupOnlyDriver(), AbortToken);

    private static void _ConfigureTransport(MessagingSetupBuilder setup) =>
        setup.UseAzureServiceBus(options =>
        {
            options.ConnectionString = _UnreachableEmulatorConnectionString;
            options.AutoProvision = false;
        });

    /// <summary>Selects the emulator profile's transport for the request/reply startup proof; it opens no session.</summary>
    private sealed class StartupOnlyDriver : TransportProviderConformanceDriver
    {
        private static readonly TransportConformanceProfile _Profile = TransportConformanceManifest.Providers[
            AzureServiceBusEmulatorFixture.ProviderName
        ];

        public override string ProviderName => _Profile.Provider;

        public override TransportMalformedEnvelopeBound MalformedEnvelopeBound => _Profile.MalformedEnvelopeBound!;

        public override void ConfigureRequestReplyTransport(MessagingSetupBuilder setup) => _ConfigureTransport(setup);

        public override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
            TransportConformanceEndpoint endpoint,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("The startup rejection proofs open no session.");
    }
}
