// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Reliability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

internal static class JobFunctionRegistryBuilder
{
    public static JobFunctionRegistry Build(
        IReadOnlyCollection<KeyValuePair<string, JobFunctionRegistration>> functions,
        IReadOnlyCollection<KeyValuePair<string, (string, Type)>> requestTypes,
        IReadOnlyCollection<KeyValuePair<string, JobFunctionDescriptor>> descriptors,
        IConfiguration? configuration = null
    )
    {
        var mismatchedNames = descriptors
            .Where(entry => !string.Equals(entry.Key, entry.Value.FunctionName, StringComparison.Ordinal))
            .Select(entry => $"'{entry.Key}' maps descriptor '{entry.Value.FunctionName}'")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (mismatchedNames.Length != 0)
        {
            throw new InvalidOperationException(
                $"Ambiguous Jobs contract registrations: {string.Join(", ", mismatchedNames)}."
            );
        }

        var generatedDescriptorNames = descriptors.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        // Keeps the first entry for a name and never throws: a duplicate name is rejected by the checks and the
        // frozen-map build below, not by this lookup.
        var requestTypesByName = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var requestTypeEntry in requestTypes)
        {
            requestTypesByName.TryAdd(requestTypeEntry.Key, requestTypeEntry.Value.Item2);
        }

        var effectiveDescriptors = descriptors
            .Concat(
                functions
                    .Where(entry => !generatedDescriptorNames.Contains(entry.Key))
                    .Select(entry =>
                    {
                        var requestType = requestTypesByName.GetValueOrDefault(entry.Key);
                        var registration = entry.Value;
                        return new KeyValuePair<string, JobFunctionDescriptor>(
                            entry.Key,
                            new(
                                entry.Key,
                                requestType,
                                registration.CronExpression,
                                registration.Priority,
                                registration.MaxConcurrency
                            )
                        );
                    })
            )
            .ToArray();

        var duplicateFunctionNames = functions
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key)
            .Concat(
                effectiveDescriptors
                    .GroupBy(entry => entry.Key, StringComparer.Ordinal)
                    .Where(group => group.Skip(1).Any())
                    .Select(group => group.Key)
            )
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var duplicateRequestTypes = requestTypes
            .GroupBy(entry => entry.Value.Item2)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key)
            .Concat(
                effectiveDescriptors
                    .Where(entry => entry.Value.RequestType != null)
                    .GroupBy(entry => entry.Value.RequestType!)
                    .Where(group => group.Skip(1).Any())
                    .Select(group => group.Key)
            )
            .Distinct()
            .OrderBy(TypeDisplayName, StringComparer.Ordinal)
            .ToArray();

        var duplicateJobTypes = functions
            .Where(entry => entry.Value.JobType != null)
            .GroupBy(entry => entry.Value.JobType!)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key)
            .OrderBy(TypeDisplayName, StringComparer.Ordinal)
            .ToArray();

        if (duplicateFunctionNames.Length > 0 || duplicateRequestTypes.Length > 0 || duplicateJobTypes.Length > 0)
        {
            var conflicts = duplicateFunctionNames
                .Select(name => $"Function name '{name}' is registered more than once.")
                .Concat(
                    duplicateRequestTypes.Select(type =>
                        $"Request type '{TypeDisplayName(type)}' is mapped more than once."
                    )
                )
                .Concat(
                    duplicateJobTypes.Select(type => $"Job type '{TypeDisplayName(type)}' is mapped more than once.")
                );
            throw new InvalidOperationException(
                $"Job function registration conflicts were found:{Environment.NewLine}{string.Join(Environment.NewLine, conflicts)}"
            );
        }

        // Cron configuration tokens resolve against this host's configuration, so every consumer of the frozen
        // registry sees the effective expression.
        var frozenFunctions = functions
            .ToDictionary(
                entry => entry.Key,
                entry =>
                    configuration is null || !_IsConfigurationToken(entry.Value.CronExpression)
                        ? entry.Value
                        : entry.Value with
                        {
                            CronExpression = _ResolveCronExpression(entry.Value.CronExpression, configuration),
                        },
                StringComparer.Ordinal
            )
            .ToFrozenDictionary(StringComparer.Ordinal);
        var frozenRequestTypes = requestTypes
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)
            .ToFrozenDictionary(StringComparer.Ordinal);
        var frozenDescriptors = effectiveDescriptors
            .ToDictionary(
                entry => entry.Key,
                entry => _ResolveCronExpression(entry.Value, configuration),
                StringComparer.Ordinal
            )
            .ToFrozenDictionary(StringComparer.Ordinal);

        return new JobFunctionRegistry(
            frozenFunctions,
            frozenRequestTypes,
            frozenDescriptors,
            IndexDescriptorsByRequestType(frozenDescriptors),
            IndexDescriptorsByJobType(frozenFunctions, frozenDescriptors)
        );
    }

    internal static FrozenDictionary<Type, JobFunctionDescriptor> IndexDescriptorsByRequestType(
        IReadOnlyDictionary<string, JobFunctionDescriptor> descriptors
    ) =>
        descriptors
            .Values.Where(descriptor => descriptor.RequestType != null)
            .ToFrozenDictionary(descriptor => descriptor.RequestType!, descriptor => descriptor);

    internal static FrozenDictionary<Type, JobFunctionDescriptor> IndexDescriptorsByJobType(
        IReadOnlyDictionary<string, JobFunctionRegistration> functions,
        IReadOnlyDictionary<string, JobFunctionDescriptor> descriptors
    ) =>
        functions
            .Where(entry => entry.Value.JobType != null && descriptors.ContainsKey(entry.Key))
            .ToFrozenDictionary(entry => entry.Value.JobType!, entry => descriptors[entry.Key]);

    private static JobFunctionDescriptor _ResolveCronExpression(
        JobFunctionDescriptor descriptor,
        IConfiguration? configuration
    )
    {
        var cronExpression =
            configuration == null
                ? descriptor.CronExpression
                : _ResolveCronExpression(descriptor.CronExpression, configuration);

        return string.Equals(cronExpression, descriptor.CronExpression, StringComparison.Ordinal)
            ? descriptor
            : descriptor.With(cronExpression: cronExpression);
    }

    private static string _ResolveCronExpression(string cronExpression, IConfiguration configuration)
    {
        if (!_IsConfigurationToken(cronExpression))
        {
            return cronExpression;
        }

        var mappedCronExpression = configuration[cronExpression.Trim('%')];
        return string.IsNullOrEmpty(mappedCronExpression) ? cronExpression : mappedCronExpression;
    }

    private static bool _IsConfigurationToken(string cronExpression)
    {
        return cronExpression.StartsWith('%');
    }

    /// <summary>How a registration conflict names a type.</summary>
    internal static string TypeDisplayName(Type type)
    {
        return type.FullName ?? type.Name;
    }
}

