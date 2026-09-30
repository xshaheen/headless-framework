// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Globalization;
using Headless.Checks;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Microsoft.Extensions.Configuration;

namespace Headless.Jobs;

/// <summary>
/// Collects one host's generated job registrations while its job registry is built. Each generated
/// <see cref="IJobsModule"/> writes its functions, argument types, descriptors, and middleware here.
/// </summary>
/// <remarks>
/// A builder exists only while one host's registry is being built and is discarded when that registry freezes, so
/// generated registrations never reach process-wide state. Every entry remembers the module that added it, which lets a
/// cross-module conflict name both modules.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class JobsCatalogBuilder
{
    /// <summary>The configuration section whose children tune jobs by identity.</summary>
    internal const string TuningConfigurationSection = "Headless:Jobs:Jobs";

    /// <summary>The source of every entry the framework registers itself rather than through a generated module.</summary>
    internal const string FrameworkSource = "Headless.Jobs.Core";

    private readonly List<(string Source, string Name, JobFunctionRegistration Registration)> _functions = [];
    private readonly List<(string Source, string Name, (string, Type) RequestType)> _requestTypes = [];
    private readonly List<(string Source, string Name, JobFunctionDescriptor Descriptor)> _descriptors = [];
    private readonly List<JobScheduleMiddlewareRegistration> _schedule = [];
    private readonly List<JobExecuteMiddlewareRegistration> _execute = [];
    private string _source = FrameworkSource;

    internal JobsCatalogBuilder() { }

    /// <summary>Adds generated job registrations, keyed by job identity.</summary>
    /// <param name="functions">The registrations to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="functions"/> is <see langword="null"/>.</exception>
    public void AddFunctions(IDictionary<string, JobFunctionRegistration> functions)
    {
        Argument.IsNotNull(functions);
        _functions.AddRange(functions.Select(entry => (_source, entry.Key, entry.Value)));
    }

    /// <summary>Adds generated argument types, keyed by job identity.</summary>
    /// <param name="requestTypes">The argument type names and types to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="requestTypes"/> is <see langword="null"/>.</exception>
    public void AddRequestTypes(IDictionary<string, (string, Type)> requestTypes)
    {
        Argument.IsNotNull(requestTypes);
        _requestTypes.AddRange(requestTypes.Select(entry => (_source, entry.Key, entry.Value)));
    }

    /// <summary>Adds generated descriptors, keyed by job identity.</summary>
    /// <param name="descriptors">The descriptors to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="descriptors"/> is <see langword="null"/>.</exception>
    public void AddDescriptors(IDictionary<string, JobFunctionDescriptor> descriptors)
    {
        Argument.IsNotNull(descriptors);
        _descriptors.AddRange(descriptors.Select(entry => (_source, entry.Key, entry.Value)));
    }

    /// <summary>Adds one generated schedule middleware declaration.</summary>
    /// <param name="identity">The stable middleware identity that breaks priority ties.</param>
    /// <param name="function">The job identity the middleware is limited to, or <see langword="null"/> for every job.</param>
    /// <param name="priority">Ordering priority; lower values run first.</param>
    /// <param name="dispatch">Resolves and invokes the middleware.</param>
    /// <exception cref="ArgumentNullException"><paramref name="identity"/> or <paramref name="dispatch"/> is <see langword="null"/>.</exception>
    public void AddScheduleMiddleware(
        string identity,
        string? function,
        int priority,
        JobScheduleMiddlewareDispatch dispatch
    )
    {
        Argument.IsNotNull(identity);
        Argument.IsNotNull(dispatch);
        _schedule.Add(new(identity, function, priority, dispatch));
    }

    /// <summary>Adds one generated execute middleware declaration.</summary>
    /// <param name="identity">The stable middleware identity that breaks priority ties.</param>
    /// <param name="function">The job identity the middleware is limited to, or <see langword="null"/> for every job.</param>
    /// <param name="priority">Ordering priority; lower values run first.</param>
    /// <param name="dispatch">Resolves and invokes the middleware.</param>
    /// <exception cref="ArgumentNullException"><paramref name="identity"/> or <paramref name="dispatch"/> is <see langword="null"/>.</exception>
    public void AddExecuteMiddleware(
        string identity,
        string? function,
        int priority,
        JobExecuteMiddlewareDispatch dispatch
    )
    {
        Argument.IsNotNull(identity);
        Argument.IsNotNull(dispatch);
        _execute.Add(new(identity, function, priority, dispatch));
    }

    /// <summary>Runs one module's generated registration, attributing its entries to the module.</summary>
    internal void AddModule(Type moduleType, Action<JobsCatalogBuilder> register)
    {
        _source = moduleType.FullName ?? moduleType.Name;
        try
        {
            register(this);
        }
        finally
        {
            _source = FrameworkSource;
        }
    }

    /// <summary>
    /// Freezes the catalog into one host's registry: detects conflicts across modules, applies tuning and then
    /// configuration, and resolves the host's run filter.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Two modules declare one identity, argument type, or job type; tuning or configuration names an unknown job or
    /// carries an invalid value; or a <c>RunOnly</c> entry matches no registered job.
    /// </exception>
    internal JobFunctionRegistry Build(
        IEnumerable<JobTuning> tunings,
        IReadOnlyCollection<string> runOnly,
        IConfiguration? configuration
    )
    {
        _ThrowOnConflicts();

        var registry = JobFunctionRegistryBuilder.Build(
            [.. _functions.Select(x => new KeyValuePair<string, JobFunctionRegistration>(x.Name, x.Registration))],
            [.. _requestTypes.Select(x => new KeyValuePair<string, (string, Type)>(x.Name, x.RequestType))],
            [.. _descriptors.Select(x => new KeyValuePair<string, JobFunctionDescriptor>(x.Name, x.Descriptor))],
            configuration
        );

        var functions = registry.Functions.ToDictionary(StringComparer.Ordinal);
        var descriptors = registry.Descriptors.ToDictionary(StringComparer.Ordinal);
        var options = new Dictionary<string, JobOptions>(StringComparer.Ordinal);
        var schedule = new List<JobScheduleMiddlewareRegistration>(_schedule);
        var execute = new List<JobExecuteMiddlewareRegistration>(_execute);
        var errors = new List<string>();

        foreach (var tuning in tunings)
        {
            if (!functions.ContainsKey(tuning.Identity))
            {
                errors.Add($"Tune names job '{tuning.Identity}', which no registered module declares.");
                continue;
            }

            _Apply(functions, descriptors, tuning.Identity, tuning.MaxConcurrency, tuning.Priority);
            if (tuning.FailurePolicy is { } failurePolicy)
            {
                functions[tuning.Identity] = functions[tuning.Identity] with { FailurePolicy = failurePolicy };
            }

            if (tuning.Options is { } tunedOptions)
            {
                options[tuning.Identity] = tunedOptions;
            }

            _AddMissingMiddleware(schedule, tuning.ScheduleMiddleware);
            _AddMissingMiddleware(execute, tuning.ExecuteMiddleware);
        }

        if (configuration is not null)
        {
            _ApplyConfiguration(configuration, functions, descriptors, errors);
        }

        if (errors.Count != 0)
        {
            throw new InvalidOperationException(
                $"Jobs tuning is invalid:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}"
            );
        }

        var frozenFunctions = functions.ToFrozenDictionary(StringComparer.Ordinal);
        var frozenDescriptors = descriptors.ToFrozenDictionary(StringComparer.Ordinal);

        return registry with
        {
            Functions = frozenFunctions,
            Descriptors = frozenDescriptors,
            DescriptorsByRequestType = JobFunctionRegistryBuilder.IndexDescriptorsByRequestType(frozenDescriptors),
            DescriptorsByJobType = JobFunctionRegistryBuilder.IndexDescriptorsByJobType(
                frozenFunctions,
                frozenDescriptors
            ),
            Middleware = JobMiddlewarePipeline.Create(schedule, execute),
            // Resolved against every registered identity, so a filter never makes a job unschedulable.
            RunFilter = JobsRunFilter.Create(runOnly, frozenFunctions.Keys),
            OptionsByFunction = options.ToFrozenDictionary(StringComparer.Ordinal),
        };
    }

    private static void _ApplyConfiguration(
        IConfiguration configuration,
        Dictionary<string, JobFunctionRegistration> functions,
        Dictionary<string, JobFunctionDescriptor> descriptors,
        List<string> errors
    )
    {
        foreach (var job in configuration.GetSection(TuningConfigurationSection).GetChildren())
        {
            var path = $"{TuningConfigurationSection}:{job.Key}";
            if (!functions.ContainsKey(job.Key))
            {
                errors.Add($"Configuration '{path}' names job '{job.Key}', which no registered module declares.");
                continue;
            }

            int? maxConcurrency = null;
            JobPriority? priority = null;
            foreach (var setting in job.GetChildren())
            {
                if (string.Equals(setting.Key, "Concurrency", StringComparison.OrdinalIgnoreCase))
                {
                    if (
                        int.TryParse(setting.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                        && value >= 0
                    )
                    {
                        maxConcurrency = value;
                    }
                    else
                    {
                        errors.Add($"Configuration '{setting.Path}' must be a non-negative integer.");
                    }
                }
                else if (string.Equals(setting.Key, "Priority", StringComparison.OrdinalIgnoreCase))
                {
                    if (
                        Enum.TryParse<JobPriority>(setting.Value, ignoreCase: true, out var value)
                        && Enum.IsDefined(value)
                        && !int.TryParse(setting.Value, CultureInfo.InvariantCulture, out _)
                    )
                    {
                        priority = value;
                    }
                    else
                    {
                        errors.Add(
                            $"Configuration '{setting.Path}' must name a job priority: {string.Join(", ", Enum.GetNames<JobPriority>())}."
                        );
                    }
                }
                else
                {
                    errors.Add(
                        $"Configuration '{setting.Path}' is not a job setting. Supported settings are Concurrency and Priority."
                    );
                }
            }

            _Apply(functions, descriptors, job.Key, maxConcurrency, priority);
        }
    }

    private static void _Apply(
        Dictionary<string, JobFunctionRegistration> functions,
        Dictionary<string, JobFunctionDescriptor> descriptors,
        string identity,
        int? maxConcurrency,
        JobPriority? priority
    )
    {
        if (maxConcurrency is null && priority is null)
        {
            return;
        }

        var registration = functions[identity];
        functions[identity] = registration with
        {
            MaxConcurrency = maxConcurrency ?? registration.MaxConcurrency,
            Priority = priority ?? registration.Priority,
        };

        if (descriptors.TryGetValue(identity, out var descriptor))
        {
            descriptors[identity] = descriptor.With(priority: priority, maxConcurrency: maxConcurrency);
        }
    }

    private void _ThrowOnConflicts()
    {
        var conflicts = new List<string>();

        // A module lists each identity once, so an identity seen from two sources is a cross-module conflict. The same
        // identity added twice by one source is a hand-written registration error and still fails in the registry build.
        conflicts.AddRange(
            _functions
                .Select(x => (x.Source, Key: x.Name))
                .Concat(_descriptors.Select(x => (x.Source, Key: x.Name)))
                .GroupBy(x => x.Key, StringComparer.Ordinal)
                .Select(group => (group.Key, Sources: _Sources(group.Select(x => x.Source))))
                .Where(x => x.Sources.Length > 1)
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => $"Job '{x.Key}' is declared by {_JoinSources(x.Sources)}.")
        );

        conflicts.AddRange(
            _descriptors
                .Where(x => x.Descriptor.RequestType is not null)
                .Select(x => (x.Source, x.Name, Type: x.Descriptor.RequestType!))
                .Concat(_requestTypes.Select(x => (x.Source, x.Name, Type: x.RequestType.Item2)))
                .GroupBy(x => x.Type)
                .Select(group =>
                    (
                        Type: group.Key,
                        Sources: _Sources(group.Select(x => x.Source)),
                        Jobs: group.Select(x => x.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                    )
                )
                .Where(x => x.Sources.Length > 1)
                .OrderBy(x => JobFunctionRegistryBuilder.TypeDisplayName(x.Type), StringComparer.Ordinal)
                .Select(x =>
                    $"Argument type '{JobFunctionRegistryBuilder.TypeDisplayName(x.Type)}' is taken by jobs {string.Join(", ", x.Jobs.Select(job => $"'{job}'"))} in {_JoinSources(x.Sources)}."
                )
        );

        conflicts.AddRange(
            _functions
                .Where(x => x.Registration.JobType is not null)
                .GroupBy(x => x.Registration.JobType!)
                .Select(group => (Type: group.Key, Sources: _Sources(group.Select(x => x.Source))))
                .Where(x => x.Sources.Length > 1)
                .OrderBy(x => JobFunctionRegistryBuilder.TypeDisplayName(x.Type), StringComparer.Ordinal)
                .Select(x =>
                    $"Job type '{JobFunctionRegistryBuilder.TypeDisplayName(x.Type)}' is registered by {_JoinSources(x.Sources)}."
                )
        );

        if (conflicts.Count != 0)
        {
            throw new InvalidOperationException(
                $"Jobs modules conflict:{Environment.NewLine}{string.Join(Environment.NewLine, conflicts)}"
            );
        }
    }

    private static string[] _Sources(IEnumerable<string> sources) =>
        [.. sources.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static string _JoinSources(string[] sources) => string.Join(" and ", sources.Select(x => $"'{x}'"));

    /// <summary>
    /// Appends each tuned middleware the list does not already hold for the same identity and job, in tuning order, so
    /// the same middleware tuned twice onto one job still runs once.
    /// </summary>
    private static void _AddMissingMiddleware<T>(List<T> registered, IEnumerable<T> tuned)
        where T : IJobMiddlewareRegistration
    {
        foreach (var middleware in tuned)
        {
            var exists = registered.Exists(existing =>
                string.Equals(existing.Identity, middleware.Identity, StringComparison.Ordinal)
                && string.Equals(existing.Function, middleware.Function, StringComparison.Ordinal)
            );
            if (!exists)
            {
                registered.Add(middleware);
            }
        }
    }
}
