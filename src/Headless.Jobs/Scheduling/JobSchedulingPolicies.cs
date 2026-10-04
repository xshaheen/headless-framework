// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Reliability;

namespace Headless.Jobs;

internal sealed class JobSchedulingPolicies
{
    internal static readonly JobSchedulingPolicies Empty = new(new JobOptions(), [], []);
    private readonly JobOptions _defaults;
    private readonly Dictionary<Type, JobOptions> _byRequest;
    private readonly Dictionary<string, JobOptions> _byFunction;
    private readonly JobFunctionRegistry? _registry;

    internal JobSchedulingPolicies(
        JobOptions defaults,
        Dictionary<Type, JobOptions> byRequest,
        Dictionary<string, JobOptions> byFunction,
        JobFunctionRegistry? registry = null
    )
    {
        _defaults = Snapshot(defaults);
        _byRequest = byRequest.ToDictionary(pair => pair.Key, pair => Snapshot(pair.Value));
        _byFunction = byFunction.ToDictionary(pair => pair.Key, pair => Snapshot(pair.Value), StringComparer.Ordinal);
        _registry = registry;
    }

    /// <summary>
    /// Combines the host's defaults and request-type overrides with the per-job options and failure policies frozen
    /// into the host's registry.
    /// </summary>
    internal JobSchedulingPolicies WithRegistry(JobFunctionRegistry registry) =>
        new(_defaults, _byRequest, registry.OptionsByFunction.ToDictionary(StringComparer.Ordinal), registry);

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

    /// <summary>
    /// Resolves the options a scheduling call stores. A call that supplies a retry count keeps it, with only its own
    /// intervals; otherwise the job's failure policy is flattened into the stored count and intervals, and a call's own
    /// intervals, when supplied, replace the flattened ones.
    /// </summary>
    internal JobOptions Resolve(JobFunctionDescriptor descriptor, JobOptions? call)
    {
        var function =
            _byFunction.GetValueOrDefault(descriptor.FunctionName)
            ?? (descriptor.RequestType is { } requestType ? _byRequest.GetValueOrDefault(requestType) : null);
        int retries;
        int[]? retryIntervals;
        if (call?.Retries is { } callRetries)
        {
            retries = callRetries;
            retryIntervals = call.RetryIntervals?.ToArray();
        }
        else
        {
            (retries, var flattenedIntervals) = _FlattenedFailurePolicy(descriptor.FunctionName);
            retryIntervals = call?.RetryIntervals?.ToArray() ?? flattenedIntervals;
        }

        var result = (call ?? _defaults) with
        {
            Retries = retries,
            RetryIntervals = retryIntervals,
            OnNodeDeath = call?.OnNodeDeath ?? function?.OnNodeDeath ?? _defaults.OnNodeDeath ?? NodeDeathPolicy.Retry,
            // The idempotency window is per call by contract: it is never inherited from host/function policy
            // (Snapshot rejects it there), so only the call's own key and TTL survive resolution.
            IdempotencyKey = call?.IdempotencyKey,
            IdempotencyTtl = call?.IdempotencyTtl,
        };
        _ValidateOptions(result);
        return result;
    }

    /// <summary>
    /// Flattens <paramref name="policy"/> into the retry count and per-retry intervals a job row stores: the immediate
    /// plus the delayed retries, with <c>0</c> seconds for each immediate retry and then each delayed retry's delay
    /// before jitter.
    /// </summary>
    /// <remarks>
    /// The row stores whole seconds, so a delay with a fractional second rounds up rather than down: a retry may wait a
    /// little longer than declared, never shorter, and a sub-second delay never collapses into an immediate retry.
    /// Jitter cannot be stored per row, so the stored delay is the deterministic base delay.
    /// </remarks>
    internal static (int Retries, int[]? RetryIntervals) Flatten(FailurePolicyDefinition policy)
    {
        var retries = policy.ImmediateRetries + policy.DelayedRetries;
        if (retries == 0)
        {
            return (0, null);
        }

        var intervals = new int[retries];
        for (var delayedAttempt = 1; delayedAttempt <= policy.DelayedRetries; delayedAttempt++)
        {
            var delay = policy.GetDelayedRetryBaseDelay(delayedAttempt);

            // The delay is capped at 24 hours, so the rounded-up second count fits an int.
            intervals[policy.ImmediateRetries + delayedAttempt - 1] = (int)(
                (delay.Ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond
            );
        }

        return (retries, intervals);
    }

    internal static JobOptions Snapshot(JobOptions options)
    {
        Argument.IsNotNull(options);
        _ValidateOptions(options);
        if (options.Retries is not null || options.RetryIntervals is not null)
        {
            throw new ArgumentException(
                "Startup job policies do not accept retries or retry intervals: a job's retries come from its "
                    + "FailurePolicy. Declare one with [Job(FailurePolicy = typeof(...))], replace it with "
                    + "Tune(...).FailurePolicy(...), or set the host default with DefaultFailurePolicy(...). A "
                    + "scheduling call's WithRetries and WithRetryIntervals still override the stored retries.",
                nameof(options)
            );
        }

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
                "Startup job policies accept only node-death settings. Supply invocation metadata on each call.",
                nameof(options)
            );
        }

        return options;
    }

    // A hand-built scheduler without a registry takes the policy of an undeclared job on a host without a default:
    // no retries.
    private (int Retries, int[]? RetryIntervals) _FlattenedFailurePolicy(string functionName) =>
        _registry?.GetFlattenedFailurePolicy(functionName) ?? Flatten(FailurePolicyDefinition.None);

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