/// <summary>
/// One host's immutable job registry. Everything that dispatches, schedules, or displays jobs reads this instance, never
/// process-wide state, so two hosts in one process can register different modules, tuning, and filters.
/// </summary>
internal sealed record JobFunctionRegistry(
    FrozenDictionary<string, JobFunctionRegistration> Functions,
    FrozenDictionary<string, (string, Type)> RequestTypes,
    FrozenDictionary<string, JobFunctionDescriptor> Descriptors,
    FrozenDictionary<Type, JobFunctionDescriptor> DescriptorsByRequestType,
    FrozenDictionary<Type, JobFunctionDescriptor> DescriptorsByJobType
)
{
    /// <summary>The host's ordered schedule and execute middleware.</summary>
    public JobMiddlewarePipeline Middleware { get; init; } = JobMiddlewarePipeline.Empty;

    /// <summary>
    /// Which registered jobs this host claims and executes. Every registered job stays schedulable regardless.
    /// </summary>
    public JobsRunFilter RunFilter { get; init; } = JobsRunFilter.All;

    /// <summary>Node-death overrides tuned per job identity.</summary>
    public FrozenDictionary<string, JobOptions> OptionsByFunction { get; init; } =
        FrozenDictionary<string, JobOptions>.Empty;

    /// <summary>
    /// The resolved failure policy of every registered job: the tuned policy, else the declared one, else
    /// <see cref="DefaultFailurePolicy"/>, with <c>Headless:Jobs:Jobs:{identity}:FailurePolicy</c> configuration
    /// applied last.
    /// </summary>
    public FrozenDictionary<string, FailurePolicyDefinition> FailurePolicies
    {
        get;
        init
        {
            field = value;
            // Policies are frozen per host, so each is flattened once here instead of on every scheduling call.
            _flattenedFailurePolicies = value.ToFrozenDictionary(
                pair => pair.Key,
                pair => JobSchedulingPolicies.Flatten(pair.Value),
                value.Comparer
            );
        }
    } = FrozenDictionary<string, FailurePolicyDefinition>.Empty;

    /// <summary>The host's default failure policy; without one a failed run does not retry.</summary>
    public FailurePolicyDefinition DefaultFailurePolicy
    {
        get;
        init
        {
            field = value;
            _flattenedDefaultFailurePolicy = JobSchedulingPolicies.Flatten(value);
        }
    } = FailurePolicyDefinition.None;

    private FrozenDictionary<string, (int Retries, int[]? RetryIntervals)> _flattenedFailurePolicies = FrozenDictionary<
        string,
        (int Retries, int[]? RetryIntervals)
    >.Empty;

    private (int Retries, int[]? RetryIntervals) _flattenedDefaultFailurePolicy = JobSchedulingPolicies.Flatten(
        FailurePolicyDefinition.None
    );

    /// <summary>
    /// Returns the failure policy that classifies and paces the retries of <paramref name="functionName"/>. A function
    /// this host does not register, such as a row another deployment scheduled, takes the host default.
    /// </summary>
    public FailurePolicyDefinition GetFailurePolicy(string functionName) =>
        FailurePolicies.GetValueOrDefault(functionName) ?? DefaultFailurePolicy;

    /// <summary>
    /// Returns <see cref="GetFailurePolicy"/> of <paramref name="functionName"/> flattened by
    /// <see cref="JobSchedulingPolicies.Flatten"/>. The intervals are a fresh array on every call, because callers
    /// store them on a job row they may still change.
    /// </summary>
    public (int Retries, int[]? RetryIntervals) GetFlattenedFailurePolicy(string functionName)
    {
        var (retries, retryIntervals) = _flattenedFailurePolicies.TryGetValue(functionName, out var flattened)
            ? flattened
            : _flattenedDefaultFailurePolicy;

        return (retries, retryIntervals?.ToArray());
    }
}
