// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Headless.Messaging;

/// <summary>The host-level controls applied to the consumers when the registrations drain.</summary>
/// <param name="Tunings">Every <c>Tune</c> call, in registration order.</param>
/// <param name="ConsumeOnly">Every <c>ConsumeOnly</c> entry.</param>
/// <param name="Configuration">The host configuration that tunes consumers by identity, if any.</param>
internal sealed record MessagingHostControls(
    IReadOnlyList<ConsumerTuning> Tunings,
    IReadOnlyList<string> ConsumeOnly,
    IConfiguration? Configuration
);

/// <summary>Applies <c>Tune</c> calls and then configuration to the host's consumers, by identity.</summary>
internal static class ConsumerTuningApplier
{
    /// <summary>The configuration section whose children tune consumers by identity.</summary>
    internal const string TuningConfigurationSection = "Headless:Messaging:Consumers";

    /// <summary>
    /// Returns <paramref name="consumers"/> with every tuning applied, in the same order. A consumer that covers several
    /// messages has one entry per message, and each takes the tuning of its identity.
    /// </summary>
    /// <param name="consumers">The host's consumers, one entry per consumed message.</param>
    /// <param name="controls">The tunings and configuration to apply.</param>
    /// <param name="errors">Collects a tuning that names an unknown identity or carries an invalid value.</param>
    public static ConsumerMetadata[] Apply(
        IReadOnlyList<ConsumerMetadata> consumers,
        MessagingHostControls controls,
        List<string> errors
    )
    {
        var tuned = consumers.ToArray();
        var identities = consumers.Select(static x => x.ConsumerIdentity).ToHashSet(StringComparer.Ordinal);

        foreach (var tuning in controls.Tunings)
        {
            if (!identities.Contains(tuning.Identity))
            {
                errors.Add($"Tune names consumer '{tuning.Identity}', which no registered consumer declares.");
                continue;
            }

            _Apply(tuned, tuning.Identity, metadata => _Tune(metadata, tuning));
        }

        if (controls.Configuration is { } configuration)
        {
            _ApplyConfiguration(configuration, tuned, identities, errors);
        }

        return tuned;
    }

    private static ConsumerMetadata _Tune(ConsumerMetadata metadata, ConsumerTuning tuning)
    {
        var providerConfigs = metadata.ProviderConfigs;
        if (tuning.ProviderConfigs.Count != 0)
        {
            var merged = new Dictionary<Type, object>(metadata.ProviderConfigs);
            foreach (var (type, config) in tuning.ProviderConfigs)
            {
                merged[type] = config;
            }

            providerConfigs = merged;
        }

        return metadata with
        {
            Concurrency = tuning.Concurrency ?? metadata.Concurrency,
            FailurePolicy = tuning.FailurePolicy ?? metadata.FailurePolicy,
            Middleware = [.. metadata.Middleware.Union(tuning.Middleware)],
            ProviderConfigs = providerConfigs,
        };
    }

    private static void _ApplyConfiguration(
        IConfiguration configuration,
        ConsumerMetadata[] tuned,
        HashSet<string> identities,
        List<string> errors
    )
    {
        foreach (var consumer in configuration.GetSection(TuningConfigurationSection).GetChildren())
        {
            var path = $"{TuningConfigurationSection}:{consumer.Key}";
            if (!identities.Contains(consumer.Key))
            {
                errors.Add(
                    $"Configuration '{path}' names consumer '{consumer.Key}', which no registered consumer declares."
                );
                continue;
            }

            byte? concurrency = null;
            foreach (var setting in consumer.GetChildren())
            {
                if (!string.Equals(setting.Key, "Concurrency", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(
                        $"Configuration '{setting.Path}' is not a consumer setting. The supported setting is Concurrency."
                    );
                }
                else if (
                    byte.TryParse(setting.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                    && value > 0
                )
                {
                    concurrency = value;
                }
                else
                {
                    errors.Add($"Configuration '{setting.Path}' must be an integer from 1 to {byte.MaxValue}.");
                }
            }

            if (concurrency is { } limit)
            {
                _Apply(tuned, consumer.Key, metadata => metadata with { Concurrency = limit });
            }
        }
    }

    private static void _Apply(
        ConsumerMetadata[] tuned,
        string identity,
        Func<ConsumerMetadata, ConsumerMetadata> apply
    )
    {
        for (var index = 0; index < tuned.Length; index++)
        {
            if (string.Equals(tuned[index].ConsumerIdentity, identity, StringComparison.Ordinal))
            {
                tuned[index] = apply(tuned[index]);
            }
        }
    }
}
