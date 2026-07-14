// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Checks;
using Headless.Jobs.Base;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

/// <summary>
/// Global registry of job function delegates and their associated request types. Populated at application
/// startup by the source-generated <c>ModuleInitializer</c> and frozen into read-optimized dictionaries
/// via <see cref="Build"/> before the first job is dispatched.
/// </summary>
/// <remarks>
/// The registration flow is callback-based: each source-generated assembly calls
/// <c>RegisterFunctions</c>, <c>RegisterRequestType</c>, and <c>RegisterDescriptors</c> to enqueue its entries, then
/// <see cref="Build"/> executes all callbacks in order and freezes the results. After <c>Build</c> is
/// called, no further registrations take effect.
/// </remarks>
public static class JobFunctionProvider
{
    private static Action<List<KeyValuePair<string, (string, Type)>>>? _requestTypeRegistrations;
    private static Action<List<KeyValuePair<string, JobFunctionRegistration>>>? _functionRegistrations;
    private static Action<List<KeyValuePair<string, JobFunctionDescriptor>>>? _descriptorRegistrations;
    private static IConfiguration? _configuration;

    /// <summary>
    /// Frozen lookup of all registered job functions, keyed by function name. Each entry is a
    /// <see cref="JobFunctionRegistration"/> carrying the cron expression (empty string for time jobs),
    /// scheduling priority, execution delegate, and maximum concurrency limit. Available after
    /// <see cref="Build"/> is called.
    /// </summary>
    public static FrozenDictionary<string, JobFunctionRegistration> JobFunctions { get; private set; } =
        FrozenDictionary<string, JobFunctionRegistration>.Empty;

    /// <summary>
    /// Frozen lookup of request type metadata, keyed by function name. Each entry carries the full type
    /// name and <see cref="Type"/> object for functions that accept a typed request payload. Available
    /// after <see cref="Build"/> is called.
    /// </summary>
    public static FrozenDictionary<string, (string, Type)> JobFunctionRequestTypes { get; private set; } =
        FrozenDictionary<string, (string, Type)>.Empty;

    /// <summary>
    /// Frozen lookup of generated job-function descriptors, keyed by the durable function name.
    /// </summary>
    public static FrozenDictionary<string, JobFunctionDescriptor> JobFunctionDescriptors { get; private set; } =
        FrozenDictionary<string, JobFunctionDescriptor>.Empty;

    /// <summary>
    /// Frozen inverse lookup of generated job-function descriptors, keyed by the typed request payload.
    /// Requestless functions are intentionally absent.
    /// </summary>
    public static FrozenDictionary<Type, JobFunctionDescriptor> JobFunctionDescriptorsByRequestType
    {
        get;
        private set;
    } = FrozenDictionary<Type, JobFunctionDescriptor>.Empty;

    /// <summary>
    /// Registers job functions during application startup by adding to the callback chain.
    /// This method should only be called during application startup before Build() is called.
    /// </summary>
    /// <param name="functions">The functions to register. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when functions parameter is null.</exception>
    public static void RegisterFunctions(IDictionary<string, JobFunctionRegistration> functions)
    {
        Argument.IsNotNull(functions);

        if (functions.Count == 0)
        {
            return;
        }

        _functionRegistrations += entries => entries.AddRange(functions);
    }

    /// <summary>
    /// Registers job functions with capacity hint during application startup by adding to the callback chain.
    /// This method should only be called during application startup before Build() is called.
    /// </summary>
    /// <param name="functions">The functions to register. Cannot be null.</param>
    /// <param name="_">The total expected capacity (ignored - capacity calculated automatically).</param>
    /// <exception cref="ArgumentNullException">Thrown when functions parameter is null.</exception>
    public static void RegisterFunctions(IDictionary<string, JobFunctionRegistration> functions, int _)
    {
        // For callback approach, capacity is calculated automatically in Build()
        RegisterFunctions(functions);
    }

    /// <summary>
    /// Registers request types during application startup by adding to the callback chain.
    /// This method should only be called during application startup before Build() is called.
    /// </summary>
    /// <param name="requestTypes">The request types to register. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when requestTypes parameter is null.</exception>
    public static void RegisterRequestType(IDictionary<string, (string, Type)> requestTypes)
    {
        Argument.IsNotNull(requestTypes);

        if (requestTypes.Count == 0)
        {
            return;
        }

        _requestTypeRegistrations += entries => entries.AddRange(requestTypes);
    }

