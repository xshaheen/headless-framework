using Headless.Checks;
using Headless.Jobs.Base;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

public delegate Task JobFunctionDelegate(
    CancellationToken cancellationToken,
    IServiceProvider serviceProvider,
    JobFunctionContext context
);

/// <summary>
/// Provider for managing job functions and their request types using FrozenDictionary.
/// Uses a callback-based approach to collect all registrations and create a single optimized FrozenDictionary.
/// </summary>
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

    // Final frozen dictionaries
    public static FrozenDictionary<
        string,
        (string cronExpression, JobPriority Priority, JobFunctionDelegate Delegate, int MaxConcurrency)
    > JobFunctions { get; private set; } =
        FrozenDictionary<string, (string, JobPriority, JobFunctionDelegate, int)>.Empty;

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
    /// Builds the final FrozenDictionaries by executing all callbacks with optimal capacity.
    /// Uses a single-pass approach: directly creates optimally-sized dictionaries and populates them.
    /// This method should be called once after all registration is complete.
    /// After calling this method, no more registrations should be made.
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

public static class JobsRequestProvider
{
    public static async Task<T?> GetRequestAsync<T>(JobFunctionContext context, CancellationToken cancellationToken)
    {
        try
        {
            var internalJobsManager = context.ServiceScope.ServiceProvider.GetRequiredService<IInternalJobManager>();
            return await internalJobsManager.GetRequestAsync<T>(context.Id, context.Type, cancellationToken);
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
