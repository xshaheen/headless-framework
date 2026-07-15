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
/// Jobs discovery marks collection complete before <see cref="Build"/> executes all callbacks in order and freezes
/// the results. Registrations attempted after discovery completes fail deterministically.
/// </remarks>
public static class JobFunctionProvider
{
    private const string _DiscoveryIncompleteMessage =
        "Jobs discovery must complete before JobFunctionProvider.Build() can freeze the catalog.";
    private const string _DiscoveryCompleteMessage = "Jobs generated registration is closed after discovery completes.";
    private const string _FrozenMessage = "Jobs generated registration is frozen after JobFunctionProvider.Build().";
    private static readonly Lock _Sync = new();
    private static Action<List<KeyValuePair<string, (string, Type)>>>? _requestTypeRegistrations;
    private static Action<List<KeyValuePair<string, JobFunctionRegistration>>>? _functionRegistrations;
    private static Action<List<KeyValuePair<string, JobFunctionDescriptor>>>? _descriptorRegistrations;
    private static TaskCompletionSource<object?> _discoveryCompletion = _CreateDiscoveryCompletion();
    private static int _activeDiscoveries;

    [ThreadStatic]
    private static int _discoveryDepth;
    private static readonly JobFunctionRegistry _EmptyRegistry = new(
        FrozenDictionary<string, JobFunctionRegistration>.Empty,
        FrozenDictionary<string, (string, Type)>.Empty,
        FrozenDictionary<string, JobFunctionDescriptor>.Empty,
        FrozenDictionary<string, JobFunctionDescriptor>.Empty,
        FrozenDictionary<Type, JobFunctionDescriptor>.Empty
    );
    private static JobFunctionRegistry _canonicalRegistry = _EmptyRegistry;
    private static RegistryState _state;

    /// <summary>
    /// Frozen lookup of all registered job functions, keyed by function name. Each entry is a
    /// <see cref="JobFunctionRegistration"/> carrying the cron expression (empty string for time jobs),
    /// scheduling priority, execution delegate, and maximum concurrency limit. Available after
    /// <see cref="Build"/> is called.
    /// </summary>
    public static FrozenDictionary<string, JobFunctionRegistration> JobFunctions => _canonicalRegistry.Functions;

    /// <summary>
    /// Frozen lookup of request type metadata, keyed by function name. Each entry carries the full type
    /// name and <see cref="Type"/> object for functions that accept a typed request payload. Available
    /// after <see cref="Build"/> is called.
    /// </summary>
    public static FrozenDictionary<string, (string, Type)> JobFunctionRequestTypes => _canonicalRegistry.RequestTypes;

    /// <summary>
    /// Frozen lookup of generated job-function descriptors, keyed by the durable function name.
    /// </summary>
    public static FrozenDictionary<string, JobFunctionDescriptor> JobFunctionDescriptors =>
        _canonicalRegistry.Descriptors;

    /// <summary>
    /// Frozen inverse lookup of generated job-function descriptors, keyed by the typed request payload.
    /// Requestless functions are intentionally absent.
    /// </summary>
    public static FrozenDictionary<Type, JobFunctionDescriptor> JobFunctionDescriptorsByRequestType =>
        _canonicalRegistry.DescriptorsByRequestType;

    /// <summary>
    /// Registers job functions during application startup by adding to the callback chain.
    /// This method should only be called during application startup before Jobs discovery completes.
    /// </summary>
    /// <param name="functions">The functions to register. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when functions parameter is null.</exception>
    /// <exception cref="InvalidOperationException">Jobs discovery has completed or the catalog is frozen.</exception>
    public static void RegisterFunctions(IDictionary<string, JobFunctionRegistration> functions) =>
        _Register(functions, ref _functionRegistrations);

