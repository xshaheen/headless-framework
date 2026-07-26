// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Headless.Jobs;

internal interface IJobsOptionsSeeding
{
    bool SeedDefinedCronJobs { get; }
    Func<IServiceProvider, Task>? TimeSeederAction { get; }
    Func<IServiceProvider, Task>? CronSeederAction { get; }
}

internal sealed class JobsExecutionContext
{
    private long _nextOccurrenceTicks;
    internal Action<IServiceProvider>? ExternalProviderApplicationAction { get; set; }
    public Action<object?, CoreNotifyActionType>? NotifyCoreAction { get; set; }
    public string? LastHostExceptionMessage { get; set; }
    internal IJobsOptionsSeeding? OptionsSeeding { get; set; }

    private JobExecutionState[] _functions = [];
    internal JobExecutionState[] Functions => Volatile.Read(ref _functions);

    public void SetNextPlannedOccurrence(DateTime? dt)
    {
        Interlocked.Exchange(ref _nextOccurrenceTicks, dt?.Ticks ?? -1);
    }

    public DateTime? GetNextPlannedOccurrence()
    {
        var t = Interlocked.Read(ref _nextOccurrenceTicks);
        return t < 0 ? null : new DateTime(t, DateTimeKind.Utc);
    }

    public void SetFunctions(ReadOnlySpan<JobExecutionState> functions, JobFunctionRegistry functionRegistry)
    {
        var copy = new JobExecutionState[functions.Length];
        functions.CopyTo(copy.AsSpan());

        CacheFunctionReferences(copy.AsSpan(), functionRegistry);
        Volatile.Write(ref _functions, copy);
    }

    public void ClearFunctions() => Volatile.Write(ref _functions, []);

    internal static void CacheFunctionReferences(
        Span<JobExecutionState> functions,
        JobFunctionRegistry functionRegistry
    )
    {
        for (var i = 0; i < functions.Length; i++)
        {
            CacheFunctionReferences(functions[i], functionRegistry);
        }
    }

    // Stamps the cached delegate/priority/max-concurrency onto one context and recurses into its timed-job children.
    // Shared with the fallback background service so both pickup paths hydrate the whole tree identically.
    internal static void CacheFunctionReferences(JobExecutionState context, JobFunctionRegistry functionRegistry)
    {
        if (functionRegistry.Functions.TryGetValue(context.FunctionName, out var tickerItem))
        {
            context.CachedDelegate = tickerItem.Delegate;
            context.CachedPriority = tickerItem.Priority;
            context.CachedMaxConcurrency = tickerItem.MaxConcurrency;
        }

        if (context.TimeJobChildren is { Count: > 0 })
        {
            var childrenSpan = CollectionsMarshal.AsSpan(context.TimeJobChildren);
            for (var i = 0; i < childrenSpan.Length; i++)
            {
                CacheFunctionReferences(childrenSpan[i], functionRegistry);
            }
        }
    }
}
