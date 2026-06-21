// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Base;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

/// <summary>
/// Delegate type for job function handlers. The source generator emits implementations of this signature
/// that instantiate the job class from DI and invoke its <c>[JobFunction]</c>-annotated method.
/// </summary>
/// <param name="cancellationToken">Token signalled when the job is cancelled or the host is shutting down.</param>
/// <param name="serviceProvider">The scoped service provider for this execution.</param>
/// <param name="context">Scheduling metadata and cooperative-cancel hook for this execution.</param>
public delegate Task JobFunctionDelegate(
    CancellationToken cancellationToken,
    IServiceProvider serviceProvider,
    JobFunctionContext context
);

/// <summary>
/// Global registry of job function delegates and their associated request types. Populated at application
/// startup by the source-generated <c>ModuleInitializer</c> and frozen into read-optimized dictionaries
/// via <see cref="Build"/> before the first job is dispatched.
/// </summary>
/// <remarks>
/// The registration flow is callback-based: each source-generated assembly calls
/// <c>RegisterFunctions</c> and <c>RegisterRequestType</c> to enqueue its entries, then
/// <see cref="Build"/> executes all callbacks in order and freezes the results. After <c>Build</c> is
/// called, no further registrations take effect.
/// </remarks>
public static class JobFunctionProvider
{
    // Callback actions to collect registrations
    private static Action<Dictionary<string, (string, Type)>>? _requestTypeRegistrations;
    private static Action<
        Dictionary<
            string,
            (string cronExpression, JobPriority Priority, JobFunctionDelegate Delegate, int MaxConcurrency)
        >
    >? _functionRegistrations;

    /// <summary>
    /// Frozen lookup of all registered job functions, keyed by function name. Each entry carries the
    /// cron expression (empty string for time jobs), scheduling priority, execution delegate, and maximum
    /// concurrency limit. Available after <see cref="Build"/> is called.
    /// </summary>
    public static FrozenDictionary<
        string,
        (string cronExpression, JobPriority Priority, JobFunctionDelegate Delegate, int MaxConcurrency)
    > JobFunctions { get; private set; } =
        FrozenDictionary<string, (string, JobPriority, JobFunctionDelegate, int)>.Empty;

    /// <summary>
    /// Frozen lookup of request type metadata, keyed by function name. Each entry carries the full type
    /// name and <see cref="Type"/> object for functions that accept a typed request payload. Available
    /// after <see cref="Build"/> is called.
    /// </summary>
    public static FrozenDictionary<string, (string, Type)> JobFunctionRequestTypes { get; private set; } =
        FrozenDictionary<string, (string, Type)>.Empty;

    /// <summary>
    /// Registers job functions during application startup by adding to the callback chain.
    /// This method should only be called during application startup before Build() is called.
    /// </summary>
    /// <param name="functions">The functions to register. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when functions parameter is null.</exception>
    public static void RegisterFunctions(IDictionary<string, (string, JobPriority, JobFunctionDelegate, int)> functions)
    {
        Argument.IsNotNull(functions);

        if (functions.Count == 0)
        {
            return;
        }

        _functionRegistrations += dict =>
        {
            foreach (var (key, value) in functions)
            {
                dict.TryAdd(key, value); // Preserves existing entries
            }
        };
    }

    /// <summary>
    /// Registers job functions with capacity hint during application startup by adding to the callback chain.
    /// This method should only be called during application startup before Build() is called.
    /// </summary>
    /// <param name="functions">The functions to register. Cannot be null.</param>
    /// <param name="_">The total expected capacity (ignored - capacity calculated automatically).</param>
    /// <exception cref="ArgumentNullException">Thrown when functions parameter is null.</exception>
    public static void RegisterFunctions(
        IDictionary<string, (string, JobPriority, JobFunctionDelegate, int)> functions,
        int _
    )
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

        _requestTypeRegistrations += dict =>
        {
            foreach (var (key, value) in requestTypes)
            {
                dict.TryAdd(key, value); // Preserves existing entries
            }
        };
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
    /// Updates cron expressions for registered functions by adding to the callback chain.
    /// This method should only be called during application startup before Build() is called.
    /// </summary>
    /// <param name="configuration">IConfiguration to update based on path</param>
    /// <exception cref="ArgumentNullException">Thrown when cronUpdates parameter is null.</exception>
    internal static void UpdateCronExpressionsFromIConfiguration(IConfiguration configuration)
    {
        _functionRegistrations += dict =>
        {
            foreach (var (key, value) in dict)
            {
                if (value.cronExpression.StartsWith('%'))
                {
                    var configKey = value.cronExpression.Trim('%');
                    var mappedCronExpression = configuration[configKey];

                    if (!string.IsNullOrEmpty(mappedCronExpression))
                    {
                        dict[key] = (mappedCronExpression, value.Priority, value.Delegate, value.MaxConcurrency);
                    }
                }
            }
        };
    }

    /// <summary>
    /// Freezes the registered functions and request types into read-optimized
    /// <see cref="FrozenDictionary{TKey,TValue}"/> instances. Must be called exactly once after all
    /// assemblies have had their <c>ModuleInitializer</c> executed and before the first job is dispatched.
    /// After this call, <see cref="JobFunctions"/> and <see cref="JobFunctionRequestTypes"/> are
    /// populated and subsequent <c>Register*</c> calls have no effect.
    /// </summary>
    public static void Build()
    {
        // Build functions dictionary
        if (_functionRegistrations != null)
        {
            // Single pass: execute callbacks directly on final dictionary
            var functionsDict = new Dictionary<
                string,
                (string cronExpression, JobPriority Priority, JobFunctionDelegate Delegate, int MaxConcurrency)
            >(StringComparer.Ordinal);
            _functionRegistrations(functionsDict);
            JobFunctions = functionsDict.ToFrozenDictionary();
            _functionRegistrations = null; // Release callback chain
        }
        else
        {
            JobFunctions = new Dictionary<
                string,
                (string cronExpression, JobPriority Priority, JobFunctionDelegate Delegate, int MaxConcurrency)
            >(StringComparer.Ordinal).ToFrozenDictionary();
        }

        // Build request types dictionary
        if (_requestTypeRegistrations != null)
        {
            // Single pass: execute callbacks directly on final dictionary
            var requestTypesDict = new Dictionary<string, (string, Type)>(StringComparer.Ordinal);
            _requestTypeRegistrations(requestTypesDict);
            JobFunctionRequestTypes = requestTypesDict.ToFrozenDictionary();
            _requestTypeRegistrations = null; // Release callback chain
        }
        else
        {
            JobFunctionRequestTypes = new Dictionary<string, (string, Type)>(
                StringComparer.Ordinal
            ).ToFrozenDictionary();
        }
    }
}

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