    /// <summary>
    /// Registers request types with capacity hint during application startup by adding to the callback chain.
    /// This method should only be called during application startup before Build() is called.
    /// </summary>
    /// <param name="requestTypes">The request types to register. Cannot be null.</param>
    /// <param name="_">The total expected capacity (ignored - capacity calculated automatically).</param>
    /// <exception cref="ArgumentNullException">Thrown when requestTypes parameter is null.</exception>
    public static void RegisterRequestType(IDictionary<string, (string, Type)> requestTypes, int _)
    {
        // For callback approach, capacity is calculated automatically in Build()
        RegisterRequestType(requestTypes);
    }

    /// <summary>
    /// Registers generated descriptors during application startup by adding the assembly batch to the callback chain.
    /// </summary>
    /// <param name="descriptors">The descriptors to register. Cannot be null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="descriptors"/> is <see langword="null"/>.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterDescriptors(IDictionary<string, JobFunctionDescriptor> descriptors)
    {
        Argument.IsNotNull(descriptors);

        if (descriptors.Count == 0)
        {
            return;
        }

        _descriptorRegistrations += entries => entries.AddRange(descriptors);
    }

    /// <summary>
    /// Registers generated descriptors with a source-generated capacity hint.
    /// </summary>
    /// <param name="descriptors">The descriptors to register. Cannot be null.</param>
    /// <param name="_">The total expected capacity; retained for generated-call compatibility.</param>
    /// <exception cref="ArgumentNullException"><paramref name="descriptors"/> is <see langword="null"/>.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterDescriptors(IDictionary<string, JobFunctionDescriptor> descriptors, int _)
    {
        RegisterDescriptors(descriptors);
    }

    /// <summary>
    /// Updates cron expressions for registered functions by adding to the callback chain.
    /// This method should only be called during application startup before Build() is called.
    /// </summary>
    /// <param name="configuration">IConfiguration to update based on path</param>
    /// <exception cref="ArgumentNullException">Thrown when cronUpdates parameter is null.</exception>
    internal static void UpdateCronExpressionsFromIConfiguration(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Freezes the registered functions, request types, and descriptors into read-optimized
    /// <see cref="FrozenDictionary{TKey,TValue}"/> instances. Must be called exactly once after all
    /// assemblies have had their <c>ModuleInitializer</c> executed and before the first job is dispatched.
    /// After this call, the function, request-type, and descriptor indexes are populated and subsequent
    /// <c>Register*</c> calls have no effect.
    /// </summary>
    public static void Build()
    {
        var functions = new List<KeyValuePair<string, JobFunctionRegistration>>();
        var requestTypes = new List<KeyValuePair<string, (string, Type)>>();
        var descriptors = new List<KeyValuePair<string, JobFunctionDescriptor>>();
        _functionRegistrations?.Invoke(functions);
        _requestTypeRegistrations?.Invoke(requestTypes);
        _descriptorRegistrations?.Invoke(descriptors);

        var registry = JobFunctionRegistryBuilder.Build(functions, requestTypes, descriptors, _configuration);
        JobFunctions = registry.Functions;
        JobFunctionRequestTypes = registry.RequestTypes;
        JobFunctionDescriptors = registry.Descriptors;
        JobFunctionDescriptorsByRequestType = registry.DescriptorsByRequestType;

        _functionRegistrations = null;
        _requestTypeRegistrations = null;
        _descriptorRegistrations = null;
        _configuration = null;
    }
}

internal static class JobFunctionRegistryBuilder
{
    public static JobFunctionRegistry Build(
        IReadOnlyCollection<KeyValuePair<string, JobFunctionRegistration>> functions,
        IReadOnlyCollection<KeyValuePair<string, (string, Type)>> requestTypes,
        IReadOnlyCollection<KeyValuePair<string, JobFunctionDescriptor>> descriptors,
        IConfiguration? configuration = null
    )
    {
        var generatedDescriptorNames = descriptors.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        var effectiveDescriptors = descriptors
            .Concat(
                functions
                    .Where(entry => !generatedDescriptorNames.Contains(entry.Key))
                    .Select(entry =>
                    {
                        var requestType = requestTypes
                            .FirstOrDefault(requestTypeEntry =>
                                string.Equals(requestTypeEntry.Key, entry.Key, StringComparison.Ordinal)
                            )
                            .Value.Item2;
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
            .OrderBy(_TypeDisplayName, StringComparer.Ordinal)
            .ToArray();

        if (duplicateFunctionNames.Length > 0 || duplicateRequestTypes.Length > 0)
        {
            var conflicts = duplicateFunctionNames
                .Select(name => $"Function name '{name}' is registered more than once.")
                .Concat(
                    duplicateRequestTypes.Select(type =>
                        $"Request type '{_TypeDisplayName(type)}' is mapped more than once."
                    )
                );
            throw new InvalidOperationException(
                $"Job function registration conflicts were found:{Environment.NewLine}{string.Join(Environment.NewLine, conflicts)}"
            );
        }

        var functionDictionary = functions.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal
        );
        var requestTypeDictionary = requestTypes.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal
        );
        var descriptorDictionary = effectiveDescriptors.ToDictionary(
            entry => entry.Key,
            entry => _ResolveCronExpression(entry.Value, configuration),
            StringComparer.Ordinal
        );

        if (configuration != null)
        {
            foreach (var (name, registration) in functionDictionary)
            {
                functionDictionary[name] = registration with
                {
                    CronExpression = _ResolveCronExpression(registration.CronExpression, configuration),
                };
            }
        }

        var descriptorsByRequestType = descriptorDictionary
            .Values.Where(descriptor => descriptor.RequestType != null)
            .ToDictionary(descriptor => descriptor.RequestType!, descriptor => descriptor);

        return new(
            functionDictionary.ToFrozenDictionary(StringComparer.Ordinal),
            requestTypeDictionary.ToFrozenDictionary(StringComparer.Ordinal),
            descriptorDictionary.ToFrozenDictionary(StringComparer.Ordinal),
            descriptorsByRequestType.ToFrozenDictionary()
        );
    }

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
            : new(
                descriptor.FunctionName,
                descriptor.RequestType,
                cronExpression,
                descriptor.Priority,
                descriptor.MaxConcurrency
            );
    }

