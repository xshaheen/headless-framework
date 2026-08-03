// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Base;
using Headless.Jobs.Enums;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Compiles the exact API shapes the misfire-recovery documentation shows. Documentation drift is silent — a renamed
/// property or changed default leaves the prose looking authoritative while it quietly stops being true — so the
/// examples are pinned here rather than trusted to review.
/// </summary>
public sealed class DocumentedRecoveryApiTests : TestBase
{
    private sealed class DocumentedJob
    {
        // The attribute example from "Configuring it".
        [JobFunction("reports.nightly", "0 0 2 * * *", OnMissedRun = MissedRunPolicy.Skip, MissedRunGraceSeconds = 300)]
        public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        // The job-visible context example from "What an executing job sees".
        public Task ExecuteAsync(JobFunctionContext context, CancellationToken cancellationToken)
        {
            if (context.IsRecoveryRun)
            {
                _ = context.RecoveredFromUtc!.Value;
                _ = context.Lateness;

                return Task.CompletedTask;
            }

            _ = context.ScheduledFor;

            return Task.CompletedTask;
        }
    }

    [Fact]
    public void the_documented_attribute_shape_compiles_and_round_trips()
    {
        var attribute = typeof(DocumentedJob)
            .GetMethod(nameof(DocumentedJob.RunAsync))!
            .GetCustomAttributes(typeof(JobFunctionAttribute), inherit: false)
            .Cast<JobFunctionAttribute>()
            .Single();

        attribute.OnMissedRun.Should().Be(MissedRunPolicy.Skip);
        attribute.MissedRunGraceSeconds.Should().Be(300);
    }

    [Fact]
    public void the_documented_scheduler_defaults_compile_and_match_the_documented_values()
    {
        var scheduler = new SchedulerOptionsBuilder();

        // The documented defaults, asserted so the prose cannot drift from them silently.
        scheduler.DefaultMissedRunPolicy.Should().Be(MissedRunPolicy.Coalesce);
        scheduler.DefaultMissedRunGraceSeconds.Should().Be(60);

        // The documented configuration example.
        scheduler.DefaultMissedRunPolicy = MissedRunPolicy.Coalesce;
        scheduler.DefaultMissedRunGraceSeconds = 60;

        scheduler.DefaultMissedRunGraceSeconds.Should().Be(JobsRecoveryDefaults.MissedRunGraceSeconds);
    }

    [Fact]
    public void the_documented_default_policy_is_coalesce()
    {
        // Stated in the policy table and in the release notes; a reordered enum would break both.
        default(MissedRunPolicy).Should().Be(MissedRunPolicy.Coalesce);
    }
}
