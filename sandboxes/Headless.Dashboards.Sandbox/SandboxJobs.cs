// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;

namespace Headless.Dashboards.Sandbox;

/// <summary>Shape of a <see cref="ProgressJob"/> run; the scenarios pick values that make each dashboard state last long enough to observe.</summary>
/// <param name="Steps">How many steps the run reports, one report per step.</param>
/// <param name="StepDelayMs">How long each step takes.</param>
/// <param name="FailAtStep">The step at which the run throws, or <see langword="null"/> to finish.</param>
/// <param name="Label">Text shown in each progress message.</param>
public sealed record ProgressJobArgs(int Steps, int StepDelayMs, int? FailAtStep, string Label);

/// <summary>Reports progress once per step, so the dashboard shows a moving bar, then finishes or fails as asked.</summary>
[Job(SandboxJobIds.Progress)]
public sealed class ProgressJob : IJob<ProgressJobArgs>
{
    public async ValueTask ExecuteAsync(JobContext<ProgressJobArgs> context, CancellationToken cancellationToken)
    {
        var args = context.Request;
        for (var step = 1; step <= args.Steps; step++)
        {
            await Task.Delay(args.StepDelayMs, cancellationToken);
            context.ReportProgress(
                100d * step / args.Steps,
                string.Create(CultureInfo.InvariantCulture, $"{args.Label}: step {step} of {args.Steps}")
            );

            if (step == args.FailAtStep)
            {
                throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"{args.Label} failed at step {step} of {args.Steps}.")
                );
            }
        }
    }
}

/// <summary>Runs briefly without reporting progress, the baseline a row with no progress must keep.</summary>
[Job(SandboxJobIds.Quiet)]
public sealed class QuietJob : IJob
{
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }
}

/// <summary>Fails at once without reporting progress, so the dashboard has a Failed row to requeue.</summary>
[Job(SandboxJobIds.AlwaysFails)]
public sealed class AlwaysFailsJob : IJob
{
    public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Sandbox job failed on purpose.");
    }
}

/// <summary>The order a <see cref="OrderSyncJob"/> run syncs; only this id differs between the runs of one scenario.</summary>
/// <param name="OrderId">The order the run fails to sync.</param>
public sealed record OrderSyncArgs(int OrderId);

/// <summary>
/// Fails with a message that carries the order id, so every run's message differs while the cause does not: the
/// failures share one fingerprint and the dashboard groups them into one Needs you item.
/// </summary>
[Job(SandboxJobIds.OrderSync)]
public sealed class OrderSyncJob : IJob<OrderSyncArgs>
{
    public ValueTask ExecuteAsync(JobContext<OrderSyncArgs> context, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Order {context.Request.OrderId} could not be synced: the warehouse API answered 503."
            )
        );
    }
}

/// <summary>One step of the sandbox chain: its name, and whether it fails.</summary>
/// <param name="Step">The step's name, shown in its failure message.</param>
/// <param name="Fails">Whether the step throws instead of finishing.</param>
public sealed record ChainStepArgs(string Step, bool Fails);

/// <summary>A chain step that runs briefly, then finishes or fails as its arguments say.</summary>
[Job(SandboxJobIds.ChainStep)]
public sealed class ChainStepJob : IJob<ChainStepArgs>
{
    public async ValueTask ExecuteAsync(JobContext<ChainStepArgs> context, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        if (context.Request.Fails)
        {
            throw new InvalidOperationException($"Chain step '{context.Request.Step}' failed on purpose.");
        }
    }
}

/// <summary>
/// Runs 15 s, then fails. Its scenario schedules it every 10 s under the skip overlap policy, so occurrences that fall
/// due while one still runs are skipped and every occurrence that runs fails.
/// </summary>
[Job(SandboxJobIds.OverlapFails)]
public sealed class OverlapFailsJob : IJob
{
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);

        throw new InvalidOperationException("The nightly ledger export timed out.");
    }
}

/// <summary>
/// A cron job defined in code: the host seeds its definition as it starts, so the Cron jobs view always lists it. It
/// runs at 03:00 each day and does nothing.
/// </summary>
[Job(SandboxJobIds.NightlyReport, Cron = "0 0 3 * * *")]
public sealed class NightlyReportJob : IJob
{
    public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

/// <summary>
/// Reports progress for 5 minutes and never observes its cancellation token, so Cancel only requests a stop: the
/// dashboard shows the request while the run keeps going until it ends on its own.
/// </summary>
[Job(SandboxJobIds.Stubborn)]
public sealed class StubbornJob : IJob
{
    private const int _Steps = 300;

    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        for (var step = 1; step <= _Steps; step++)
        {
            // CancellationToken.None, not cancellationToken: the job stands in for code that cannot be interrupted.
            await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
            context.ReportProgress(
                100d * step / _Steps,
                string.Create(CultureInfo.InvariantCulture, $"ignoring cancellation: second {step} of {_Steps}")
            );
        }
    }
}

/// <summary>The stable job identities the scenarios and the feature map name.</summary>
public static class SandboxJobIds
{
    public const string Progress = "sandbox.progress";
    public const string Quiet = "sandbox.quiet";
    public const string AlwaysFails = "sandbox.always-fails";
    public const string OrderSync = "sandbox.order-sync";
    public const string ChainStep = "sandbox.chain-step";
    public const string OverlapFails = "sandbox.overlap-fails";
    public const string NightlyReport = "sandbox.nightly-report";
    public const string Stubborn = "sandbox.stubborn";
}
