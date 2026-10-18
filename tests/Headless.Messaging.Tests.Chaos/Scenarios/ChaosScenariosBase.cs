// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Threading;
using Polly;
using Polly.Retry;
using Tests.Ledger;
using Tests.Stack;

namespace Tests.Scenarios;

/// <summary>
/// The multi-host stress scenarios, each judged on a count against a stated threshold: several hosts on one database
/// and one broker, real faults injected through Toxiproxy and host restarts, and a ledger of every publish, send, and
/// handler run across the cluster. A leaf runs the whole set against one database.
/// </summary>
/// <remarks>
/// Sizes and fault timings come from <see cref="ChaosProfile.Current"/>: a smoke profile for a local check, or the
/// full sizes for the scheduled run. Every scenario writes its counts to the run summary, pass or fail.
/// </remarks>
public abstract class ChaosScenariosBase(ChaosStackFixture stack) : MultiHostGuaranteeSuiteBase
{
    /// <summary>How long a scenario keeps watching after its last expected run, so a late duplicate is still counted.</summary>
    private static readonly TimeSpan _QuietWindow = TimeSpan.FromSeconds(5);

    /// <summary>How long a scenario waits for the stack to finish its work once the faults are over.</summary>
    private static readonly TimeSpan _Recovery = TimeSpan.FromMinutes(3);

    /// <summary>
    /// The share of publishes that must be accepted while storage stays up. A publish then only has to write the
    /// outbox, so most must succeed; the floor stays loose until a nightly run has measured the real rejection rate.
    /// </summary>
    private const double _StorageUpAcceptanceShare = 0.5;

    private static ChaosProfile Profile => ChaosProfile.Current;

