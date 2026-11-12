// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Checks;
using Headless.Jobs;
using Headless.Messaging;
using Headless.Messaging.Persistence;

namespace Headless.Dashboards.Sandbox;

/// <summary>The services a scenario drives; the scenario endpoint binds each one from the container.</summary>
/// <param name="Jobs">Schedules and cancels the Jobs fixtures.</param>
/// <param name="CronJobs">Stores a cron definition with settings the scheduler's recurring API does not take.</param>
/// <param name="Bus">Publishes the Bus-lane messages.</param>
/// <param name="Queue">Enqueues the Queue-lane messages.</param>
/// <param name="Storage">The Messaging store, for the operator actions a scenario applies to a received row.</param>
/// <param name="Clock">Times the wait for a consumer to finish.</param>
public sealed record SandboxScenarioServices(
    IJobScheduler Jobs,
    ICronJobManager<CronJobEntity> CronJobs,
    IBus Bus,
    IQueue Queue,
    IDataStorage Storage,
    TimeProvider Clock
);

/// <summary>
/// Named fixtures that put the dashboards into the states a tester needs, each one call away:
/// <c>POST /sandbox/scenarios/{name}</c>, or <c>make seed SCENARIO=name</c>. Every call creates new rows, so a
/// scenario is safe to run again; durations are long enough to watch a state and short enough to finish in a test.
/// </summary>
public static class SandboxScenarios
{
    private static readonly JobOptions _NoRetries = new() { Retries = 0 };

    // In-process consumption finishes in milliseconds; the bound only keeps a stuck consumer from hanging the call.
    private static readonly TimeSpan _ConsumeTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan _ConsumePollInterval = TimeSpan.FromMilliseconds(50);

