// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text.Json;
using Headless.Checks;
using Headless.Hosting;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Reliability;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging;

/// <summary>
/// Writes the <c>messaging</c> section of the host manifest: every consumer identity this host registered, read from
/// the host's frozen registry, so the manifest shows the tuning and <c>ConsumeOnly</c> filter the host actually runs
/// with; and the publish side, which is every declared message contract, the outboxes, and the transport of each lane.
/// </summary>
/// <remarks>
/// One entry is written per identity and lane, because an identity may own a Queue message and consume Bus messages,
/// and each lane opens its own subscription with its own tuning. Settings an every-instance consumer cannot have (an
/// inbox, a failure policy, a <c>ConsumeOnly</c> state) are omitted instead of being written as <see langword="null"/>.
/// Provider-specific consumer configuration and runtime subscriptions are never written: the first can carry broker
/// addresses, and the second is not part of what the host was built with. A transport is named by its broker name
/// only, never its endpoint, and an outbox by its storage provider, never its connection.
/// </remarks>
/// <param name="registry">The host's consumer registry; resolving it freezes the registry if nothing has yet.</param>
/// <param name="services">
/// The host's services, read only when the section is written so building the section resolves no storage or transport.
/// </param>
internal sealed class MessagingHostManifestSection(ConsumerRegistry registry, IServiceProvider services)
    : IHostManifestSection
{
    private const string _PrimaryOutbox = "primary";

    private readonly ConsumerRegistry _registry = Argument.IsNotNull(registry);
    private readonly IServiceProvider _services = Argument.IsNotNull(services);

    public string Name => "messaging";

    public void Write(Utf8JsonWriter writer)
    {
        Argument.IsNotNull(writer);

        var consumers = _registry.GetAll();
        var filter = _registry.ConsumeFilter;

        writer.WriteStartObject();
        writer.WriteStartArray("consumers");

        foreach (
            var entry in consumers
                .GroupBy(static x => (x.ConsumerIdentity, x.Lane))
                .OrderBy(static x => x.Key.ConsumerIdentity, StringComparer.Ordinal)
                .ThenBy(static x => x.Key.Lane)
        )
        {
            _WriteConsumer(writer, [.. entry], filter);
        }

        writer.WriteEndArray();

        _WriteMessages(writer, _registry.DeclaredRoutes);
        _WriteOutboxes(writer, _services.GetService<MessagingOutboxes>());
        _WriteTransports(writer, _services.GetService<IBusTransport>(), _services.GetService<IQueueTransport>());

        writer.WriteEndObject();
    }

    // One entry per declared contract, with the lanes it publishes on. Settings at their default are omitted, so two
    // manifests of the same declarations diff cleanly and a new default does not show up as a change.
    private static void _WriteMessages(Utf8JsonWriter writer, IReadOnlyList<MessageRegistration> routes)
    {
        writer.WriteStartArray("messages");

        foreach (
            var message in routes
                .Where(static x => x.MessageName is not null)
                .GroupBy(static x => x.MessageName!, StringComparer.Ordinal)
                .OrderBy(static x => x.Key, StringComparer.Ordinal)
        )
        {
            writer.WriteStartObject();
            writer.WriteString("name", message.Key);
            writer.WriteString("contractVersion", message.First().ContractVersion);
            writer.WriteStartArray("lanes");

            foreach (var route in message.OrderBy(static x => x.Lane))
            {
                writer.WriteStartObject();
                writer.WriteString("lane", _Lane(route.Lane));

                if (route.DeliveryMode is { } deliveryMode)
                {
                    writer.WriteString("deliveryMode", _DeliveryMode(deliveryMode));
                }

                if (route.RequiresRoutingAffinity)
                {
                    writer.WriteBoolean("routingAffinity", value: true);
                }

                if (route.RawPayload)
                {
                    writer.WriteBoolean("rawPayload", value: true);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    // A host without storage has no outboxes: it publishes directly or not at all.
    private static void _WriteOutboxes(Utf8JsonWriter writer, MessagingOutboxes? outboxes)
    {
        writer.WriteStartArray("outboxes");

        if (outboxes?.Primary is { } primary)
        {
            _WriteOutbox(writer, _PrimaryOutbox, primary);
        }

        // Named by registration order, as their health checks are: the registration's own name describes the call that
        // made it, which two outboxes on one provider share.
        var secondaries = outboxes?.Secondaries ?? [];

        for (var i = 0; i < secondaries.Count; i++)
        {
            _WriteOutbox(writer, "outbox-" + (i + 1).ToString(CultureInfo.InvariantCulture), secondaries[i].Storage);
        }

        writer.WriteEndArray();
    }

    private static void _WriteOutbox(Utf8JsonWriter writer, string name, IDataStorage storage)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        // The provider only: a relational storage knows its database engine; any other storage is named by its type.
        writer.WriteString(
            "storage",
            storage is RelationalDataStorage relational ? relational.ProviderName : storage.GetType().Name
        );
        writer.WriteEndObject();
    }

    // The broker name only: the endpoint half of a broker address is a host name or connection target.
    private static void _WriteTransports(Utf8JsonWriter writer, ITransport? bus, ITransport? queue)
    {
        writer.WriteStartObject("transports");

        if (bus is not null)
        {
            writer.WriteString("bus", bus.BrokerAddress.Name);
        }

        if (queue is not null)
        {
            writer.WriteString("queue", queue.BrokerAddress.Name);
        }

        writer.WriteEndObject();
    }

    private static string _DeliveryMode(DeliveryMode deliveryMode) =>
        deliveryMode switch
        {
            DeliveryMode.Durable => "durable",
            DeliveryMode.Direct => "direct",
            _ => ((int)deliveryMode).ToString(CultureInfo.InvariantCulture),
        };

    private static void _WriteConsumer(
        Utf8JsonWriter writer,
        IReadOnlyList<ConsumerMetadata> entries,
        MessagingConsumeFilter filter
    )
    {
        // Tuning applies by identity, so every message entry of one identity and lane carries the same settings.
        var first = entries[0];

        writer.WriteStartObject();
        writer.WriteString("identity", first.ConsumerIdentity);
        writer.WriteString("lane", _Lane(first.Lane));
        writer.WriteString("delivery", first.EveryInstance ? "every-instance" : "competing");
        writer.WriteString("runState", filter.Allows(first) ? "runs-here" : "registered-not-run-here");

        // ConsumeOnly never applies to an every-instance consumer, and an unfiltered host has no list to be in.
        if (!first.EveryInstance && filter.ConsumedIdentities is not null)
        {
            writer.WriteString("consumeOnly", filter.Allows(first) ? "listed" : "not-listed");
        }

        writer.WriteStartArray("messages");

        foreach (var message in entries.OrderBy(static x => x.MessageName, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", message.MessageName);
            writer.WriteString("contractVersion", message.MessageContractVersion);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteNumber("concurrency", first.Concurrency);

        if (first.FailurePolicy is { } failurePolicy)
        {
            writer.WritePropertyName("failurePolicy");
            failurePolicy.WriteDescription(writer);
        }

        // An every-instance subscription stores no inbox rows, so a retention means nothing for it.
        if (!first.EveryInstance)
        {
            writer.WriteNumber("inboxRetentionSeconds", (long)first.InboxRetention.TotalSeconds);
        }

        if (first.RateLimit is { } rateLimit)
        {
            _WriteRateLimit(writer, rateLimit);
        }

        if (first.CircuitBreakerOverride is { } circuitBreaker)
        {
            _WriteCircuitBreaker(writer, circuitBreaker);
        }

        if (first.Middleware.Count != 0)
        {
            writer.WriteStartArray("middleware");

            foreach (var middleware in first.Middleware)
            {
                writer.WriteStringValue(middleware.FullName ?? middleware.Name);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    // A value this binary does not name, such as a lane added later, is written as its number rather than failing the
    // whole manifest.
    private static string _Lane(MessageLane lane) =>
        lane switch
        {
            MessageLane.Bus => "bus",
            MessageLane.Queue => "queue",
            _ => ((int)lane).ToString(CultureInfo.InvariantCulture),
        };

    private static void _WriteRateLimit(Utf8JsonWriter writer, ConsumerRateLimit rateLimit)
    {
        writer.WriteStartObject("rateLimit");

        // Only the parameters of the rate's own algorithm are set; the others are left at zero.
        if (rateLimit.Algorithm == ConsumerRateLimit.LimiterAlgorithm.TokenBucket)
        {
            writer.WriteString("algorithm", "token-bucket");
            writer.WriteNumber("tokenLimit", rateLimit.TokenLimit);
            writer.WriteNumber("tokensPerPeriod", rateLimit.TokensPerPeriod);
            writer.WriteNumber("replenishmentPeriodSeconds", rateLimit.ReplenishmentPeriod.TotalSeconds);
        }
        else
        {
            writer.WriteString("algorithm", "fixed-window");
            writer.WriteNumber("permitLimit", rateLimit.PermitLimit);
            writer.WriteNumber("windowSeconds", rateLimit.Window.TotalSeconds);
        }

        writer.WriteEndObject();
    }

    // Only the overrides are written: an unset value falls back to the host-wide circuit breaker options.
    private static void _WriteCircuitBreaker(Utf8JsonWriter writer, ConsumerCircuitBreakerOptions options)
    {
        writer.WriteStartObject("circuitBreaker");
        writer.WriteBoolean("enabled", options.Enabled);

        if (options.FailureThreshold is { } threshold)
        {
            writer.WriteNumber("failureThreshold", threshold);
        }

        if (options.OpenDuration is { } openDuration)
        {
            writer.WriteNumber("openDurationSeconds", openDuration.TotalSeconds);
        }

        if (options.IsTransientException is not null)
        {
            writer.WriteBoolean("customTransientPredicate", value: true);
        }

        writer.WriteEndObject();
    }
}
