// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Startup rejection against a provider's real transport wiring: a host that asks for what the transport lacks fails
/// its bootstrap before it touches storage or reports ready, so a misconfigured host never creates anything it would
/// then abandon.
/// </summary>
[PublicAPI]
public static class TransportStartupRejectionConformance
{
    /// <summary>
    /// Proves that a transport without every-instance subscriptions rejects an every-instance consumer at bootstrap,
    /// naming the consumer and the provider, before storage is resolved and without reporting ready.
    /// </summary>
    /// <param name="configureTransport">Selects the provider exactly as a consuming host would.</param>
    /// <param name="cancellationToken">Cancels the bootstrap.</param>
    public static async Task AssertEveryInstanceRejectedAsync(
        Action<MessagingSetupBuilder> configureTransport,
        CancellationToken cancellationToken
    )
    {
        var storageResolveCalls = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(configureTransport);
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<EveryInstanceProbe>(EveryInstanceProbeModule.MessageName);
            messaging.AddModule<EveryInstanceProbeModule>();
        });

        // Storage is declared but never built: resolving its table names is the first thing a bootstrap that got past
        // validation would do, so a count above zero is a side effect the rejection should have prevented.
        services.AddMessagingProviderCapabilities(
            MessagingProviderCapabilities.Storage(
                "StartupRejectionStorage",
                [MessageLane.Bus, MessageLane.Queue],
                supportsDelayedScheduling: true,
                inboxGuarantee: InboxGuarantee.Transactional
            )
        );
        services.AddSingleton<IStorageTableNames>(_ =>
        {
            Interlocked.Increment(ref storageResolveCalls);
            return new UnusedStorageTableNames();
        });

        await using var provider = services.BuildServiceProvider();
        var transport = provider
            .GetServices<MessagingProviderCapabilities>()
            .Single(capabilities => capabilities.Role == MessagingProviderRole.Transport);
        transport
            .SupportsEveryInstance.Should()
            .BeFalse("only a transport without every-instance subscriptions has this rejection to prove");

        var bootstrapper = provider.GetRequiredService<IBootstrapper>();
        var act = () => bootstrapper.BootstrapAsync(cancellationToken);

        await act.Should()
            .ThrowAsync<MessagingConfigurationException>()
            .WithMessage($"*'{EveryInstanceProbeModule.ConsumerIdentity}'*every-instance*'{transport.Provider}'*");
        bootstrapper.IsStarted.Should().BeFalse("a rejected host never reports readiness");
        Volatile.Read(ref storageResolveCalls).Should().Be(0, "the rejection must come before any storage work");
    }

    private sealed record EveryInstanceProbe;

    private sealed class EveryInstanceProbeConsumer : IConsume<EveryInstanceProbe>
    {
        public ValueTask ConsumeAsync(
            ConsumeContext<EveryInstanceProbe> context,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;
    }

    private sealed class EveryInstanceProbeModule : IMessagingModule
    {
        public const string MessageName = "conformance.startup.every-instance";

        public const string ConsumerIdentity = "conformance.startup.every-instance-consumer";

        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddBusConsumer<EveryInstanceProbeConsumer, EveryInstanceProbe>(
                ConsumerIdentity,
                everyInstance: true,
                static (_, _, _) => ValueTask.CompletedTask
            );
    }

    private sealed class UnusedStorageTableNames : IStorageTableNames
    {
        public string GetPublishedTableName() => "published";

        public string GetReceivedTableName() => "received";
    }
}
