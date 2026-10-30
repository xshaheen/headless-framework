// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Jobs;

/// <summary>
/// One recurring job an app declares on its Jobs options. The seed writes it as its own definition, identified by its
/// key, so several declarations may run one job with different arguments or zones.
/// </summary>
internal sealed class RecurringJobDeclaration
{
    private readonly Func<JobsRequestSerializationOptions, byte[]>? _serializeRequest;

    private RecurringJobDeclaration(
        string key,
        Type declaredType,
        bool takesArguments,
        Func<JobsRequestSerializationOptions, byte[]>? serializeRequest,
        string cronExpression,
        RecurringJobOptions? options
    )
    {
        Key = key;
        DeclaredType = declaredType;
        TakesArguments = takesArguments;
        _serializeRequest = serializeRequest;
        CronExpression = cronExpression;
        // A copy, so a caller that keeps mutating its interval array after the call does not change the declaration.
        Options = options is null ? null : options with { RetryIntervals = options.RetryIntervals?.ToArray() };
    }

    /// <summary>The key, unique within the app, that the seeded row's identity derives from.</summary>
    public string Key { get; }

    /// <summary>The argument type of a job that takes arguments, else the job class.</summary>
    public Type DeclaredType { get; }

    /// <summary>Whether the job is addressed by its argument type rather than by its class.</summary>
    public bool TakesArguments { get; }

    /// <summary>The declared six-field cron expression, or a <c>%name%</c> configuration key.</summary>
    public string CronExpression { get; }

    /// <summary>The declared recurring options, or <see langword="null"/> for the configured defaults.</summary>
    public RecurringJobOptions? Options { get; }

    public static RecurringJobDeclaration WithArguments<TArgs>(
        string key,
        TArgs request,
        string cronExpression,
        RecurringJobOptions? options
    )
    {
        return new(
            Argument.IsNotNullOrWhiteSpace(key),
            typeof(TArgs),
            takesArguments: true,
            // Serialized at seed time with the host's request settings, the way ScheduleRecurringAsync serializes a
            // runtime definition: the settings are not final until AddHeadlessJobs returns.
            serializationOptions => JobsHelper.CreateJobRequest(request, serializationOptions),
            Argument.IsNotNullOrWhiteSpace(cronExpression),
            options
        );
    }

    public static RecurringJobDeclaration WithoutArguments<TJob>(
        string key,
        string cronExpression,
        RecurringJobOptions? options
    )
        where TJob : IJob
    {
        return new(
            Argument.IsNotNullOrWhiteSpace(key),
            typeof(TJob),
            takesArguments: false,
            serializeRequest: null,
            Argument.IsNotNullOrWhiteSpace(cronExpression),
            options
        );
    }

    /// <summary>The declared request serialized with <paramref name="options"/>, or <see langword="null"/> without arguments.</summary>
    public byte[]? SerializeRequest(JobsRequestSerializationOptions options)
    {
        return _serializeRequest?.Invoke(options);
    }

    /// <summary>
    /// Finds the registered job this declaration runs, the way a scheduling call addresses it: by argument type, or by
    /// class for a job without arguments.
    /// </summary>
    /// <returns>The job's descriptor, or <see langword="null"/> with <paramref name="error"/> naming why.</returns>
    public JobFunctionDescriptor? TryResolve(JobFunctionRegistry registry, out string? error)
    {
        var typeName = JobFunctionRegistryBuilder.TypeDisplayName(DeclaredType);

        if (TakesArguments)
        {
            if (registry.DescriptorsByRequestType.TryGetValue(DeclaredType, out var byRequest))
            {
                error = null;
                return byRequest;
            }

            error =
                $"Recurring job declaration '{Key}' names argument type '{typeName}', which no registered job accepts.";
            return null;
        }

        if (!registry.DescriptorsByJobType.TryGetValue(DeclaredType, out var byJob))
        {
            error = $"Recurring job declaration '{Key}' names job '{typeName}', which is not registered in this host.";
            return null;
        }

        if (byJob.RequestType is not null)
        {
            error =
                $"Recurring job declaration '{Key}' names job '{byJob.FunctionName}', which takes arguments; declare it through its argument type.";
            return null;
        }

        error = null;
        return byJob;
    }
}
