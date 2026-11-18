// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.Cluster;

/// <summary>What one job body does, carried as the job's request so every node runs it the same way.</summary>
/// <param name="Index">The workload index.</param>
/// <param name="WorkMilliseconds">How long the body runs.</param>
/// <param name="IgnoresToken">Whether the body keeps running after its token is cancelled.</param>
/// <param name="DoubleRun">Sabotage for a negative control: the body records its side effect twice.</param>
public sealed record ClusterWork(int Index, int WorkMilliseconds, bool IgnoresToken, bool DoubleRun = false);

/// <summary>
/// One host incarnation in a node slot, as its job bodies see it: who they run on, where they record, and why their
/// token was cancelled.
/// </summary>
public sealed class ClusterIncarnation(
    string node,
    int number,
    JobLedger ledger,
    ClusterStoreReader reader,
    INodeMembership membership,
    IHostApplicationLifetime lifetime
)
{
    public string Node { get; } = node;

    public int Number { get; } = number;

    public string Runner => $"{Node}#{Number.ToString(CultureInfo.InvariantCulture)}";

    public JobLedger Ledger { get; } = ledger;

    public ClusterStoreReader Reader { get; } = reader;

    public INodeMembership Membership { get; } = membership;

    /// <summary>
    /// Classifies a cancelled run. Membership loss and a failed renewal both charge the retry budget; a graceful host
    /// stop with membership intact hands the row back uncharged.
    /// </summary>
    public AttemptEnd CauseOfCancellation() =>
        Membership.LocalMembershipLostToken.IsCancellationRequested ? AttemptEnd.MembershipLost
        : lifetime.ApplicationStopping.IsCancellationRequested ? AttemptEnd.GracefulStop
        : AttemptEnd.LeaseLost;
}

/// <summary>The cluster's one job function, registered the way a generated module would register it.</summary>
public sealed class ClusterJobsModule : IJobsModule
{
    public const string FunctionName = "Chaos_Cluster_Work";

    private ClusterJobsModule() { }

    static void IJobsModule.Register(JobsCatalogBuilder catalog)
    {
        catalog.AddFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                [FunctionName] = new JobFunctionRegistration
                {
                    CronExpression = string.Empty,
                    Priority = JobPriority.Normal,
                    Delegate = RunAsync,
                    MaxConcurrency = 0,
                },
            }
        );
        catalog.AddRequestTypes(
            new Dictionary<string, (string, Type)>(StringComparer.Ordinal)
            {
                [FunctionName] = (typeof(ClusterWork).FullName!, typeof(ClusterWork)),
            }
        );
        catalog.AddDescriptors(
            new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
            {
                [FunctionName] = new(FunctionName, typeof(ClusterWork), string.Empty, JobPriority.Normal, 0),
            }
        );
    }

    /// <summary>
    /// Records the start, does the work, and records how it ended. A body that ignores its token reads its own row
    /// before it returns, so the oracle can tell whether its completion should have been refused.
    /// </summary>
    public static async Task RunAsync(
        IServiceProvider services,
        JobContext context,
        CancellationToken cancellationToken
    )
    {
        var incarnation = services.GetRequiredService<ClusterIncarnation>();
        var work =
            await JobsRequestProvider.GetRequestAsync<ClusterWork>(context, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Job {context.Id} carries no request.");
        var owner = incarnation.Membership.GetOwnerTag();
        var attempt = incarnation.Ledger.Start(
            context.Id,
            incarnation.Node,
            incarnation.Number,
            owner,
            context.RetryCount
        );

        if (work.DoubleRun)
        {
            incarnation
                .Ledger.Start(context.Id, incarnation.Node, incarnation.Number, owner, context.RetryCount)
                ?.End(AttemptEnd.Completed);
        }

        try
        {
            if (work.IgnoresToken)
            {
                // real-wait: the body stands in for user work that ignores cancellation and runs on past lease loss
                await Task.Delay(work.WorkMilliseconds, CancellationToken.None).ConfigureAwait(false);

                if (await incarnation.Reader.ObserveAsync(context.Id).ConfigureAwait(false) is { } observation)
                {
                    attempt?.Observe(observation);
                }
            }
            else
            {
                // real-wait: the body stands in for user work that takes real time and spans the injected faults
                await Task.Delay(work.WorkMilliseconds, cancellationToken).ConfigureAwait(false);
            }

            attempt?.End(AttemptEnd.Completed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            attempt?.End(incarnation.CauseOfCancellation());
            throw;
        }
    }
}