    /// <summary>
    /// Registers job functions with capacity hint during application startup by adding to the callback chain.
    /// This method should only be called during application startup before Jobs discovery completes.
    /// </summary>
    /// <param name="functions">The functions to register. Cannot be null.</param>
    /// <param name="_">The total expected capacity (ignored - capacity calculated automatically).</param>
    /// <exception cref="ArgumentNullException">Thrown when functions parameter is null.</exception>
    /// <exception cref="InvalidOperationException">Jobs discovery has completed or the catalog is frozen.</exception>
    public static void RegisterFunctions(IDictionary<string, JobFunctionRegistration> functions, int _)
    {
        // For callback approach, capacity is calculated automatically in Build()
        RegisterFunctions(functions);
    }

    /// <summary>
    /// Registers request types during application startup by adding to the callback chain.
    /// This method should only be called during application startup before Jobs discovery completes.
    /// </summary>
    /// <param name="requestTypes">The request types to register. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when requestTypes parameter is null.</exception>
    /// <exception cref="InvalidOperationException">Jobs discovery has completed or the catalog is frozen.</exception>
    public static void RegisterRequestType(IDictionary<string, (string, Type)> requestTypes) =>
        _Register(requestTypes, ref _requestTypeRegistrations);

    /// <summary>
    /// Registers request types with capacity hint during application startup by adding to the callback chain.
    /// This method should only be called during application startup before Jobs discovery completes.
    /// </summary>
    /// <param name="requestTypes">The request types to register. Cannot be null.</param>
    /// <param name="_">The total expected capacity (ignored - capacity calculated automatically).</param>
    /// <exception cref="ArgumentNullException">Thrown when requestTypes parameter is null.</exception>
    /// <exception cref="InvalidOperationException">Jobs discovery has completed or the catalog is frozen.</exception>
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
    /// <exception cref="InvalidOperationException">Jobs discovery has completed or the catalog is frozen.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterDescriptors(IDictionary<string, JobFunctionDescriptor> descriptors) =>
        _Register(descriptors, ref _descriptorRegistrations);

    /// <summary>
    /// Registers generated descriptors with a source-generated capacity hint.
    /// </summary>
    /// <param name="descriptors">The descriptors to register. Cannot be null.</param>
    /// <param name="_">The total expected capacity; retained for generated-call compatibility.</param>
    /// <exception cref="ArgumentNullException"><paramref name="descriptors"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Jobs discovery has completed or the catalog is frozen.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterDescriptors(IDictionary<string, JobFunctionDescriptor> descriptors, int _)
    {
        RegisterDescriptors(descriptors);
    }

    /// <summary>Closes generated registration after configured discovery assemblies have loaded.</summary>
    internal static void MarkDiscoveryComplete()
    {
        lock (_Sync)
        {
            if (_state == RegistryState.Collecting)
            {
                _state = RegistryState.DiscoveryComplete;
            }
        }
    }

    internal static DiscoveryParticipation BeginDiscovery()
    {
        if (_discoveryDepth > 0)
        {
            _discoveryDepth++;
            return DiscoveryParticipation.Nested;
        }

        lock (_Sync)
        {
            if (_state != RegistryState.Collecting)
            {
                return DiscoveryParticipation.ExistingCatalog;
            }

            _activeDiscoveries++;
            _discoveryDepth = 1;
            return DiscoveryParticipation.Participant;
        }
    }

    internal static void CompleteDiscovery(DiscoveryParticipation participation)
    {
        if (participation == DiscoveryParticipation.Nested)
        {
            _discoveryDepth--;
            return;
        }

        if (participation == DiscoveryParticipation.ExistingCatalog)
        {
            Build();
            return;
        }

        _discoveryDepth = 0;
        Task? pendingDiscovery = null;
        var shouldFreeze = false;
        lock (_Sync)
        {
            _activeDiscoveries--;
            if (_activeDiscoveries == 0)
            {
                _state = RegistryState.DiscoveryComplete;
                shouldFreeze = true;
            }
            else
            {
                pendingDiscovery = _discoveryCompletion.Task;
            }
        }

        if (!shouldFreeze)
        {
            pendingDiscovery!.GetAwaiter().GetResult();
            return;
        }

        try
        {
            Build();
            _discoveryCompletion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            _discoveryCompletion.TrySetException(exception);
            throw;
        }
    }

