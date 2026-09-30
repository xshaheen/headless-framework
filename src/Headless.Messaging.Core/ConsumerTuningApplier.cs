// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Messaging.CircuitBreaker;
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

            if (_RejectDurableSettingsOnEveryInstance(tuned, tuning.Identity, tuning, errors))
            {
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
            InboxRetention = tuning.InboxRetention ?? metadata.InboxRetention,
            CircuitBreakerOverride = tuning.CircuitBreaker ?? metadata.CircuitBreakerOverride,
        };
    }

    // An every-instance subscription belongs to one process and delivers at most once, with no inbox and no retry
    // backlog, so an inbox retention or a circuit breaker on it would silently do nothing.
    private static bool _RejectDurableSettingsOnEveryInstance(
        ConsumerMetadata[] tuned,
        string identity,
        ConsumerTuning tuning,
        List<string> errors
    )
    {
        if (tuning.InboxRetention is null && tuning.CircuitBreaker is null)
        {
            return false;
        }

        if (!tuned.Any(x => x.EveryInstance && string.Equals(x.ConsumerIdentity, identity, StringComparison.Ordinal)))
        {
            return false;
        }

        var setting = tuning.InboxRetention is not null ? "an inbox retention" : "a circuit breaker";
        errors.Add(
            $"Tune gives every-instance consumer '{identity}' {setting}. An every-instance subscription belongs to one "
                + "process and delivers at most once, with no inbox, retry, or circuit breaker."
        );

        return true;
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

            var settings = _ReadConfiguredSettings(consumer, errors);
            if (settings.IsEmpty)
            {
                continue;
            }

            var tuning = new ConsumerTuning(
                consumer.Key,
                settings.Concurrency,
                FailurePolicy: null,
                Middleware: [],
                ProviderConfigs: new Dictionary<Type, object>(),
                settings.InboxRetention,
                settings.CircuitBreaker
            );

            if (!_RejectDurableSettingsOnEveryInstance(tuned, consumer.Key, tuning, errors))
            {
                _Apply(tuned, consumer.Key, metadata => _Tune(metadata, tuning));
            }
        }
    }

    private static ConfiguredSettings _ReadConfiguredSettings(IConfigurationSection consumer, List<string> errors)
    {
        byte? concurrency = null;
        TimeSpan? inboxRetention = null;
        ConsumerCircuitBreakerOptions? circuitBreaker = null;

        foreach (var setting in consumer.GetChildren())
        {
            if (string.Equals(setting.Key, "Concurrency", StringComparison.OrdinalIgnoreCase))
            {
                if (
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
            else if (string.Equals(setting.Key, "InboxRetention", StringComparison.OrdinalIgnoreCase))
            {
                inboxRetention = _ReadInboxRetention(setting, errors);
            }
            else if (string.Equals(setting.Key, "CircuitBreaker", StringComparison.OrdinalIgnoreCase))
            {
                circuitBreaker = _ReadCircuitBreaker(setting, errors);
            }
            else
            {
                errors.Add(
                    $"Configuration '{setting.Path}' is not a consumer setting. The supported settings are "
                        + "Concurrency, InboxRetention, and CircuitBreaker."
                );
            }
        }

        return new ConfiguredSettings(concurrency, inboxRetention, circuitBreaker);
    }

    private static TimeSpan? _ReadInboxRetention(IConfigurationSection setting, List<string> errors)
    {
        if (TimeSpan.TryParse(setting.Value, CultureInfo.InvariantCulture, out var retention))
        {
            try
            {
                return ConsumerTuningBuilder.ValidateInboxRetention(retention);
            }
            catch (ArgumentException)
            {
                // Reported below with the configuration path, which the argument exception does not carry.
            }
        }

        errors.Add(
            $"Configuration '{setting.Path}' must be a positive whole-second duration such as '30.00:00:00', no "
                + "greater than Int32.MaxValue seconds."
        );

        return null;
    }

    private static ConsumerCircuitBreakerOptions? _ReadCircuitBreaker(
        IConfigurationSection section,
        List<string> errors
    )
    {
        var options = new ConsumerCircuitBreakerOptions();
        var valid = true;

        foreach (var setting in section.GetChildren())
        {
            if (string.Equals(setting.Key, "Enabled", StringComparison.OrdinalIgnoreCase))
            {
                if (bool.TryParse(setting.Value, out var enabled))
                {
                    options.Enabled = enabled;
                    continue;
                }

                errors.Add($"Configuration '{setting.Path}' must be true or false.");
            }
            else if (string.Equals(setting.Key, "FailureThreshold", StringComparison.OrdinalIgnoreCase))
            {
                if (
                    int.TryParse(setting.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var threshold)
                    && threshold > 0
                )
                {
                    options.FailureThreshold = threshold;
                    continue;
                }

                errors.Add($"Configuration '{setting.Path}' must be a positive integer.");
            }
            else if (string.Equals(setting.Key, "OpenDuration", StringComparison.OrdinalIgnoreCase))
            {
                if (
                    TimeSpan.TryParse(setting.Value, CultureInfo.InvariantCulture, out var openDuration)
                    && openDuration > TimeSpan.Zero
                )
                {
                    options.OpenDuration = openDuration;
                    continue;
                }

                errors.Add($"Configuration '{setting.Path}' must be a positive duration such as '00:00:30'.");
            }
            else
            {
                errors.Add(
                    $"Configuration '{setting.Path}' is not a circuit breaker setting. The supported settings are "
                        + "Enabled, FailureThreshold, and OpenDuration."
                );
            }

            valid = false;
        }

        return valid ? options : null;
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

    private readonly record struct ConfiguredSettings(
        byte? Concurrency,
        TimeSpan? InboxRetention,
        ConsumerCircuitBreakerOptions? CircuitBreaker
    )
    {
        public bool IsEmpty => Concurrency is null && InboxRetention is null && CircuitBreaker is null;
    }
}
