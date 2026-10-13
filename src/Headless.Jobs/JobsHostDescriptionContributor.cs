// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text.Json;
using Headless.Checks;
using Headless.Hosting;
using Headless.Reliability;

namespace Headless.Jobs;

/// <summary>
/// Writes the <c>jobs</c> section of the host description: every job function this host registered, read from the
/// host's frozen registry, so the description shows the tuning and filter the scheduler actually runs with.
/// </summary>
/// <remarks>
/// Cron-only settings are omitted for a time job instead of being written as <see langword="null"/>, and nothing about
/// the persistence provider or its connection is written: the description is meant to be printed and shared.
/// </remarks>
/// <param name="registry">The host's frozen job registry.</param>
/// <param name="schedulerOptions">The scheduler options whose cron defaults seed an undeclared policy.</param>
/// <param name="runsJobs">
/// Whether this host runs background services. A host without them claims nothing, so no function runs here even when
/// the run filter admits it.
/// </param>
internal sealed class JobsHostDescriptionContributor(
    JobFunctionRegistry registry,
    SchedulerOptionsBuilder schedulerOptions,
    bool runsJobs
) : IHostDescriptionContributor
{
    private readonly JobFunctionRegistry _registry = Argument.IsNotNull(registry);
    private readonly SchedulerOptionsBuilder _schedulerOptions = Argument.IsNotNull(schedulerOptions);

    public string SectionName => "jobs";

    public void WriteSection(Utf8JsonWriter writer)
    {
        Argument.IsNotNull(writer);

        writer.WriteStartObject();
        writer.WriteStartArray("functions");

        foreach (var name in _registry.Functions.Keys.Order(StringComparer.Ordinal))
        {
            _WriteFunction(writer, name, _registry.Functions[name]);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private void _WriteFunction(Utf8JsonWriter writer, string name, JobFunctionRegistration function)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);

        if (_registry.Descriptors.TryGetValue(name, out var descriptor))
        {
            writer.WriteString("contractVersion", descriptor.ContractVersion);
        }

        // An empty expression is how the registry marks a job that runs only when scheduled, never on a cadence.
        var isCron = !string.IsNullOrEmpty(function.CronExpression);
        writer.WriteString("kind", isCron ? "cron" : "time");

        if (isCron)
        {
            writer.WriteString("cronExpression", function.CronExpression);
        }

        writer.WriteString("priority", _Priority(function.Priority));
        writer.WriteNumber("maxConcurrency", function.MaxConcurrency);
        writer.WriteNumber("clusterMaxConcurrency", function.ClusterMaxConcurrency);

        if (isCron)
        {
            // These seed the cron definition when it is first created; the scheduler default stands in for an
            // undeclared one, exactly as seeding resolves it.
            writer.WriteString(
                "missedRunPolicy",
                _MissedRunPolicy(function.OnMissedRun ?? _schedulerOptions.DefaultMissedRunPolicy)
            );
            writer.WriteNumber(
                "missedRunGraceSeconds",
                function.MissedRunGraceSeconds ?? _schedulerOptions.DefaultMissedRunGraceSeconds
            );
            writer.WriteString(
                "overlapPolicy",
                _OverlapPolicy(function.OnOverlap ?? _schedulerOptions.DefaultOverlapPolicy)
            );
        }

        writer.WritePropertyName("failurePolicy");
        _registry.GetFailurePolicy(name).WriteDescription(writer);

        var runsHere = runsJobs && _registry.RunFilter.Allows(name);
        writer.WriteString("runState", runsHere ? "runs-here" : "registered-not-run-here");

        writer.WriteEndObject();
    }

    // A value this binary does not name, such as a member added later, is written as its number rather than failing
    // the whole description.
    private static string _Priority(JobPriority priority) =>
        priority switch
        {
            JobPriority.Normal => "normal",
            JobPriority.High => "high",
            JobPriority.Low => "low",
            JobPriority.LongRunning => "long-running",
            _ => ((int)priority).ToString(CultureInfo.InvariantCulture),
        };

    private static string _MissedRunPolicy(MissedRunPolicy policy) =>
        policy switch
        {
            MissedRunPolicy.Coalesce => "coalesce",
            MissedRunPolicy.Skip => "skip",
            _ => ((int)policy).ToString(CultureInfo.InvariantCulture),
        };

    private static string _OverlapPolicy(CronOverlapPolicy policy) =>
        policy switch
        {
            CronOverlapPolicy.Allow => "allow",
            CronOverlapPolicy.Skip => "skip",
            _ => ((int)policy).ToString(CultureInfo.InvariantCulture),
        };
}
