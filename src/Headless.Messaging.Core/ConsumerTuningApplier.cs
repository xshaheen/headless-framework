// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Messaging.CircuitBreaker;
using Headless.Reliability;
using Microsoft.Extensions.Configuration;

namespace Headless.Messaging;

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

            if (_RejectDurableSettingsOnEveryInstance(tuned, tuning.Identity, "Tune", tuning, errors))
            {
                continue;
            }

            _Apply(tuned, tuning.Identity, metadata => _Tune(metadata, tuning));
        }

        // The host default fills in only after every Tune call, so a tuned policy replaces the declared one and an
        // undeclared, untuned consumer still gets a policy that configuration can then adjust.
        for (var index = 0; index < tuned.Length; index++)
        {
            if (tuned[index] is { EveryInstance: false, FailurePolicy: null })
            {
                tuned[index] = tuned[index] with { FailurePolicy = controls.DefaultFailurePolicy };
            }
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
            Middleware = [.. metadata.Middleware.Union(tuning.Middleware)],
            ProviderConfigs = providerConfigs,
            InboxRetention = tuning.InboxRetention ?? metadata.InboxRetention,
            CircuitBreakerOverride = tuning.CircuitBreaker ?? metadata.CircuitBreakerOverride,
            FailurePolicy = tuning.FailurePolicy ?? metadata.FailurePolicy,
        };
    }

    // An every-instance subscription belongs to one process and delivers at most once, with no inbox and no retry
    // backlog, so an inbox retention, a circuit breaker, or a failure policy on it would silently do nothing.
    private static bool _RejectDurableSettingsOnEveryInstance(
        ConsumerMetadata[] tuned,
        string identity,
        string source,
        ConsumerTuning tuning,
        List<string> errors,
        bool configuresFailurePolicy = false
    )
    {
        var setting =
            tuning.InboxRetention is not null ? "an inbox retention"
            : tuning.CircuitBreaker is not null ? "a circuit breaker"
            : tuning.FailurePolicy is not null || configuresFailurePolicy ? "a failure policy"
            : null;

        if (setting is null)
        {
            return false;
        }

        if (!tuned.Any(x => x.EveryInstance && string.Equals(x.ConsumerIdentity, identity, StringComparison.Ordinal)))
        {
            return false;
        }

        errors.Add(
            $"{source} gives every-instance consumer '{identity}' {setting}. An every-instance subscription belongs to "
                + "one process and delivers at most once, with no inbox, retry, or circuit breaker."
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
                Middleware: [],
                ProviderConfigs: new Dictionary<Type, object>(),
                settings.InboxRetention,
                settings.CircuitBreaker
            );

            if (
                _RejectDurableSettingsOnEveryInstance(
                    tuned,
                    consumer.Key,
                    $"Configuration '{path}'",
                    tuning,
                    errors,
                    configuresFailurePolicy: settings.FailurePolicy is not null
                )
            )
            {
                continue;
            }

            _Apply(tuned, consumer.Key, metadata => _Tune(metadata, tuning));

            if (settings.FailurePolicy is { } overrides)
            {
                _ApplyFailurePolicyOverrides(tuned, consumer.Key, $"{path}:FailurePolicy", overrides, errors);
            }
        }
    }

    // One identity can be declared on both the Bus and the Queue lane, each with its own policy, so the overrides are
    // applied to every entry's own definition. Entries that shared one definition keep sharing the overridden result.
    // The overrides are the same for every entry, so the first invalid result is reported once for the path.
    private static void _ApplyFailurePolicyOverrides(
        ConsumerMetadata[] tuned,
        string identity,
        string path,
        FailurePolicyOverrides overrides,
        List<string> errors
    )
    {
        var resolvedBySource = new Dictionary<FailurePolicyDefinition, FailurePolicyDefinition>(
            ReferenceEqualityComparer.Instance
        );

        for (var index = 0; index < tuned.Length; index++)
        {
            var metadata = tuned[index];
            if (!string.Equals(metadata.ConsumerIdentity, identity, StringComparison.Ordinal))
            {
                continue;
            }

            var source = metadata.FailurePolicy!;
            if (!resolvedBySource.TryGetValue(source, out var resolved))
            {
                try
                {
                    resolved = source.With(overrides);
                }
                catch (ArgumentException exception)
                {
                    errors.Add($"Configuration '{path}' does not describe a valid failure policy: {exception.Message}");
                    return;
                }

                resolvedBySource.Add(source, resolved);
            }

            tuned[index] = metadata with { FailurePolicy = resolved };
        }
    }

    private static ConfiguredSettings _ReadConfiguredSettings(IConfigurationSection consumer, List<string> errors)
    {
        byte? concurrency = null;
        TimeSpan? inboxRetention = null;
        ConsumerCircuitBreakerOptions? circuitBreaker = null;
        FailurePolicyOverrides? failurePolicy = null;

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
            else if (string.Equals(setting.Key, "FailurePolicy", StringComparison.OrdinalIgnoreCase))
            {
                failurePolicy = _ReadFailurePolicy(setting, errors);
            }
            else
            {
                errors.Add(
                    $"Configuration '{setting.Path}' is not a consumer setting. The supported settings are "
                        + "Concurrency, InboxRetention, CircuitBreaker, and FailurePolicy."
                );
            }
        }

        return new ConfiguredSettings(concurrency, inboxRetention, circuitBreaker, failurePolicy);
    }

    // Only the numbers are configurable: fail rules are code, so they always come from the resolved policy.
    private static FailurePolicyOverrides? _ReadFailurePolicy(IConfigurationSection section, List<string> errors)
    {
        var settings = section.GetChildren().Select(setting => (setting.Key, setting.Path, setting.Value));

        return FailurePolicyOverrides.TryParse(settings, errors, out var overrides) ? overrides : null;
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
        ConsumerCircuitBreakerOptions? CircuitBreaker,
        FailurePolicyOverrides? FailurePolicy
    )
    {
        public bool IsEmpty =>
            Concurrency is null && InboxRetention is null && CircuitBreaker is null && FailurePolicy is null;
    }
}

/// <summary>The host-level controls applied to the consumers when the consumer registry is built.</summary>
/// <param name="Tunings">Every <c>Tune</c> call, in registration order.</param>
/// <param name="ConsumeOnly">Every <c>ConsumeOnly</c> entry.</param>
/// <param name="Configuration">The host configuration that tunes consumers by identity, if any.</param>
/// <param name="DefaultFailurePolicy">The failure policy of a competing consumer that neither declares nor tunes one.</param>
internal sealed record MessagingHostControls(
    IReadOnlyList<ConsumerTuning> Tunings,
    IReadOnlyList<string> ConsumeOnly,
    IConfiguration? Configuration,
    FailurePolicyDefinition DefaultFailurePolicy
);
