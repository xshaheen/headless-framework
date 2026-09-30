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
#pragma warning disable IDE0060 // These are the documented examples verbatim; trimming a parameter stops pinning them.
    // The attribute example from "Configuring it".
    [Job("reports.nightly", Cron = "0 0 2 * * *", OnMissedRun = MissedRunPolicy.Skip, MissedRunGraceSeconds = 300)]
    private sealed class DocumentedJob : IJob
    {
        // The job-visible context example from "What an executing job sees".
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken)
        {
            if (context.IsRecoveryRun)
            {
                _ = context.RecoveredFromUtc!.Value;
                _ = context.Lateness;

                return ValueTask.CompletedTask;
            }

            _ = context.ScheduledFor;

            return ValueTask.CompletedTask;
        }
    }
#pragma warning restore IDE0060

    [Fact]
    public void the_documented_attribute_shape_compiles_and_round_trips()
    {
        var attribute = typeof(DocumentedJob)
            .GetCustomAttributes(typeof(JobAttribute), inherit: false)
            .Cast<JobAttribute>()
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
