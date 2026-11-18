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
    /// Proves that a Queue route requiring routing affinity is rejected at bootstrap when the transport has no native key
    /// mapping for it, before storage is resolved and without reporting ready. Every transport has such a route: one
    /// without a native mapping at all, or a destination its mapping does not cover, such as a standard SQS queue or an
    /// Azure Service Bus queue without sessions.
    /// </summary>
    /// <param name="configureTransport">
    /// Selects the provider exactly as a consuming host would, with no native key mapping for
    /// <paramref name="messageName"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the bootstrap.</param>
    /// <param name="messageName">The Queue route that requires routing affinity.</param>
    public static Task AssertRoutingAffinityRejectedAsync(
        Action<MessagingSetupBuilder> configureTransport,
        CancellationToken cancellationToken,
        string messageName = "conformance.startup.required-affinity"
    ) =>
        _AssertRejectedAsync(
            configureTransport,
            messaging => messaging.Message<StartupProbe>(messageName).OnQueue(queue => queue.RequireRoutingAffinity()),
            _ => $"*Routing affinity is required but unsupported for '{messageName}'*",
            cancellationToken
        );

    /// <summary>
    /// Proves that a transport without every-instance subscriptions rejects an every-instance consumer at bootstrap,
    /// naming the consumer and the provider, before storage is resolved and without reporting ready.
    /// </summary>
    /// <param name="configureTransport">Selects the provider exactly as a consuming host would.</param>
    /// <param name="cancellationToken">Cancels the bootstrap.</param>
    public static Task AssertEveryInstanceRejectedAsync(
        Action<MessagingSetupBuilder> configureTransport,
        CancellationToken cancellationToken
    ) =>
        _AssertRejectedAsync(
            configureTransport,
            messaging =>
            {
                messaging.Message<StartupProbe>(EveryInstanceProbeModule.MessageName);
                messaging.AddModule<EveryInstanceProbeModule>();
            },
            transport =>
            {
                transport
                    .SupportsEveryInstance.Should()
                    .BeFalse("only a transport without every-instance subscriptions has this rejection to prove");

                return $"*'{EveryInstanceProbeModule.ConsumerIdentity}'*every-instance*'{transport.Provider}'*";
            },
            cancellationToken
        );

    private static async Task _AssertRejectedAsync(
        Action<MessagingSetupBuilder> configureTransport,
        Action<MessagingContributionBuilder> configureMessaging,
        Func<MessagingProviderCapabilities, string> expectedMessage,
        CancellationToken cancellationToken
    )
    {
        var storageResolveCalls = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(configureTransport);
        services.ConfigureMessaging(configureMessaging);

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
        var message = expectedMessage(transport);

        var bootstrapper = provider.GetRequiredService<IBootstrapper>();
        var act = () => bootstrapper.BootstrapAsync(cancellationToken);

        await act.Should().ThrowAsync<MessagingConfigurationException>().WithMessage(message);
        bootstrapper.IsStarted.Should().BeFalse("a rejected host never reports readiness");
        Volatile.Read(ref storageResolveCalls).Should().Be(0, "the rejection must come before any storage work");
    }

    private sealed record StartupProbe;

    private sealed class EveryInstanceProbeConsumer : IConsume<StartupProbe>
    {
        public ValueTask ConsumeAsync(ConsumeContext<StartupProbe> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class EveryInstanceProbeModule : IMessagingModule
    {
        public const string MessageName = "conformance.startup.every-instance";

        public const string ConsumerIdentity = "conformance.startup.every-instance-consumer";

        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddBusConsumer<EveryInstanceProbeConsumer, StartupProbe>(
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
