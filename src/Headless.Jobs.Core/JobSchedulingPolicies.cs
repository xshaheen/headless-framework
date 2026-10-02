// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Models;

namespace Headless.Jobs;

internal sealed class JobSchedulingPolicies
{
    internal static readonly JobSchedulingPolicies Empty = new(new JobOptions(), [], []);
    private readonly JobOptions _defaults;
    private readonly Dictionary<Type, JobOptions> _byRequest;
    private readonly Dictionary<string, JobOptions> _byFunction;

    internal JobSchedulingPolicies(
        JobOptions defaults,
        Dictionary<Type, JobOptions> byRequest,
        Dictionary<string, JobOptions> byFunction
    )
    {
        _defaults = Snapshot(defaults);
        _byRequest = byRequest.ToDictionary(pair => pair.Key, pair => Snapshot(pair.Value));
        _byFunction = byFunction.ToDictionary(pair => pair.Key, pair => Snapshot(pair.Value), StringComparer.Ordinal);
    }

    /// <summary>
    /// Combines the host's defaults and request-type overrides with the per-job options tuned into the host's registry.
    /// </summary>
    internal JobSchedulingPolicies WithFunctionOptions(IReadOnlyDictionary<string, JobOptions> byFunction) =>
        new(_defaults, _byRequest, byFunction.ToDictionary(StringComparer.Ordinal));

    internal void Validate(JobFunctionRegistry registry)
    {
        foreach (var request in _byRequest.Keys)
        {
            if (!registry.DescriptorsByRequestType.ContainsKey(request))
            {
                throw new InvalidOperationException(
                    $"Configured job request '{request}' has no generated handler in this host."
                );
            }
        }

        foreach (var function in _byFunction.Keys)
        {
            if (!registry.Descriptors.TryGetValue(function, out var descriptor))
            {
                throw new InvalidOperationException($"Configured job '{function}' is not registered in this host.");
            }

            if (descriptor.RequestType is { } requestType && _byRequest.ContainsKey(requestType))
            {
                throw new InvalidOperationException(
                    $"Job '{function}' is configured by both Tune options and ConfigureJob<{requestType.Name}>. Choose one."
                );
            }
        }
    }

    internal JobOptions ResolveRecurring(JobFunctionDescriptor descriptor, RecurringJobOptions? call) =>
        Resolve(
            descriptor,
            new JobOptions
            {
                Retries = call?.Retries,
                RetryIntervals = call?.RetryIntervals,
                OnNodeDeath = call?.OnNodeDeath,
            }
        );

    internal JobOptions Resolve(JobFunctionDescriptor descriptor, JobOptions? call)
    {
        var function =
            _byFunction.GetValueOrDefault(descriptor.FunctionName)
            ?? (descriptor.RequestType is { } requestType ? _byRequest.GetValueOrDefault(requestType) : null);
        var result = (call ?? _defaults) with
        {
            Retries = call?.Retries ?? function?.Retries ?? _defaults.Retries ?? 0,
            RetryIntervals = (call?.RetryIntervals ?? function?.RetryIntervals ?? _defaults.RetryIntervals)?.ToArray(),
            OnNodeDeath =
                call?.OnNodeDeath ?? function?.OnNodeDeath ?? _defaults.OnNodeDeath ?? Enums.NodeDeathPolicy.Retry,
            // The idempotency window is per call by contract: it is never inherited from host/function policy
            // (Snapshot rejects it there), so only the call's own key and TTL survive resolution.
            IdempotencyKey = call?.IdempotencyKey,
            IdempotencyTtl = call?.IdempotencyTtl,
        };
        _ValidateOptions(result);
        return result;
    }

    internal static JobOptions Snapshot(JobOptions options)
    {
        Argument.IsNotNull(options);
        _ValidateOptions(options);
        if (
            options.CorrelationId is not null
            || options.CausationId is not null
            || options.Description is not null
            || options.TenantId is not null
            || options.IsSystemJob
            || options.IdempotencyKey is not null
        )
        {
            throw new ArgumentException(
                "Startup job policies accept only retry and node-death settings. Supply invocation metadata on each call.",
                nameof(options)
            );
        }

        return options with
        {
            RetryIntervals = options.RetryIntervals?.ToArray(),
        };
    }

    private static void _ValidateOptions(JobOptions options)
    {
        if (options.Retries < 0 || options.RetryIntervals?.Any(interval => interval < 0) == true)
        {
            throw new ArgumentException("Job retries and retry intervals must be non-negative.", nameof(options));
        }

        if (options.OnNodeDeath is { } policy && !Enum.IsDefined(policy))
        {
            throw new ArgumentException("The node-death policy must be a defined value.", nameof(options));
        }

        if (options.IdempotencyKey is { } key)
        {
            // Same bounded-string rules as every other durable Jobs identity, so one validator and one collation
            // story cover JobKey, contract names, and idempotency keys. TTL bounds live in JobContract beside the
            // other identity rules; every public surface that accepts a TTL validates through the same member.
            JobContract.ValidateName(key);
            if (options.IdempotencyTtl is not { } ttl)
            {
                throw new ArgumentException(
                    "An idempotency key requires a TTL; supply both or neither.",
                    nameof(options)
                );
            }

            JobContract.ValidateIdempotencyTtl(ttl);
        }
        else if (options.IdempotencyTtl is not null)
        {
            throw new ArgumentException(
                "An idempotency TTL requires its idempotency key; supply both or neither.",
                nameof(options)
            );
        }
    }
}