    [Fact]
    public override async Task should_run_each_retry_row_once_when_several_hosts_drain_one_backlog()
    {
        // given — every first attempt fails, so each row waits in the retry backlog for one retry run
        var size = Profile.RetryBacklog;
        await using var run = _NewRun(settings =>
        {
            settings.RetryBatchSize = size.RetryBatchSize;
            settings.WorkFailurePolicy = policy =>
                policy.Immediate(0).Delayed(1, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            // The failures that fill the backlog are deliberate, and every first attempt fails, so a breaker would
            // open on them and probe only with first attempts that fail again: the scenario measures retry claims,
            // not the breaker, which its own guarantees cover.
            settings.WorkCircuitBreaker = circuitBreaker => circuitBreaker.Enabled = false;
        });

        for (var i = 0; i < size.Hosts; i++)
        {
            await run.StartHostAsync(
                run.Behavior($"host-{i}", size.MinWork, size.MaxWork, failFirst: true),
                cancellationToken: AbortToken
            );
        }

        // when
        await run.PublishBacklogAsync(run.Hosts[0], size.Rows, AbortToken);
        var drainBudget = TimeSpan.FromTicks(size.Rows * size.MaxWork.Ticks / size.Hosts) + _Recovery;
        var drained = await run.Ledger.WaitUntilAsync(
            progress => progress.Accepted == size.Rows && progress.AllAcceptedCompleted,
            drainBudget,
            AbortToken
        );
        await Task.Delay(_QuietWindow, AbortToken);

        // then
        var snapshot = run.Ledger.Snapshot();
        var verdict = LedgerVerdict.Evaluate(snapshot);
        var succeeded = await run.CountReceivedAsync(ChaosModule.WorkName, StatusName.Succeeded, AbortToken);
        var retriedOtherThanOnce = snapshot
            .Accepted.Where(entry => entry.Started != 2)
            .Select(entry => entry.Key)
            .ToList();

        List<string> violations = [.. verdict.ExactlyOnceViolations()];
        _AddIf(violations, !drained, $"the backlog did not drain within {drainBudget}");
        _AddIf(
            violations,
            retriedOtherThanOnce.Count > 0,
            $"{retriedOtherThanOnce.Count} row(s) did not run exactly once from the retry backlog: {_Sample(retriedOtherThanOnce)}"
        );
        _AddIf(violations, succeeded != size.Rows, $"{succeeded} of {size.Rows} received rows ended Succeeded");

        _Report(
            "retry backlog across hosts",
            violations,
            $"rows {size.Rows}, hosts {size.Hosts}; {verdict.Describe()}; retry runs by host {_ByHost(snapshot.Accepted.SelectMany(entry => entry.CompletedOn))}"
        );
        violations.Should().BeEmpty();
    }

    [Fact]
    public override async Task should_send_each_delayed_row_once_while_hosts_restart_in_turn()
    {
        // given
        var size = Profile.RollingRestart;
        await using var run = _NewRun(settings => settings.DelayedPollInterval = size.PollInterval);
        var live = new ChaosHost[size.Hosts];

        for (var i = 0; i < size.Hosts; i++)
        {
            live[i] = await run.StartHostAsync(run.Behavior($"host-{i}.0"), cancellationToken: AbortToken);
        }

        // when — delayed rows come due across the spread while one host after another restarts
        var lead = TimeSpan.FromSeconds(5);
        await _ForEachBoundedAsync(
            size.Rows,
            async index =>
            {
                var delay = lead + (size.Spread * index / size.Rows);
                await run.PublishWorkAsync(live[index % live.Length], run.Key(index), delay, AbortToken);
            }
        );

        var restarts = 0;
        bool sent;
        using (var restarting = CancellationTokenSource.CreateLinkedTokenSource(AbortToken))
        {
            var rolling = _RollAsync(run, live, size.RestartEvery, () => restarts++, restarting.Token);
            sent = await run.Ledger.WaitUntilAsync(
                progress => progress.AllAcceptedCompleted,
                lead + size.Spread + _Recovery,
                AbortToken
            );
            await restarting.CancelAsync();
            await _IgnoreCancellationAsync(rolling);
        }

        await Task.Delay(_QuietWindow, AbortToken);

        foreach (var host in live)
        {
            await host.StopAsync();
        }

        // then
        var snapshot = run.Ledger.Snapshot();
        var verdict = LedgerVerdict.Evaluate(snapshot);
        var notSentOnce = snapshot.Accepted.Where(entry => entry.SuccessfulSends != 1).ToList();
        var leased = await run.FindLeasedPublishedRowsAsync(ChaosModule.WorkName, AbortToken);
        var lateness = snapshot
            .Accepted.Where(entry => entry is { DueAt: not null, FirstSuccessfulSendAt: not null })
            .Select(entry => entry.FirstSuccessfulSendAt!.Value - entry.DueAt!.Value)
            .Order()
            .ToList();
        var p99 = _Percentile(lateness, 0.99);
        var latenessLimit = size.PollInterval * 2;

        List<string> violations = [.. verdict.ExactlyOnceViolations()];
        _AddIf(violations, !sent, $"not every delayed row ran within {lead + size.Spread + _Recovery}");
        _AddIf(
            violations,
            notSentOnce.Count > 0,
            $"{notSentOnce.Count} row(s) were not sent to the broker exactly once: {_Sample(notSentOnce.ConvertAll(entry => $"{entry.Key} x{entry.SuccessfulSends}"))}"
        );
        _AddIf(violations, leased.Count > 0, $"{leased.Count} row(s) stayed leased after the last host stopped");
        _AddIf(
            violations,
            p99 >= latenessLimit,
            $"p99 lateness {_Seconds(p99)} is not under {_Seconds(latenessLimit)}"
        );

        _Report(
            "delayed rows during rolling restarts",
            violations,
            $"rows {size.Rows}, restarts {restarts}, sends {snapshot.Accepted.Sum(entry => entry.SuccessfulSends)}, leased after stop {leased.Count}, lateness p50 {_Seconds(_Percentile(lateness, 0.5))} p99 {_Seconds(p99)} max {_Seconds(lateness.LastOrDefault())}; {verdict.Describe()}"
        );
        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task should_follow_the_back_off_and_lose_nothing_when_the_broker_browns_out()
    {
        // given — no jitter, so every inline retry waits at least the configured delay
        var size = Profile.BrownOut;
        var strategy = new RetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = false,
            ShouldHandle = RetryPolicyOptions.DefaultShouldHandle,
        };
        const int maxPersistedRetries = 15;
        await using var run = _NewRun(settings =>
        {
            settings.BrokerThroughProxy = true;
            settings.ConfigureMessaging = setup =>
            {
                setup.Options.RetryPolicy.RetryStrategy = strategy;
                setup.Options.RetryPolicy.MaxPersistedRetries = maxPersistedRetries;
            };
        });
        var publisher = await run.StartHostAsync(run.Behavior("host-0"), cancellationToken: AbortToken);
        await run.StartHostAsync(run.Behavior("host-1"), cancellationToken: AbortToken);

        // when — the broker stops answering while messages keep being published
        await stack.BlackholeBrokerAsync(AbortToken);
        var publishing = run.PublishAtRateAsync(
            (int)Math.Ceiling(size.Messages / size.Outage.TotalSeconds),
            size.Outage,
            _ => publisher,
            AbortToken
        );
        await Task.Delay(size.Outage, AbortToken);
        await stack.RestoreBrokerAnswersAsync(AbortToken);
        var attempted = await publishing;

        var recovered = await run.Ledger.WaitUntilAsync(
            progress => progress.AllAcceptedCompleted,
            _Recovery,
            AbortToken
        );
        await Task.Delay(_QuietWindow, AbortToken);

        // then
        var snapshot = run.Ledger.Snapshot();
        var verdict = LedgerVerdict.Evaluate(snapshot);
        var budget = (strategy.MaxRetryAttempts + 1) * (maxPersistedRetries + 1);
        var overBudget = snapshot
            .Accepted.Where(entry => entry.Sends.Count > budget)
            .Select(entry => entry.Key)
            .ToList();
        var gaps = snapshot
            .Accepted.SelectMany(entry => entry.Sends.Zip(entry.Sends.Skip(1), (first, next) => next.At - first.At))
            .ToList();
        var shortestGap = gaps.Count == 0 ? TimeSpan.Zero : gaps.Min();
        // A send that returns later than the retry delay can only be a back-off; the tolerance covers timer skew.
        var floor = strategy.Delay - TimeSpan.FromMilliseconds(100);

        List<string> violations =
        [
            .. verdict.AcceptanceViolations(attempted, _StorageUpAcceptanceShare),
            .. verdict.LossViolations(),
        ];
        _AddIf(
            violations,
            !recovered,
            $"not every accepted message ran within {_Recovery} after the broker answered again"
        );
        _AddIf(
            violations,
            overBudget.Count > 0,
            $"{overBudget.Count} row(s) were sent more than the {budget} attempts the publish budget allows: {_Sample(overBudget)}"
        );
        _AddIf(
            violations,
            gaps.Count > 0 && shortestGap < floor,
            $"two sends of one row were {_Seconds(shortestGap)} apart, under the {_Seconds(strategy.Delay)} retry delay"
        );

        _Report(
            "broker brown-out",
            violations,
            $"outage {_Seconds(size.Outage)}, publishes attempted {attempted}, send attempts {snapshot.Accepted.Sum(entry => entry.Sends.Count)}, max per row {snapshot.Accepted.Select(entry => entry.Sends.Count).DefaultIfEmpty().Max()}, shortest retry gap {_Seconds(shortestGap)}, duplicate sends {snapshot.Accepted.Sum(entry => Math.Max(0, entry.SuccessfulSends - 1))}; {verdict.Describe()}"
        );
        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task should_lose_and_dead_letter_nothing_when_storage_flaps_under_load()
    {
        // given
        var size = Profile.StorageFlap;
        await using var run = _NewRun(settings =>
        {
            settings.StorageThroughProxy = true;
            settings.DeliveryLimit = size.DeliveryLimit;
            settings.WorkConcurrency = 32;
        });
        ChaosHost[] hosts =
        [
            await run.StartHostAsync(run.Behavior("host-0"), cancellationToken: AbortToken),
            await run.StartHostAsync(run.Behavior("host-1"), cancellationToken: AbortToken),
        ];

        // when — storage drops out on a fixed cadence while load keeps arriving
        var flaps = 0;
        int attempted;
        using (var flapping = CancellationTokenSource.CreateLinkedTokenSource(AbortToken))
        {
            var flapper = _FlapStorageAsync(size, () => flaps++, flapping.Token);
            attempted = await run.PublishAtRateAsync(
                size.MessagesPerSecond,
                size.Duration,
                index => hosts[index % hosts.Length],
                AbortToken
            );
            await flapping.CancelAsync();
            await _IgnoreCancellationAsync(flapper);
        }

        await stack.StorageProxy.EnableAsync(AbortToken);
        var recovered = await run.Ledger.WaitUntilAsync(
            progress => progress.AllAcceptedCompleted,
            _Recovery,
            AbortToken
        );
        await Task.Delay(_QuietWindow, AbortToken);

        // then
        var verdict = LedgerVerdict.Evaluate(run.Ledger.Snapshot());
        var deadLettered = await stack.CountQueuedAsync(run.WorkDeadLetterQueue, AbortToken);
        var failedRows = await run.CountReceivedAsync(ChaosModule.WorkName, StatusName.Failed, AbortToken);

        // Storage refuses publishes while it is down, so only a run that accepted nothing fails the floor.
        List<string> violations =
        [
            .. verdict.AcceptanceViolations(attempted, minimumShare: 0),
            .. verdict.NoLossViolations(maxDuplicateRatio: 0.01),
        ];
        _AddIf(
            violations,
            !recovered,
            $"not every accepted message ran within {_Recovery} after storage stopped flapping"
        );
        _AddIf(violations, deadLettered > 0, $"{deadLettered} message(s) were dead-lettered by the broker");
        _AddIf(violations, failedRows > 0, $"{failedRows} received row(s) ended Failed");

        _Report(
            "storage flap under load",
            violations,
            $"rate {size.MessagesPerSecond}/s for {_Seconds(size.Duration)}, publishes attempted {attempted}, flaps {flaps}, dead-lettered {deadLettered}, failed rows {failedRows}; {verdict.Describe()}"
        );
        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task should_resume_consuming_within_the_reconnect_back_off_when_the_broker_link_churns()
    {
        // given
        var size = Profile.ConnectionChurn;
        await using var run = _NewRun(settings => settings.BrokerThroughProxy = true);
        ChaosHost[] hosts =
        [
            await run.StartHostAsync(run.Behavior("host-0"), cancellationToken: AbortToken),
            await run.StartHostAsync(run.Behavior("host-1"), cancellationToken: AbortToken),
        ];

        // when — the broker link drops on a fixed cadence under a steady load
        var drops = 0;
        int attempted;
        using (var churning = CancellationTokenSource.CreateLinkedTokenSource(AbortToken))
        {
            var churner = _ChurnBrokerAsync(size.DropEvery, () => drops++, churning.Token);
            attempted = await run.PublishAtRateAsync(
                size.MessagesPerSecond,
                size.Duration,
                index => hosts[index % hosts.Length],
                AbortToken
            );
            await churning.CancelAsync();
            await _IgnoreCancellationAsync(churner);
        }

        var recovered = await run.Ledger.WaitUntilAsync(
            progress => progress.AllAcceptedCompleted,
            _Recovery,
            AbortToken
        );
        await Task.Delay(_QuietWindow, AbortToken);

        // then
        var snapshot = run.Ledger.Snapshot();
        var verdict = LedgerVerdict.Evaluate(snapshot);
        var completions = snapshot.Completions.Order().ToList();
        var longestGap = completions.Zip(completions.Skip(1), (first, next) => next - first).DefaultIfEmpty().Max();
        var limit = ReconnectBackoff.Ceiling + TimeSpan.FromSeconds(5);

        List<string> violations =
        [
            .. verdict.AcceptanceViolations(attempted, _StorageUpAcceptanceShare),
            .. verdict.LossViolations(),
        ];
        _AddIf(violations, !recovered, $"not every accepted message ran within {_Recovery} after the churn stopped");
        _AddIf(
            violations,
            longestGap > limit,
            $"consumption stopped for {_Seconds(longestGap)}, longer than the reconnect back-off plus 5s ({_Seconds(limit)})"
        );

        _Report(
            "connection churn",
            violations,
            $"rate {size.MessagesPerSecond}/s for {_Seconds(size.Duration)}, publishes attempted {attempted}, drops {drops}, longest consumption gap {_Seconds(longestGap)}; {verdict.Describe()}"
        );
        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task should_run_one_handler_and_send_one_reply_when_one_message_reaches_many_consumers()
    {
        // given
        var size = Profile.FanIn;
        await using var run = _NewRun(settings => settings.ClientsPerHost = size.ClientsPerHost);
        var hosts = new List<ChaosHost>();

        for (var i = 0; i < size.Hosts; i++)
        {
            hosts.Add(await run.StartHostAsync(run.Behavior($"host-{i}"), cancellationToken: AbortToken));
        }

        // when — every round sends copies of one message ID straight to the broker, past any outbox
        var answered = true;

        for (var round = 0; round < size.Rounds; round++)
        {
            var key = run.Key(round);

            await Task.WhenAll(
                Enumerable
                    .Range(0, size.Copies)
                    .Select(copy =>
                        hosts[copy % hosts.Count]
                            .Queue.EnqueueAsync(
                                new ChaosRequest(key),
                                new QueueOptions
                                {
                                    MessageName = ChaosModule.RequestName,
                                    MessageId = key,
                                    DeliveryMode = DeliveryMode.Direct,
                                },
                                AbortToken
                            )
                    )
            );
            run.Ledger.RecordPublished(key, hosts[0].Name);

            var expected = round + 1;
            answered &= await run.Ledger.WaitUntilAsync(
                progress => progress.Replies >= expected && progress.AcceptedCompleted >= expected,
                TimeSpan.FromSeconds(60),
                AbortToken
            );
        }

        await Task.Delay(_QuietWindow, AbortToken);

        // then
        var snapshot = run.Ledger.Snapshot();
        var verdict = LedgerVerdict.Evaluate(snapshot);
        var poisonRows = await run.CountReceivedAsync(ChaosModule.RequestName, StatusName.Failed, AbortToken);
        var notOnce = snapshot
            .Accepted.Where(entry => entry is not { Started: 1, Completed: 1, Replies: 1 })
            .Select(entry =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{entry.Key} (runs {entry.Started}, replies {entry.Replies})"
                )
            )
            .ToList();

        List<string> violations = [.. verdict.ExactlyOnceViolations()];
        _AddIf(violations, !answered, "a round got no reply within 60s");
        _AddIf(
            violations,
            notOnce.Count > 0,
            $"{notOnce.Count} message(s) did not run and reply exactly once: {_Sample(notOnce)}"
        );
        _AddIf(violations, poisonRows > 0, $"{poisonRows} poison row(s) were stored");

        _Report(
            "one message to many consumers",
            violations,
            $"rounds {size.Rounds}, copies {size.Copies}, hosts {size.Hosts} x {size.ClientsPerHost} clients, handler runs {snapshot.Accepted.Sum(entry => entry.Started)}, replies {snapshot.Accepted.Sum(entry => entry.Replies)}, poison rows {poisonRows}"
        );
        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task should_finish_a_backlog_on_the_next_host_without_running_a_side_effect_twice()
    {
        // given
        var size = Profile.ShutdownBacklog;
        await using var run = _NewRun(settings =>
        {
            settings.ShutdownTimeout = size.StopBudget;
            settings.WorkConcurrency = 8;
        });
        var first = await run.StartHostAsync(
            run.Behavior("host-a", size.MinWork, size.MaxWork),
            cancellationToken: AbortToken
        );

        // when — the host stops with part of the backlog done and the rest in flight or queued
        await run.PublishBacklogAsync(first, size.Messages, AbortToken);
        await run.Ledger.WaitUntilAsync(progress => progress.Completions >= size.Messages / 10, _Recovery, AbortToken);
        await first.StopAsync();
        var doneBeforeStop = run.Ledger.Progress.AcceptedCompleted;

        await run.StartHostAsync(run.Behavior("host-b", size.MinWork, size.MaxWork), cancellationToken: AbortToken);
        var finished = await run.Ledger.WaitUntilAsync(
            progress => progress.AllAcceptedCompleted,
            ChaosRun.DispatchTimeout + _Recovery,
            AbortToken
        );
        await Task.Delay(_QuietWindow, AbortToken);

        // then
        var snapshot = run.Ledger.Snapshot();
        var verdict = LedgerVerdict.Evaluate(snapshot);
        var cutOff = snapshot.Accepted.Count(entry => entry.Started > entry.Completed);

        List<string> violations = [.. verdict.ExactlyOnceViolations()];
        _AddIf(
            violations,
            !finished,
            $"the next host did not finish the backlog within {ChaosRun.DispatchTimeout + _Recovery}"
        );
        _AddIf(
            violations,
            doneBeforeStop >= size.Messages,
            "the backlog finished before the host stopped, so nothing was left for the next host; raise the work time"
        );

        _Report(
            "shutdown under backlog",
            violations,
            $"messages {size.Messages}, stop budget {_Seconds(size.StopBudget)}, done before stop {doneBeforeStop}, runs cut off {cutOff}, completed by host {_ByHost(snapshot.Accepted.SelectMany(entry => entry.CompletedOn))}; {verdict.Describe()}"
        );
        violations.Should().BeEmpty();
    }

    [Fact]
    public override async Task should_run_a_killed_hosts_in_flight_rows_once_on_the_other_hosts()
    {
        // given — the victim holds every delivery it takes, then loses the broker and the database together
        var size = Profile.KilledHost;
        await using var run = _NewRun(settings =>
        {
            settings.Coordination = true;
            settings.WorkConcurrency = (byte)Math.Min(size.Messages, byte.MaxValue);
        });
        var victimBehavior = run.Behavior("victim");
        victimBehavior.Hold();
        var victim = await run.StartHostAsync(
            victimBehavior,
            brokerThroughProxy: true,
            storageThroughProxy: true,
            AbortToken
        );

        await run.PublishBacklogAsync(victim, size.Messages, AbortToken);
        var held = await run.Ledger.WaitUntilAsync(
            progress => progress.Starts >= size.Messages,
            TimeSpan.FromSeconds(60),
            AbortToken
        );

        for (var i = 0; i < size.Survivors; i++)
        {
            await run.StartHostAsync(
                run.Behavior($"survivor-{i}"),
                brokerThroughProxy: false,
                storageThroughProxy: false,
                AbortToken
            );
        }

        // when — a cut on both routes looks like a crash to the broker, the database, and the cluster
        await stack.BrokerProxy.DisableAsync(AbortToken);
        await stack.StorageProxy.DisableAsync(AbortToken);
        var budget = (ChaosRun.DispatchTimeout * 2) + _Recovery;
        var recovered = await run.Ledger.WaitUntilAsync(progress => progress.AllAcceptedCompleted, budget, AbortToken);
        await Task.Delay(_QuietWindow, AbortToken);

        // then
        var snapshot = run.Ledger.Snapshot();
        var verdict = LedgerVerdict.Evaluate(snapshot);
        var ranOnVictim = snapshot.Accepted.Count(entry =>
            entry.CompletedOn.Contains(victim.Name, StringComparer.Ordinal)
        );

        List<string> violations = [.. verdict.ExactlyOnceViolations()];
        _AddIf(violations, !held, "the victim did not take every message before it was cut off");
        _AddIf(violations, !recovered, $"the other hosts did not run the victim's messages within {budget}");
        _AddIf(violations, ranOnVictim > 0, $"{ranOnVictim} message(s) completed on the cut-off victim");

        _Report(
            "killed host with coordination",
            violations,
            $"messages {size.Messages}, survivors {size.Survivors}, completed by host {_ByHost(snapshot.Accepted.SelectMany(entry => entry.CompletedOn))}; {verdict.Describe()}"
        );
        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task should_fail_the_ledger_check_when_the_transport_reports_unsent_messages_as_sent()
    {
        // given — the negative control: every fifth send is dropped but reported to the outbox as sent
        const int messages = 20;
        await using var run = _NewRun();
        var dropped = Enumerable.Range(0, messages).Where(index => index % 5 == 0).Select(run.Key).ToList();
        run.Settings.Sabotage.SwallowSends(dropped);
        var host = await run.StartHostAsync(run.Behavior("host-0"), cancellationToken: AbortToken);

        // when
        await run.PublishBacklogAsync(host, messages, AbortToken);
        await run.Ledger.WaitUntilAsync(
            progress => progress.AcceptedCompleted >= messages - dropped.Count,
            TimeSpan.FromSeconds(60),
            AbortToken
        );
        await Task.Delay(_QuietWindow, AbortToken);

        // then — the ledger catches exactly the dropped messages as lost
        var verdict = LedgerVerdict.Evaluate(run.Ledger.Snapshot());
        var caught = verdict.ExactlyOnceViolations();

        _Report(
            "negative control: sends dropped",
            caught.Count > 0 ? [] : ["the ledger missed the dropped sends"],
            verdict.Describe()
        );
        caught.Should().NotBeEmpty("a message the transport never sent is lost");
        verdict.Lost.Should().BeEquivalentTo(dropped);
        verdict.Duplicated.Should().BeEmpty();
    }

    [Fact]
    public async Task should_fail_the_ledger_check_when_a_message_is_sent_again_under_a_new_id()
    {
        // given — the negative control: every fifth message is sent a second time under an ID the inbox has not seen
        const int messages = 20;
        await using var run = _NewRun();
        var resent = Enumerable.Range(0, messages).Where(index => index % 5 == 0).Select(run.Key).ToList();
        run.Settings.Sabotage.RedeliverUnderNewId(resent);
        var host = await run.StartHostAsync(run.Behavior("host-0"), cancellationToken: AbortToken);

        // when
        await run.PublishBacklogAsync(host, messages, AbortToken);
        await run.Ledger.WaitUntilAsync(
            progress => progress.Completions >= messages + resent.Count,
            TimeSpan.FromSeconds(60),
            AbortToken
        );
        await Task.Delay(_QuietWindow, AbortToken);

        // then — the ledger catches exactly the resent messages as duplicates
        var verdict = LedgerVerdict.Evaluate(run.Ledger.Snapshot());
        var caught = verdict.ExactlyOnceViolations();

        _Report(
            "negative control: sent again",
            caught.Count > 0 ? [] : ["the ledger missed the duplicate runs"],
            verdict.Describe()
        );
        caught.Should().NotBeEmpty("a message whose handler ran twice is a duplicate");
        verdict.Duplicated.Should().BeEquivalentTo(resent);
        verdict.Lost.Should().BeEmpty();
    }

    [Fact]
    public async Task should_lose_nothing_under_a_seeded_sequence_of_faults()
    {
        // given — the faults come from the seed alone, so a failed run replays with the seed it printed
        var size = Profile.Soak;
        var seed = ChaosProfile.ResolveSeed(Environment.GetEnvironmentVariable(ChaosProfile.SeedVariable));
        var plan = SoakFaultPlan.Create(seed, size.Duration, size.Faults, size.Hosts);
        var replay = $"replay with {ChaosProfile.SeedVariable}={seed.ToString(CultureInfo.InvariantCulture)}";
        TestContext.Current.TestOutputHelper?.WriteLine($"Soak {plan}; {replay}");

        await using var run = _NewRun(settings =>
        {
            settings.BrokerThroughProxy = true;
            settings.StorageThroughProxy = true;
        });
        var live = new ChaosHost[size.Hosts];

        for (var i = 0; i < size.Hosts; i++)
        {
            live[i] = await run.StartHostAsync(run.Behavior($"host-{i}.0"), cancellationToken: AbortToken);
        }

        // when
        var faults = _InjectAsync(plan, run, live, AbortToken);
        var attempted = await run.PublishAtRateAsync(
            size.MessagesPerSecond,
            size.Duration,
            index => live[index % live.Length],
            AbortToken
        );
        await faults;
        await stack.RestoreNetworkAsync();

        var recovered = await run.Ledger.WaitUntilAsync(
            progress => progress.AllAcceptedCompleted,
            _Recovery,
            AbortToken
        );
        await Task.Delay(_QuietWindow, AbortToken);

        // then
        var verdict = LedgerVerdict.Evaluate(run.Ledger.Snapshot());
        var failedRows = await run.CountReceivedAsync(ChaosModule.WorkName, StatusName.Failed, AbortToken);
        var deadLettered = await stack.CountQueuedAsync(run.WorkDeadLetterQueue, AbortToken);

        // The seeded faults take storage down too, so only a run that accepted nothing fails the floor.
        List<string> violations =
        [
            .. verdict.AcceptanceViolations(attempted, minimumShare: 0),
            .. verdict.LossViolations(),
        ];
        _AddIf(violations, !recovered, $"not every accepted message ran within {_Recovery} after the last fault");
        _AddIf(violations, failedRows > 0, $"{failedRows} received row(s) ended Failed");
        _AddIf(violations, deadLettered > 0, $"{deadLettered} message(s) were dead-lettered by the broker");

        _Report(
            "seeded soak",
            violations,
            $"{plan}; publishes attempted {attempted}, failed rows {failedRows}, dead-lettered {deadLettered}; {verdict.Describe()}"
        );
        violations.Should().BeEmpty($"the soak with {plan} must lose nothing; {replay}");
    }

    private ChaosRun _NewRun(Action<ChaosSettings>? configure = null)
    {
        var settings = new ChaosSettings();
        configure?.Invoke(settings);

        return new ChaosRun(stack, settings, LoggerProvider);
    }

    private void _Report(string scenario, IReadOnlyList<string> violations, string counts) =>
        ChaosSummary.Record(stack.Database, scenario, violations, counts);

    /// <summary>Restarts one host after another, round-robin, every <paramref name="every"/> until cancelled.</summary>
    private static async Task _RollAsync(
        ChaosRun run,
        ChaosHost[] live,
        TimeSpan every,
        Action restarted,
        CancellationToken cancellationToken
    )
    {
        using var timer = new PeriodicTimer(every);

        for (var turn = 0; await timer.WaitForNextTickAsync(cancellationToken); turn++)
        {
            var slot = turn % live.Length;
            await live[slot].StopAsync();
            live[slot] = await run.StartHostAsync(
                run.Behavior(string.Create(CultureInfo.InvariantCulture, $"host-{slot}.{(turn / live.Length) + 1}")),
                cancellationToken: cancellationToken
            );
            restarted();
        }
    }

    private async Task _FlapStorageAsync(StorageFlapSize size, Action flapped, CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(size.Every - size.Down, cancellationToken);
            await stack.StorageProxy.DisableAsync(CancellationToken.None);

            try
            {
                await Task.Delay(size.Down, cancellationToken);
            }
            finally
            {
                await stack.StorageProxy.EnableAsync(CancellationToken.None);
            }

            flapped();
        }
    }

    private async Task _ChurnBrokerAsync(TimeSpan every, Action dropped, CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(every, cancellationToken);

            // Disabling closes every open connection; enabling at once makes the drop a blip, not an outage.
            await stack.BrokerProxy.DisableAsync(CancellationToken.None);
            await stack.BrokerProxy.EnableAsync(CancellationToken.None);
            dropped();
        }
    }

    /// <summary>Applies each fault of <paramref name="plan"/> at its offset from now.</summary>
    private async Task _InjectAsync(
        SoakFaultPlan plan,
        ChaosRun run,
        ChaosHost[] live,
        CancellationToken cancellationToken
    )
    {
        var started = TimeProvider.System.GetTimestamp();
        var restarts = 0;

        foreach (var fault in plan.Faults)
        {
            var wait = fault.At - TimeProvider.System.GetElapsedTime(started);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            switch (fault.Kind)
            {
                case SoakFaultKind.BrokerCut:
                    await stack.BrokerProxy.DisableAsync(cancellationToken);
                    await Task.Delay(fault.Length, cancellationToken);
                    await stack.BrokerProxy.EnableAsync(cancellationToken);
                    break;
                case SoakFaultKind.BrokerBlackhole:
                    await stack.BlackholeBrokerAsync(cancellationToken);
                    await Task.Delay(fault.Length, cancellationToken);
                    await stack.RestoreBrokerAnswersAsync(cancellationToken);
                    break;
                case SoakFaultKind.StorageCut:
                    await stack.StorageProxy.DisableAsync(cancellationToken);
                    await Task.Delay(fault.Length, cancellationToken);
                    await stack.StorageProxy.EnableAsync(cancellationToken);
                    break;
                case SoakFaultKind.HostRestart:
                    await live[fault.Host].StopAsync();
                    live[fault.Host] = await run.StartHostAsync(
                        run.Behavior(string.Create(CultureInfo.InvariantCulture, $"host-{fault.Host}.{++restarts}")),
                        cancellationToken: cancellationToken
                    );
                    break;
                default:
                    throw new InvalidOperationException($"Unknown soak fault {fault.Kind}.");
            }
        }
    }

    private async Task _ForEachBoundedAsync(int count, Func<int, Task> action)
    {
        using var gate = new SemaphoreSlim(32);

        await Task.WhenAll(
            Enumerable
                .Range(0, count)
                .Select(async index =>
                {
                    await gate.WaitAsync(AbortToken);

                    try
                    {
                        await action(index);
                    }
                    finally
                    {
                        gate.Release();
                    }
                })
        );
    }

    private static async Task _IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // The loop runs until its scenario cancels it; ending on that cancellation is its normal exit.
        }
    }

    private static void _AddIf(List<string> violations, bool condition, string violation)
    {
        if (condition)
        {
            violations.Add(violation);
        }
    }

    private static TimeSpan _Percentile(IReadOnlyList<TimeSpan> sorted, double percentile) =>
        sorted.Count == 0 ? TimeSpan.Zero : sorted[(int)Math.Ceiling(percentile * sorted.Count) - 1];

    private static string _Seconds(TimeSpan value) =>
        string.Create(CultureInfo.InvariantCulture, $"{value.TotalSeconds:F2}s");

    private static string _ByHost(IEnumerable<string> hosts) =>
        string.Join(
            ", ",
            hosts
                .GroupBy(host => host, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Key}={group.Count()}"))
        );

    private static string _Sample(IReadOnlyList<string> values) =>
        string.Join(", ", values.Take(10)) + (values.Count > 10 ? ", ..." : string.Empty);
}