    internal static void AbandonDiscovery(DiscoveryParticipation participation, Exception exception)
    {
        if (participation == DiscoveryParticipation.Nested)
        {
            _discoveryDepth--;
            return;
        }

        if (participation == DiscoveryParticipation.ExistingCatalog)
        {
            return;
        }

        _discoveryDepth = 0;
        TaskCompletionSource<object?>? abandonedDiscovery = null;
        lock (_Sync)
        {
            _activeDiscoveries--;
            if (_activeDiscoveries == 0)
            {
                abandonedDiscovery = _discoveryCompletion;
                _discoveryCompletion = _CreateDiscoveryCompletion();
            }
        }

        abandonedDiscovery?.TrySetException(exception);
    }

    internal static void RegisterMiddleware(Action registration)
    {
        lock (_Sync)
        {
            _ThrowIfRegistrationClosed();
            registration();
        }
    }

    internal static JobFunctionRegistry CreateHostRegistry(IConfiguration? configuration)
    {
        JobFunctionRegistry canonicalRegistry;
        lock (_Sync)
        {
            if (_state != RegistryState.Frozen)
            {
                throw new InvalidOperationException(_DiscoveryIncompleteMessage);
            }

            canonicalRegistry = _canonicalRegistry;
        }

        return JobFunctionRegistryBuilder.Project(
            canonicalRegistry.Functions,
            canonicalRegistry.RequestTypes,
            canonicalRegistry.Descriptors,
            canonicalRegistry.CanonicalDescriptors,
            canonicalRegistry.DescriptorsByRequestType,
            configuration
        );
    }

    /// <summary>
    /// Freezes the registered functions, request types, and descriptors into read-optimized
    /// <see cref="FrozenDictionary{TKey,TValue}"/> instances after configured discovery completes. Repeated calls
    /// return without replacing the published dictionaries. Registrations attempted after discovery completes fail.
    /// </summary>
    /// <exception cref="InvalidOperationException">Jobs discovery has not completed.</exception>
    public static void Build()
    {
        lock (_Sync)
        {
            if (_state == RegistryState.Collecting)
            {
                throw new InvalidOperationException(_DiscoveryIncompleteMessage);
            }

            if (_state == RegistryState.Frozen)
            {
                return;
            }

            var functions = new List<KeyValuePair<string, JobFunctionRegistration>>();
            var requestTypes = new List<KeyValuePair<string, (string, Type)>>();
            var descriptors = new List<KeyValuePair<string, JobFunctionDescriptor>>();
            _functionRegistrations?.Invoke(functions);
            _requestTypeRegistrations?.Invoke(requestTypes);
            _descriptorRegistrations?.Invoke(descriptors);

            var registry = JobFunctionRegistryBuilder.Build(functions, requestTypes, descriptors);
            JobMiddlewareRegistry.FreezeUnderProviderLock();
            _canonicalRegistry = registry;

            _functionRegistrations = null;
            _requestTypeRegistrations = null;
            _descriptorRegistrations = null;
            _state = RegistryState.Frozen;
        }
    }

    internal static void ResetForTests(bool discoveryComplete = false)
    {
        lock (_Sync)
        {
            _requestTypeRegistrations = null;
            _functionRegistrations = null;
            _descriptorRegistrations = null;
            _activeDiscoveries = 0;
            _discoveryCompletion = _CreateDiscoveryCompletion();
            _state = discoveryComplete ? RegistryState.DiscoveryComplete : RegistryState.Collecting;
            _canonicalRegistry = _EmptyRegistry;
            JobMiddlewareRegistry.ResetUnderProviderLock();
        }
    }

    private static void _Register<T>(
        IDictionary<string, T> entries,
        ref Action<List<KeyValuePair<string, T>>>? registrations
    )
    {
        Argument.IsNotNull(entries);
        lock (_Sync)
        {
            _ThrowIfRegistrationClosed();
            if (entries.Count != 0)
            {
                registrations += values => values.AddRange(entries);
            }
        }
    }

    private static void _ThrowIfRegistrationClosed()
    {
        if (_state == RegistryState.DiscoveryComplete)
        {
            throw new InvalidOperationException(_DiscoveryCompleteMessage);
        }

        if (_state == RegistryState.Frozen)
        {
            throw new InvalidOperationException(_FrozenMessage);
        }
    }

