// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
        ["failures-by-id"] =
            "Five time jobs that fail with the same cause and a different order id in each message: one Needs you item.",
        ["chain-failed-middle"] =
            "Three-step chain whose second step fails: the first succeeds and the third never runs.",
        ["cron-no-overlap-failing"] =
            "Cron definition every 10 s under the skip overlap policy whose occurrences run 15 s, then fail: skipped and failed occurrences.",
        ["cron-paused"] = "Cron definition every 5 minutes, paused as soon as it is created.",
        ["cancel-stubborn"] =
            "Time job that reports progress for 5 minutes and ignores its cancellation token: Cancel requests a stop it never honors.",
        ["messages"] = "Ten published messages: eight consumed, two failing in the consumer.",
        ["scheduled-message"] = "One message published with a 10 minute delay: a row on the Scheduled page.",
    };

    public static async Task<IReadOnlyList<string>> RunAsync(
        string name,
        IJobScheduler jobs,
        ICronJobManager<CronJobEntity> cronJobs,
        IBus bus,
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
            "failures-by-id" => await _EnqueueOrderSyncFailuresAsync(jobs, cancellationToken),
            "chain-failed-middle" => [_Id(await jobs.EnqueueAsync(_FailedMiddleChain(), cancellationToken))],
            "cron-no-overlap-failing" => [_Id(await _AddNoOverlapFailingCronAsync(cronJobs, cancellationToken))],
            "cron-paused" => [_Id(await _AddPausedCronAsync(jobs, cancellationToken))],
            "cancel-stubborn" => [_Id(await jobs.EnqueueAsync<StubbornJob>(_NoRetries, cancellationToken))],
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
            "all" => await _RunAllAsync(jobs, cronJobs, bus, cancellationToken),
            _ => throw new KeyNotFoundException(
                $"Unknown scenario '{name}'. Known: all, {string.Join(", ", Descriptions.Keys)}."
            ),
        };
    }

    private static async Task<IReadOnlyList<string>> _RunAllAsync(
        IJobScheduler jobs,
        ICronJobManager<CronJobEntity> cronJobs,
        IBus bus,
        CancellationToken cancellationToken
    )
    {
        var created = new List<string>();
        foreach (var scenario in Descriptions.Keys)
        {
            created.AddRange(await RunAsync(scenario, jobs, cronJobs, bus, cancellationToken));
        }

        return created;
    }

    private static async Task<IReadOnlyList<string>> _EnqueueOrderSyncFailuresAsync(
        IJobScheduler jobs,
        CancellationToken cancellationToken
    )
    {
        var ids = new List<string>();
        foreach (var orderId in new[] { 4711, 4712, 4719, 4802, 5120 })
        {
            ids.Add(_Id(await jobs.EnqueueAsync(new OrderSyncArgs(orderId), _NoRetries, cancellationToken)));
        }

        return ids;
    }

    private static JobChain _FailedMiddleChain()
    {
        var chain = JobChain.Start(new ChainStepArgs("reserve stock", Fails: false), _NoRetries);
        chain
            .Root.Then(new ChainStepArgs("charge card", Fails: true), _NoRetries)
            .Then(new ChainStepArgs("ship order", Fails: false), _NoRetries);

        return chain.Build();
    }

    // The scheduler's recurring API takes no overlap policy, so the definition goes through the cron job manager, which
    // stores the one the entity carries.
    private static async Task<Guid> _AddNoOverlapFailingCronAsync(
        ICronJobManager<CronJobEntity> cronJobs,
        CancellationToken cancellationToken
    )
    {
        var definition = await cronJobs.AddAsync(
            new CronJobEntity
            {
                Function = SandboxJobIds.OverlapFails,
                Expression = "*/10 * * * * *",
                OnOverlap = CronOverlapPolicy.Skip,
                Retries = 0,
                Description = "Ledger export: runs longer than its interval and fails.",
            },
            cancellationToken
        );

        return definition.Id;
    }

    private static async Task<Guid> _AddPausedCronAsync(IJobScheduler jobs, CancellationToken cancellationToken)
    {
        var id = await jobs.ScheduleRecurringAsync<QuietJob>("0 */5 * * * *", cancellationToken);
        await jobs.PauseCronAsync(id, cancellationToken);

        return id;
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

    private static string _Id(Guid id) => id.ToString("D");

    private static string _Id(PublishReceipt receipt) =>
        receipt.StorageId?.ToString("D") ?? receipt.MessageId ?? "(not stored)";
}
