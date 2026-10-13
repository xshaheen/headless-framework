// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text.Json;
using Headless.Checks;
using Headless.Hosting;
using Headless.Reliability;

namespace Headless.Messaging;

/// <summary>
/// Writes the <c>messaging</c> section of the host description: every consumer identity this host registered, read from
/// the host's frozen registry, so the description shows the tuning and <c>ConsumeOnly</c> filter the host actually runs
/// with.
/// </summary>
/// <remarks>
/// One entry is written per identity and lane, because an identity may own a Queue message and consume Bus messages,
/// and each lane opens its own subscription with its own tuning. Settings an every-instance consumer cannot have (an
/// inbox, a failure policy, a <c>ConsumeOnly</c> state) are omitted instead of being written as <see langword="null"/>.
/// Provider-specific consumer configuration and runtime subscriptions are never written: the first can carry broker
/// addresses, and the second is not part of what the host was built with.
/// </remarks>
/// <param name="registry">The host's consumer registry; resolving it freezes the registry if nothing has yet.</param>
internal sealed class MessagingHostDescriptionContributor(ConsumerRegistry registry) : IHostDescriptionContributor
{
    private readonly ConsumerRegistry _registry = Argument.IsNotNull(registry);

    public string SectionName => "messaging";

    public void WriteSection(Utf8JsonWriter writer)
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
        writer.WriteEndObject();
    }

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
            _WriteFailurePolicy(writer, failurePolicy);
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
    // whole description.
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
        writer.WriteString("algorithm", rateLimit.Algorithm);

        // Only the parameters of the rate's own algorithm are set; the others are left at zero.
        if (string.Equals(rateLimit.Algorithm, "token-bucket", StringComparison.Ordinal))
        {
            writer.WriteNumber("tokenLimit", rateLimit.TokenLimit);
            writer.WriteNumber("tokensPerPeriod", rateLimit.TokensPerPeriod);
            writer.WriteNumber("replenishmentPeriodSeconds", rateLimit.ReplenishmentPeriod.TotalSeconds);
        }
        else
        {
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

    private static void _WriteFailurePolicy(Utf8JsonWriter writer, FailurePolicyDefinition policy)
    {
        writer.WriteStartObject();
        writer.WriteNumber("immediateRetries", policy.ImmediateRetries);
        writer.WriteNumber("delayedRetries", policy.DelayedRetries);

        // Delays only pace delayed retries, so they mean nothing to a policy that has none.
        if (policy.DelayedRetries > 0)
        {
            writer.WriteNumber("delayedInitialDelaySeconds", policy.DelayedInitialDelay.TotalSeconds);
            writer.WriteNumber("delayedMaxDelaySeconds", policy.DelayedMaxDelay.TotalSeconds);
        }

        if (policy.FailOnExceptionTypes.Count != 0)
        {
            writer.WriteStartArray("failOn");

            foreach (var type in policy.FailOnExceptionTypes)
            {
                writer.WriteStringValue(type.FullName ?? type.Name);
            }

            writer.WriteEndArray();
        }

        if (policy.FailWhenRuleCount != 0)
        {
            writer.WriteNumber("failWhenRules", policy.FailWhenRuleCount);
        }

        writer.WriteEndObject();
    }
}