    private static TaskCompletionSource<object?> _CreateDiscoveryCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private enum RegistryState
    {
        Collecting,
        DiscoveryComplete,
        Frozen,
    }

    internal enum DiscoveryParticipation
    {
        ExistingCatalog,
        Participant,
        Nested,
    }
}

internal static class JobFunctionRegistryBuilder
{
    public static JobFunctionRegistry Project(
        FrozenDictionary<string, JobFunctionRegistration> functions,
        FrozenDictionary<string, (string, Type)> requestTypes,
        FrozenDictionary<string, JobFunctionDescriptor> descriptors,
        FrozenDictionary<string, JobFunctionDescriptor> canonicalDescriptors,
        FrozenDictionary<Type, JobFunctionDescriptor> descriptorsByRequestType,
        IConfiguration? configuration
    )
    {
        if (configuration is null)
        {
            return new(functions, requestTypes, descriptors, canonicalDescriptors, descriptorsByRequestType);
        }

        var projectedFunctions = functions.Values.Any(registration =>
            _IsConfigurationToken(registration.CronExpression)
        )
            ? functions
                .ToDictionary(
                    entry => entry.Key,
                    entry =>
                        entry.Value with
                        {
                            CronExpression = _ResolveCronExpression(entry.Value.CronExpression, configuration),
                        },
                    StringComparer.Ordinal
                )
                .ToFrozenDictionary(StringComparer.Ordinal)
            : functions;
        var projectedDescriptors = descriptors.Values.Any(descriptor =>
            _IsConfigurationToken(descriptor.CronExpression)
        )
            ? descriptors
                .ToDictionary(
                    entry => entry.Key,
                    entry => _ResolveCronExpression(entry.Value, configuration),
                    StringComparer.Ordinal
                )
                .ToFrozenDictionary(StringComparer.Ordinal)
            : descriptors;

        return new(
            projectedFunctions,
            requestTypes,
            projectedDescriptors,
            canonicalDescriptors,
            ReferenceEquals(projectedDescriptors, descriptors)
                ? descriptorsByRequestType
                : _IndexDescriptorsByRequestType(projectedDescriptors)
        );
    }

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
            entry => entry.Value,
            StringComparer.Ordinal
        );
        var frozenFunctions = functionDictionary.ToFrozenDictionary(StringComparer.Ordinal);
        var frozenRequestTypes = requestTypeDictionary.ToFrozenDictionary(StringComparer.Ordinal);
        var frozenDescriptors = descriptorDictionary.ToFrozenDictionary(StringComparer.Ordinal);
        var descriptorsByRequestType = _IndexDescriptorsByRequestType(descriptorDictionary);
        var canonicalRegistry = new JobFunctionRegistry(
            frozenFunctions,
            frozenRequestTypes,
            frozenDescriptors,
            frozenDescriptors,
            descriptorsByRequestType
        );

        return configuration is null
            ? canonicalRegistry
            : Project(
                frozenFunctions,
                frozenRequestTypes,
                frozenDescriptors,
                frozenDescriptors,
                descriptorsByRequestType,
                configuration
            );
    }

    private static FrozenDictionary<Type, JobFunctionDescriptor> _IndexDescriptorsByRequestType(
        IReadOnlyDictionary<string, JobFunctionDescriptor> descriptors
    ) =>
        descriptors
            .Values.Where(descriptor => descriptor.RequestType != null)
            .ToFrozenDictionary(descriptor => descriptor.RequestType!, descriptor => descriptor);

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

    private static string _TypeDisplayName(Type type)
    {
        return type.FullName ?? type.Name;
    }
}

internal sealed record JobFunctionRegistry(
    FrozenDictionary<string, JobFunctionRegistration> Functions,
    FrozenDictionary<string, (string, Type)> RequestTypes,
    FrozenDictionary<string, JobFunctionDescriptor> Descriptors,
    FrozenDictionary<string, JobFunctionDescriptor> CanonicalDescriptors,
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
