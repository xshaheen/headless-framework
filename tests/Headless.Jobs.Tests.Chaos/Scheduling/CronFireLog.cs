// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Scheduling;

/// <summary>One run of a recurring definition, as its handler saw it.</summary>
/// <param name="Function">The function the definition runs; each scenario gives every definition its own.</param>
/// <param name="ScheduledFor">The instant the occurrence stands for (UTC).</param>
/// <param name="Node">The host that ran it.</param>
/// <param name="RecoveredFromUtc">For a coalesced recovery run, the first instant of the backlog it stands in for.</param>
/// <param name="FiredAtStoreUtc">
/// The store's clock when the handler ran, when the scenario registers a <see cref="StoreClock"/>. A recovery run
/// executes after the pass that coalesced its backlog, so no instant it stands in for is later than this.
/// </param>
public sealed record CronFire(
    string Function,
    DateTime ScheduledFor,
    string Node,
    DateTime? RecoveredFromUtc = null,
    DateTime? FiredAtStoreUtc = null
)
{
    public bool IsRecoveryRun => RecoveredFromUtc is not null;
}

/// <summary>
/// Reads the shared store's clock. A scenario whose oracle bounds a recovery run's backlog registers one, because the
/// node clocks are skewed and the job context does not say when a run fired.
/// </summary>
public sealed record StoreClock(Func<CancellationToken, Task<DateTime>> UtcNowAsync);

/// <summary>The name a chaos host reports its runs under.</summary>
public sealed record ChaosNodeName(string Value);

/// <summary>Every recurring run of a scenario, across all of its hosts (they share one process).</summary>
public sealed class CronFireLog
{
    private readonly ConcurrentQueue<CronFire> _fires = new();

    public void Record(CronFire fire) => _fires.Enqueue(fire);

    public IReadOnlyList<CronFire> Snapshot() => [.. _fires];

    public int Count => _fires.Count;
}

/// <summary>
/// The hand-written job catalog of the scheduling scenarios: a fixed set of functions with no cron of their own, so a
/// scenario creates each recurring definition at runtime through <see cref="ICronJobManager{TCronJob}"/> (expression
/// and zone included) and attributes every run to its definition by function name.
/// </summary>
public sealed class SchedulingChaosJobsModule : IJobsModule
{
    public const string FunctionPrefix = "chaos.cron.";

    public const int FunctionCount = 8;

    private SchedulingChaosJobsModule() { }

    public static string Function(int index) => FunctionPrefix + index.ToString(CultureInfo.InvariantCulture);

    static void IJobsModule.Register(JobsCatalogBuilder catalog)
    {
        var functions = new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal);
        var descriptors = new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal);

        for (var i = 0; i < FunctionCount; i++)
        {
            var name = Function(i);
            functions[name] = new JobFunctionRegistration
            {
                CronExpression = string.Empty,
                Priority = JobPriority.Normal,
                Delegate = static async (services, context, cancellationToken) =>
                {
                    var storeClock = services.GetService<StoreClock>();
                    DateTime? firedAt = storeClock is null
                        ? null
                        : await storeClock.UtcNowAsync(cancellationToken).ConfigureAwait(false);

                    services
                        .GetRequiredService<CronFireLog>()
                        .Record(
                            new CronFire(
                                context.FunctionName,
                                DateTime.SpecifyKind(context.ScheduledFor, DateTimeKind.Utc),
                                services.GetService<ChaosNodeName>()?.Value ?? "local",
                                context.RecoveredFromUtc is { } from
                                    ? DateTime.SpecifyKind(from, DateTimeKind.Utc)
                                    : null,
                                firedAt
                            )
                        );
                },
                MaxConcurrency = 1,
            };
            descriptors[name] = new JobFunctionDescriptor(name, null, string.Empty, JobPriority.Normal, 1);
        }

        catalog.AddFunctions(functions);
        catalog.AddDescriptors(descriptors);
    }
}