    private static string _ResolveCronExpression(string cronExpression, IConfiguration configuration)
    {
        if (!cronExpression.StartsWith('%'))
        {
            return cronExpression;
        }

        var mappedCronExpression = configuration[cronExpression.Trim('%')];
        return string.IsNullOrEmpty(mappedCronExpression) ? cronExpression : mappedCronExpression;
    }

    private static string _TypeDisplayName(Type type) => type.FullName ?? type.Name;
}

internal sealed record JobFunctionRegistry(
    FrozenDictionary<string, JobFunctionRegistration> Functions,
    FrozenDictionary<string, (string, Type)> RequestTypes,
    FrozenDictionary<string, JobFunctionDescriptor> Descriptors,
    FrozenDictionary<Type, JobFunctionDescriptor> DescriptorsByRequestType
);

/// <summary>
/// Helper for deserializing a typed request payload from within a job function body.
/// </summary>
public static class JobsRequestProvider
{
    /// <summary>
    /// Deserializes the typed request payload stored for the current job execution. Returns
    /// <see langword="default"/> when deserialization fails, after logging the error.
    /// </summary>
    /// <typeparam name="T">The expected request type.</typeparam>
    /// <param name="context">The current job execution context.</param>
    /// <param name="cancellationToken">Token that can abort the persistence read.</param>
    /// <returns>The deserialized request, or <see langword="default"/> on failure.</returns>
    public static async Task<T?> GetRequestAsync<T>(JobFunctionContext context, CancellationToken cancellationToken)
    {
        try
        {
            var internalJobsManager = context.ServiceScope.ServiceProvider.GetRequiredService<IInternalJobManager>();
            return await internalJobsManager
                .GetRequestAsync<T>(context.Id, context.Type, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            var logger = context.ServiceScope.ServiceProvider.GetRequiredService<IJobsInstrumentation>();

            logger.LogRequestDeserializationFailure(
                typeof(T).FullName!,
                context.FunctionName,
                context.Id,
                context.Type,
                e
            );
        }

        return default;
    }
}
