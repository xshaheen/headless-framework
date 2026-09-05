// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Models;

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

    internal JobOptions Resolve(JobFunctionDescriptor descriptor, JobOptions? call)
    {
        var function =
            _byFunction.GetValueOrDefault(descriptor.FunctionName)
            ?? (descriptor.RequestType is { } requestType ? _byRequest.GetValueOrDefault(requestType) : null);
        var result = (call ?? new JobOptions()) with
        {
            Retries = call?.Retries ?? function?.Retries ?? _defaults.Retries ?? 0,
            RetryIntervals = (call?.RetryIntervals ?? function?.RetryIntervals ?? _defaults.RetryIntervals)?.ToArray(),
            OnNodeDeath =
                call?.OnNodeDeath ?? function?.OnNodeDeath ?? _defaults.OnNodeDeath ?? Enums.NodeDeathPolicy.Retry,
            RequireAtomicEnlistment =
                (call?.RequireAtomicEnlistment ?? false)
                || (function?.RequireAtomicEnlistment ?? false)
                || _defaults.RequireAtomicEnlistment,
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
        )
        {
            throw new ArgumentException(
                "Startup job policies accept only retry, node-death, and atomic-enlistment settings. Supply invocation metadata on each call.",
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
    }
}
