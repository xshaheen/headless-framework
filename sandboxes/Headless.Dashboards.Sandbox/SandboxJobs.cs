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

/// <summary>The stable job identities the scenarios and the feature map name.</summary>
public static class SandboxJobIds
{
    public const string Progress = "sandbox.progress";
    public const string Quiet = "sandbox.quiet";
    public const string AlwaysFails = "sandbox.always-fails";
}
