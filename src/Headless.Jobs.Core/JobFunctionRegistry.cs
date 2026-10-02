// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
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

    /// <summary>Retry and node-death overrides tuned per job identity.</summary>
    public FrozenDictionary<string, JobOptions> OptionsByFunction { get; init; } =
        FrozenDictionary<string, JobOptions>.Empty;
}

/// <summary>
/// Helper for deserializing a typed argument payload for a job run.
/// </summary>
public static class JobsRequestProvider
{
    /// <summary>
    /// Reads and deserializes the typed request payload stored for the current job execution. Returns
    /// <see langword="default"/> only when no payload is stored for the job.
    /// </summary>
    /// <typeparam name="T">The expected request type.</typeparam>
    /// <param name="context">The current job execution context.</param>
    /// <param name="cancellationToken">Token that can abort the persistence read.</param>
    /// <returns>The deserialized request, or <see langword="default"/> when the job stored no request.</returns>
    /// <remarks>
    /// A read or deserialization failure propagates and therefore fails the attempt, which the retry pipeline
    /// classifies like any other job failure. It is never converted into a handler invocation with a default
    /// payload: doing so would either surface the infrastructure fault as a misleading
    /// <see cref="NullReferenceException"/> from consumer code or record a job as succeeded although its payload
    /// was never processed.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    public static async Task<T?> GetRequestAsync<T>(JobContext context, CancellationToken cancellationToken)
    {
        var internalJobsManager = context.ServiceScope.ServiceProvider.GetRequiredService<IInternalJobManager>();

        try
        {
            return await internalJobsManager
                .GetRequestAsync<T>(context.Id, context.Type, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Observability only — the failure is rethrown. This records the request type, which the generic job
            // failure record does not carry. Resolved with GetService so a missing instrumentation registration
            // cannot replace the real payload failure with a DI exception.
            context
                .ServiceScope.ServiceProvider.GetService<IJobsInstrumentation>()
                ?.LogRequestDeserializationFailure(
                    typeof(T).FullName!,
                    context.FunctionName,
                    context.Id,
                    context.Type,
                    e
                );

            throw;
        }
    }
}
