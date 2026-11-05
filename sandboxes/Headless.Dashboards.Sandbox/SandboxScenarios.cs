// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Jobs;
using Headless.Messaging;

namespace Headless.Dashboards.Sandbox;

/// <summary>
/// Named fixtures that put the dashboards into the states a tester needs, each one call away:
/// <c>POST /sandbox/scenarios/{name}</c>, or <c>make seed SCENARIO=name</c>. Every call creates new rows, so a
/// scenario is safe to run again; durations are long enough to watch a state and short enough to finish in a test.
/// </summary>
public static class SandboxScenarios
{
    private static readonly JobOptions _NoRetries = new() { Retries = 0 };

    /// <summary>Every scenario name with what it creates, in the order <c>all</c> runs them.</summary>
    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(
        StringComparer.Ordinal
    )
    {
        ["progress-running"] = "Time job that reports progress for about 60 s (120 steps of 500 ms), then succeeds.",
        ["progress-quick"] = "Time job that reports progress for about 2 s, then succeeds at 100%.",
        ["progress-failing"] = "Time job that reports progress and fails at step 25 of 40 (62.5%), with no retries.",
        ["quiet"] = "Time job that runs 2 s without reporting progress.",
        ["failing"] = "Time job that fails at once without progress, with no retries: a row to requeue.",
        ["cron-progress"] = "Cron definition every 30 s whose occurrences report progress for about 10 s.",
        ["messages"] = "Ten published messages: eight consumed, two failing in the consumer.",
        ["scheduled-message"] = "One message published with a 10 minute delay: a row on the Scheduled page.",
        ["diagnostic-runs"] =
            "Six Headless dashboard diagnostic runs: two queued, one running, one succeeded, two failed with one error.",
        ["diagnostic-runs-long"] =
            "5,000 Headless dashboard diagnostic runs, enough to scroll the runs view through many pages: every 100th failed, the rest succeeded.",
    };

    public static async Task<IReadOnlyList<string>> RunAsync(
        string name,
        IJobScheduler jobs,
        IBus bus,
        DiagnosticsStore diagnostics,
        CancellationToken cancellationToken
    )
    {
        return name switch
        {
            "progress-running" =>
            [
                await _EnqueueProgressAsync(
                    jobs,
                    new(Steps: 120, StepDelayMs: 500, FailAtStep: null, Label: "import"),
                    cancellationToken
                ),
            ],
            "progress-quick" =>
            [
                await _EnqueueProgressAsync(
                    jobs,
                    new(Steps: 10, StepDelayMs: 200, FailAtStep: null, Label: "quick"),
                    cancellationToken
                ),
            ],
            "progress-failing" =>
            [
                await _EnqueueProgressAsync(
                    jobs,
                    new(Steps: 40, StepDelayMs: 250, FailAtStep: 25, Label: "export"),
                    cancellationToken
                ),
            ],
            "quiet" => [_Id(await jobs.EnqueueAsync<QuietJob>(cancellationToken))],
            "failing" => [_Id(await jobs.EnqueueAsync<AlwaysFailsJob>(_NoRetries, cancellationToken))],
            "cron-progress" =>
            [
                _Id(
                    await jobs.ScheduleRecurringAsync(
                        new ProgressJobArgs(Steps: 20, StepDelayMs: 500, FailAtStep: null, Label: "cron sweep"),
                        "*/30 * * * * *",
                        cancellationToken
                    )
                ),
            ],
            "messages" => await _PublishOrdersAsync(bus, cancellationToken),
            "scheduled-message" =>
            [
                _Id(
                    await bus.PublishAsync(
                        new SandboxOrderPlaced(OrderId: 999, ShouldFail: false),
                        new PublishOptions { Delay = TimeSpan.FromMinutes(10) },
                        cancellationToken
                    )
                ),
            ],
            "diagnostic-runs" => _AddDiagnosticRuns(diagnostics),
            "diagnostic-runs-long" => _AddLongDiagnosticRunList(diagnostics, count: 5_000),
            "all" => await _RunAllAsync(jobs, bus, diagnostics, cancellationToken),
            _ => throw new KeyNotFoundException(
                $"Unknown scenario '{name}'. Known: all, {string.Join(", ", Descriptions.Keys)}."
            ),
        };
    }

    private static async Task<IReadOnlyList<string>> _RunAllAsync(
        IJobScheduler jobs,
        IBus bus,
        DiagnosticsStore diagnostics,
        CancellationToken cancellationToken
    )
    {
        var created = new List<string>();
        foreach (var scenario in Descriptions.Keys)
        {
            created.AddRange(await RunAsync(scenario, jobs, bus, diagnostics, cancellationToken));
        }

        return created;
    }

    private static async Task<string> _EnqueueProgressAsync(
        IJobScheduler jobs,
        ProgressJobArgs args,
        CancellationToken cancellationToken
    )
    {
        return _Id(await jobs.EnqueueAsync(args, _NoRetries, cancellationToken));
    }

    private static async Task<IReadOnlyList<string>> _PublishOrdersAsync(IBus bus, CancellationToken cancellationToken)
    {
        var receipts = new List<string>();
        for (var orderId = 1; orderId <= 10; orderId++)
        {
            var receipt = await bus.PublishAsync(
                new SandboxOrderPlaced(orderId, ShouldFail: orderId % 5 == 0),
                cancellationToken
            );
            receipts.Add(_Id(receipt));
        }

        return receipts;
    }

    // Writes straight to the store, as a feature's own code would; the dashboard learns of the runs from its change
    // reads, so they reach an open view within the change-poll interval.
    private static IReadOnlyList<string> _AddDiagnosticRuns(DiagnosticsStore diagnostics)
    {
        var queued = new[] { diagnostics.AddRun(), diagnostics.AddRun() };
        var running = diagnostics.Start(diagnostics.AddRun().Id);
        var succeeded = diagnostics.Succeed(diagnostics.Start(diagnostics.AddRun().Id).Id);
        var failed = new[] { diagnostics.AddRun(), diagnostics.AddRun() }
            .Select(run => diagnostics.Fail(diagnostics.Start(run.Id).Id, "Upstream timed out"))
            .ToArray();

        return [.. queued.Select(run => run.Id), running.Id, succeeded.Id, .. failed.Select(run => run.Id)];
    }

    private static IReadOnlyList<string> _AddLongDiagnosticRunList(DiagnosticsStore diagnostics, int count)
    {
        var ids = new List<string>(count);

        for (var index = 1; index <= count; index++)
        {
            var started = diagnostics.Start(diagnostics.AddRun().Id);
            var run =
                index % 100 == 0
                    ? diagnostics.Fail(started.Id, "Disk quota exceeded")
                    : diagnostics.Succeed(started.Id);
            ids.Add(run.Id);
        }

        return ids;
    }

    private static string _Id(Guid id) => id.ToString("D");

    private static string _Id(PublishReceipt receipt) =>
        receipt.StorageId?.ToString("D") ?? receipt.MessageId ?? "(not stored)";
}
