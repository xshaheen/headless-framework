// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Models;
using Headless.UnitOfWork;

namespace Headless.Jobs;

internal sealed class JobSchedulingPolicies
{
    internal static readonly JobSchedulingPolicies Empty = new(new JobOptions(), [], []);
    private readonly JobOptions _defaults;
    private readonly Dictionary<Type, JobOptions> _byRequest;
    private readonly Dictionary<JobFunctionDescriptor, JobOptions> _byDescriptor;
    private readonly Dictionary<string, JobOptions> _byFunction;

    internal JobSchedulingPolicies(
        JobOptions defaults,
        Dictionary<Type, JobOptions> byRequest,
        Dictionary<JobFunctionDescriptor, JobOptions> byDescriptor
    )
    {
        _defaults = Snapshot(defaults);
        _byRequest = byRequest.ToDictionary(pair => pair.Key, pair => Snapshot(pair.Value));
        _byDescriptor = byDescriptor.ToDictionary(pair => pair.Key, pair => Snapshot(pair.Value));
        _byFunction = _byDescriptor.ToDictionary(
            pair => pair.Key.FunctionName,
            pair => pair.Value,
            StringComparer.Ordinal
        );
    }

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
        foreach (var descriptor in _byDescriptor.Keys)
        {
            if (
                registry.CanonicalDescriptors.GetValueOrDefault(descriptor.FunctionName) != descriptor
                || !registry.Descriptors.ContainsKey(descriptor.FunctionName)
            )
            {
                throw new InvalidOperationException(
                    $"Configured job '{descriptor.FunctionName}' is not a canonical generated descriptor in this host."
                );
            }
            if (descriptor.RequestType is { } requestType && _byRequest.ContainsKey(requestType))
            {
                throw new InvalidOperationException(
                    $"Job '{descriptor.FunctionName}' is configured by both descriptor and request type. Choose one identity."
                );
            }
        }
    }

    internal JobOptions Resolve(JobFunctionDescriptor descriptor, JobOptions? call) =>
        _Resolve(descriptor, call, includeHostEnlistment: true);

    internal JobOptions ResolveRecurring(JobFunctionDescriptor descriptor, RecurringJobOptions? call) =>
        _Resolve(
            descriptor,
            new JobOptions
            {
                Retries = call?.Retries,
                RetryIntervals = call?.RetryIntervals,
                OnNodeDeath = call?.OnNodeDeath,
                Enlistment = call?.Enlistment ?? TransactionEnlistment.WhenAvailable,
            },
            // Recurring definitions take the enlistment from the call or the function policy only: the host default
            // describes one-shot deadlines, and a definition is usually created at bootstrap, outside any transaction.
            includeHostEnlistment: false
        );

    private JobOptions _Resolve(JobFunctionDescriptor descriptor, JobOptions? call, bool includeHostEnlistment)
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
            Enlistment = ComposeEnlistment(
                call?.Enlistment,
                function?.Enlistment,
                includeHostEnlistment ? _defaults.Enlistment : null
            ),
            // The idempotency window is per call by contract: it is never inherited from host/function policy
            // (Snapshot rejects it there), so only the call's own key and TTL survive resolution.
            IdempotencyKey = call?.IdempotencyKey,
            IdempotencyTtl = call?.IdempotencyTtl,
        };
        _ValidateOptions(result);
        return result;
    }

    // Strictest-wins composition across call > function policy > host default: TransactionEnlistment.Required from
    // ANY tier wins outright (an explicit Never elsewhere never downgrades it); otherwise an explicit Never from any
    // tier wins over the WhenAvailable default; otherwise WhenAvailable. A null tier (call/function not configured,
    // or the host default excluded for recurring definitions) contributes nothing.
    internal static TransactionEnlistment ComposeEnlistment(params ReadOnlySpan<TransactionEnlistment?> tiers) =>
        _ComposeEnlistmentCore(tiers);

    private static TransactionEnlistment _ComposeEnlistmentCore(ReadOnlySpan<TransactionEnlistment?> tiers)
    {
        var sawNever = false;
        foreach (var tier in tiers)
        {
            if (tier == TransactionEnlistment.Required)
            {
                return TransactionEnlistment.Required;
            }
            if (tier == TransactionEnlistment.Never)
            {
                sawNever = true;
            }
        }
        return sawNever ? TransactionEnlistment.Never : TransactionEnlistment.WhenAvailable;
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
                "Startup job policies accept only retry, node-death, and enlistment settings. Supply invocation metadata on each call.",
                nameof(options)
            );
        }
        return options with { RetryIntervals = options.RetryIntervals?.ToArray() };
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
        if (!Enum.IsDefined(options.Enlistment))
        {
            throw new ArgumentException("The transaction-enlistment value must be a defined value.", nameof(options));
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