    // The store records who acted on a row, so the scenarios act as one fixed, recognizable operator.
    private static readonly OperatorAuthorizationContext _SeedOperator = new(
        new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "sandbox-seed")], "SandboxSeed"))
    );

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
        ["cancelled-before-start"] =
            "Time job scheduled an hour ahead and cancelled at once: a Cancelled row that never ran.",
        ["failing-timeout"] =
            "Time job that fails at once with a TimeoutException, with no retries: a second exception type on the Failed list.",
        ["messages"] = "Ten published messages: eight consumed, two failing in the consumer.",
        ["message-failures-by-cause"] =
            "Seven received messages that fail with two causes and a different order id in each: two Needs you groups.",
        ["scheduled-message"] = "One message published with a 10 minute delay: a row on the Scheduled view.",
        ["message-contracts"] =
            "One consumed message of each of the four declared contracts, two on the Bus lane and two on the Queue lane.",
        ["message-retrying"] =
            "Three Queue-lane messages that fail in sandbox.shipments with a retry pending: retried after a minute, then at doubling delays for over an hour.",
        ["message-held"] = "One consumed message that the operator sandbox-seed holds: a held row to release.",
        ["message-operator-log"] =
            "One failed message re-executed, and one consumed message held then released, by sandbox-seed: three kinds of entry in the operator log.",
        ["message-circuit-open"] =
            "Two messages that time out in sandbox.payments, whose circuit opens after two failures and stays open for 30 minutes.",
        ["message-backlog"] =
            "Five Queue-lane messages that sandbox.stock-counts takes 45 s to 165 s to handle: a received backlog that drains one row every 30 s.",
        ["message-publish-failed"] =
            "One message published with a delivery deadline that has passed by the time it is sent: a published row that failed for good.",
    };

    public static async Task<IReadOnlyList<string>> RunAsync(
        string name,
        SandboxScenarioServices services,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(services);
        var (jobs, cronJobs, bus, queue, _, _) = services;

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
            "cancelled-before-start" => [_Id(await _ScheduleThenCancelAsync(jobs, cancellationToken))],
            "failing-timeout" => [_Id(await jobs.EnqueueAsync<TimesOutJob>(_NoRetries, cancellationToken))],
            "messages" => await _PublishOrdersAsync(bus, cancellationToken),
            "message-failures-by-cause" => await _PublishFailuresByCauseAsync(bus, cancellationToken),
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
            "message-contracts" => await _PublishEachContractAsync(bus, queue, cancellationToken),
            "message-retrying" => await _EnqueueFailingShipmentsAsync(queue, cancellationToken),
            "message-held" => [await _HoldConsumedOrderAsync(services, cancellationToken)],
            "message-operator-log" => await _RecordOperatorActionsAsync(services, cancellationToken),
            "message-circuit-open" => await _TripPaymentsCircuitAsync(bus, cancellationToken),
            "message-backlog" => await _EnqueueStockCountsAsync(queue, cancellationToken),
            "message-publish-failed" =>
            [
                _Id(
                    await bus.PublishAsync(
                        new SandboxOrderPlaced(OrderId: 9404, ShouldFail: false),
                        // The shortest deadline there is: it has passed by the time the stored row is dispatched, so
                        // the row ends Failed without a send. The in-memory transport never fails a send on its own.
                        new PublishOptions { DeliverWithin = TimeSpan.FromTicks(1) },
                        cancellationToken
                    )
                ),
            ],
            "all" => await _RunAllAsync(services, cancellationToken),
            _ => throw new KeyNotFoundException(
                $"Unknown scenario '{name}'. Known: all, {string.Join(", ", Descriptions.Keys)}."
            ),
        };
    }

    private static async Task<IReadOnlyList<string>> _RunAllAsync(
        SandboxScenarioServices services,
        CancellationToken cancellationToken
    )
    {
        var created = new List<string>();
        foreach (var scenario in Descriptions.Keys)
        {
            created.AddRange(await RunAsync(scenario, services, cancellationToken));
        }

        return created;
    }

    // An hour ahead, so the job is still waiting when the cancel lands whatever the scheduler is doing.
    private static async Task<Guid> _ScheduleThenCancelAsync(IJobScheduler jobs, CancellationToken cancellationToken)
    {
        var id = await jobs.ScheduleAfterAsync<QuietJob>(TimeSpan.FromHours(1), _NoRetries, cancellationToken);
        await jobs.CancelAsync(id, cancellationToken);

        return id;
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

    // Four declined payments and three warehouse timeouts: the order id differs in every message, so the fingerprint
    // folds each cause into one group while the two causes stay apart.
    private static async Task<IReadOnlyList<string>> _PublishFailuresByCauseAsync(
        IBus bus,
        CancellationToken cancellationToken
    )
    {
        var receipts = new List<string>();
        foreach (var orderId in new[] { 7101, 7102, 7135, 7240 })
        {
            var message = new SandboxOrderPlaced(orderId, ShouldFail: true, "payment was declined");
            receipts.Add(_Id(await bus.PublishAsync(message, cancellationToken)));
        }

        foreach (var orderId in new[] { 8301, 8317, 8452 })
        {
            var message = new SandboxOrderPlaced(orderId, ShouldFail: true, SandboxOrderPlaced.StockTimeoutCause);
            receipts.Add(_Id(await bus.PublishAsync(message, cancellationToken)));
        }

        return receipts;
    }

    private static async Task<IReadOnlyList<string>> _PublishEachContractAsync(
        IBus bus,
        IQueue queue,
        CancellationToken cancellationToken
    )
    {
        return
        [
            _Id(await bus.PublishAsync(new SandboxOrderPlaced(OrderId: 2001, ShouldFail: false), cancellationToken)),
            _Id(
                await bus.PublishAsync(
                    new SandboxPaymentCaptured(PaymentId: 2002, ShouldFail: false),
                    cancellationToken
                )
            ),
            _Id(
                await queue.EnqueueAsync(
                    new SandboxShipmentRequested(ShipmentId: 2003, ShouldFail: false),
                    cancellationToken
                )
            ),
            _Id(await queue.EnqueueAsync(new SandboxStockCounted(Sku: 2004, CountSeconds: 0), cancellationToken)),
        ];
    }

    private static async Task<IReadOnlyList<string>> _EnqueueFailingShipmentsAsync(
        IQueue queue,
        CancellationToken cancellationToken
    )
    {
        var receipts = new List<string>();
        foreach (var shipmentId in new[] { 3101, 3102, 3107 })
        {
            var message = new SandboxShipmentRequested(shipmentId, ShouldFail: true);
            receipts.Add(_Id(await queue.EnqueueAsync(message, cancellationToken)));
        }

        return receipts;
    }

    private static async Task<string> _HoldConsumedOrderAsync(
        SandboxScenarioServices services,
        CancellationToken cancellationToken
    )
    {
        var order = new SandboxOrderPlaced(OrderId: 6001, ShouldFail: false);
        var receipt = await services.Bus.PublishAsync(order, cancellationToken);
        var generation = await _WaitUntilConsumedAsync(services, receipt, cancellationToken);
        var inbox = services.Storage.GetInboxOperationsApi();
        await _ApplyAsync(
            inbox.HoldAsync,
            generation,
            "Kept as evidence for the audit of order 6001.",
            cancellationToken
        );

        return _Id(receipt);
    }

    // The held row's scenario writes one kind of receipt. A re-execution and a release add two more, so the operator
    // log has operation types to tell apart and filter by.
    private static async Task<IReadOnlyList<string>> _RecordOperatorActionsAsync(
        SandboxScenarioServices services,
        CancellationToken cancellationToken
    )
    {
        var inbox = services.Storage.GetInboxOperationsApi();

        var failing = new SandboxOrderPlaced(OrderId: 6101, ShouldFail: true, "was rejected by the fraud check");
        var failed = await services.Bus.PublishAsync(failing, cancellationToken);
        var failedGeneration = await _WaitUntilConsumedAsync(services, failed, cancellationToken);
        await _ApplyAsync(
            inbox.ForceReprocessAsync,
            failedGeneration,
            "Fraud check rule fixed; running order 6101 again.",
            cancellationToken
        );

        var consumed = await services.Bus.PublishAsync(
            new SandboxOrderPlaced(OrderId: 6102, ShouldFail: false),
            cancellationToken
        );
        var consumedGeneration = await _WaitUntilConsumedAsync(services, consumed, cancellationToken);
        await _ApplyAsync(
            inbox.HoldAsync,
            consumedGeneration,
            "Kept while the refund of order 6102 is open.",
            cancellationToken
        );
        await _ApplyAsync(
            inbox.ReleaseHoldAsync,
            consumedGeneration,
            "Refund of order 6102 closed.",
            cancellationToken
        );

        return [_Id(failed), _Id(consumed)];
    }

    // Exactly the consumer's failure threshold, so every run stores the same two failed rows: a third message would
    // race the pause and be stored on some runs only.
    private static async Task<IReadOnlyList<string>> _TripPaymentsCircuitAsync(
        IBus bus,
        CancellationToken cancellationToken
    )
    {
        var receipts = new List<string>();
        foreach (var paymentId in new[] { 5201, 5202 })
        {
            var message = new SandboxPaymentCaptured(paymentId, ShouldFail: true);
            receipts.Add(_Id(await bus.PublishAsync(message, cancellationToken)));
        }

        return receipts;
    }

    // Staggered durations, so the backlog count steps down while a tester watches instead of emptying at once.
    private static async Task<IReadOnlyList<string>> _EnqueueStockCountsAsync(
        IQueue queue,
        CancellationToken cancellationToken
    )
    {
        var receipts = new List<string>();
        for (var index = 0; index < 5; index++)
        {
            var message = new SandboxStockCounted(Sku: 4001 + index, CountSeconds: 45 + (30 * index));
            receipts.Add(_Id(await queue.EnqueueAsync(message, cancellationToken)));
        }

        return receipts;
    }

    /// <summary>
    /// Waits until <see cref="SandboxOrderPlacedConsumer"/> has finished the published order, because an operator
    /// action is refused on a row that is still being processed.
    /// </summary>
    /// <exception cref="TimeoutException">The consumer did not finish the message in time.</exception>
    private static async Task<InboxGenerationView> _WaitUntilConsumedAsync(
        SandboxScenarioServices services,
        PublishReceipt receipt,
        CancellationToken cancellationToken
    )
    {
        var inbox = services.Storage.GetInboxOperationsApi();
        var query = new InboxGenerationQuery
        {
            MessageId = Ensure.NotNull(receipt.MessageId),
            ConsumerIdentity = SandboxConsumerIds.Orders,
        };
        var started = services.Clock.GetTimestamp();

        while (true)
        {
            var generations = await inbox.QueryAsync(query, _SeedOperator, cancellationToken);
            var finished = generations.Items.FirstOrDefault(static generation =>
                generation is { IsCurrentGeneration: true, TerminalAt: not null }
            );

            if (finished is not null)
            {
                return finished;
            }

            if (services.Clock.GetElapsedTime(started) > _ConsumeTimeout)
            {
                throw new TimeoutException(
                    $"{SandboxConsumerIds.Orders} did not finish message {query.MessageId} within {_ConsumeTimeout}."
                );
            }

            await Task.Delay(_ConsumePollInterval, services.Clock, cancellationToken);
        }
    }

    /// <summary>Applies one inbox operation as the seed operator, the way the dashboard's command applies it.</summary>
    /// <exception cref="InvalidOperationException">The store refused the operation.</exception>
    private static async Task _ApplyAsync(
        Func<InboxOperationRequest, CancellationToken, ValueTask<InboxOperationResult>> operation,
        InboxGenerationView generation,
        string reason,
        CancellationToken cancellationToken
    )
    {
        var request = new InboxOperationRequest(
            Guid.CreateVersion7(),
            generation.IncarnationId,
            generation.Status,
            reason,
            _SeedOperator
        );
        var result = await operation(request, cancellationToken);

        if (result.Outcome != InboxOperationOutcome.Applied)
        {
            throw new InvalidOperationException(
                $"The store refused {result.OperationType} on message {generation.MessageId}: {result.Outcome}."
            );
        }
    }

    private static string _Id(Guid id) => id.ToString("D");

    private static string _Id(PublishReceipt receipt) =>
        receipt.StorageId?.ToString("D") ?? receipt.MessageId ?? "(not stored)";
}
