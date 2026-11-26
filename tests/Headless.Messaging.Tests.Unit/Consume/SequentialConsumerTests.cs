// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Testing.Tests;
using Tests.Guarantees;
using Tests.Simulation;

namespace Tests.Consume;

/// <summary>
/// A consumer tuned <c>Sequential()</c> on the real consume pipeline, against the simulated broker and store: one
/// key's messages run one at a time and in order, other keys run beside them, a waiting row is held from retry pickup,
/// a message waiting for a delayed retry holds its key until it finishes, and hosts sharing one store keep the order.
/// </summary>
public sealed class SequentialConsumerTests : TestBase
{
    private static readonly TimeSpan _RetryDelay = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, TaskCompletionSource> _releases = new(StringComparer.Ordinal);

    [Fact]
    public async Task should_run_one_key_in_order_while_another_key_runs_beside_it()
    {
        // given
        var probe = new PipelineProbe { Behavior = _HoldUntilReleased };
        await using var host = await PipelineHosts.StartAsync(probe, AbortToken, _Sequential(concurrency: 2));

        // when
        await _EnqueueAsync(host, "a-1", key: "a");
        await _EnqueueAsync(host, "a-2", key: "a");
        await _EnqueueAsync(host, "b-1", key: "b");

        // then: the other key starts while the first key's head still runs, and the first key's second message waits
        await host.WithinBoundAsync(
            Task.WhenAll(probe.WhenAttemptsAsync("a-1", 1), probe.WhenAttemptsAsync("b-1", 1)),
            AbortToken
        );
        probe.Attempts("a-2").Should().Be(0, "a-1 has not finished");

        _Release("a-1");
        await host.WithinBoundAsync(probe.WhenAttemptsAsync("a-2", 1), AbortToken);
        probe.Completions("a-1").Should().Be(1);

        _Release("a-2");
        _Release("b-1");
        await host.WithinBoundAsync(
            Task.WhenAll(probe.WhenCompletedAsync("a-2", 1), probe.WhenCompletedAsync("b-1", 1)),
            AbortToken
        );
    }

    [Fact]
    public async Task should_run_a_burst_of_one_key_in_the_order_it_was_published()
    {
        // given
        var order = new ConcurrentQueue<string>();
        var probe = new PipelineProbe
        {
            Behavior = (id, _, _) =>
            {
                order.Enqueue(id);
                return ValueTask.CompletedTask;
            },
        };
        await using var host = await PipelineHosts.StartAsync(probe, AbortToken, _Sequential(concurrency: 8));
        var ids = Enumerable.Range(0, 40).Select(static i => $"a-{i}").ToArray();

        // when
        foreach (var id in ids)
        {
            await _EnqueueAsync(host, id, key: "a");
        }

        // then
        await host.WithinBoundAsync(probe.WhenCompletedAsync(ids[^1], 1), AbortToken);
        order.Should().Equal(ids);
    }

    [Fact]
    public async Task should_read_a_sequential_subscription_through_one_client_one_delivery_at_a_time()
    {
        // given: three consumer threads and a concurrency of four, which a sequential consumer applies after admission
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            setup =>
            {
                setup.Options.ConsumerThreadCount = 3;
                _Sequential(concurrency: 4)(setup);
            }
        );

        // when
        var clients = host.Broker.ClientsOf(PipelineOrder.Name);

        // then
        clients.Should().ContainSingle().Which.Request.Concurrency.Should().Be(1);
    }

    [Fact]
    [Guarantee("MSG-LANE-05")]
    public async Task should_hold_a_row_that_waits_behind_its_key_from_retry_pickup()
    {
        // quarantine: #1924 the finished row intermittently reads as Scheduled; skips while MSG-LANE-05 is Unsupported
        MessagingGuarantees.RequireSupport("MSG-LANE-05");

        // given
        var probe = new PipelineProbe { Behavior = _HoldUntilReleased };
        await using var host = await PipelineHosts.StartAsync(probe, AbortToken, _Sequential(concurrency: 1));
        var grace =
            host.GetRequiredService<Microsoft.Extensions.Options.IOptions<MessagingOptions>>().Value.RetryPolicy.InitialDispatchGrace;

        await _EnqueueAsync(host, "a-1", key: "a");
        await host.WithinBoundAsync(probe.WhenAttemptsAsync("a-1", 1), AbortToken);
        await _EnqueueAsync(host, "a-2", key: "a");
        await host.WaitForCallsAsync(StorageOperation.AdmitReceivedMessage, 2, AbortToken);
        var admittedAt = host.Clock.GetUtcNow();

        // when: the waiting row's pickup is put off before its dispatch grace runs out
        while (host.Storage.CallsFinished(StorageOperation.DeferReceivedDispatch) == 0)
        {
            (host.Clock.GetUtcNow() - admittedAt).Should().BeLessThan(grace, "the row must be held before pickup");
            await host.RunRetryPickupAsync(PipelineHosts.PollInterval, AbortToken);
        }

        // and: time runs several graces further
        await host.RunForAsync(grace * 4, AbortToken);

        // then: no host can claim the row, and it has not run
        host.Storage.CallsFinished(StorageOperation.DeferReceivedDispatch).Should().BeGreaterThan(1);
        var claimable = await host.Storage.Inner.GetReceivedMessagesOfNeedRetryAsync(
            MessageLane.Queue,
            consumerIdentities: null,
            AbortToken
        );
        claimable.Should().BeEmpty("the pickup of a row waiting behind its key keeps being put off");
        probe.Attempts("a-2").Should().Be(0);

        // and: it runs once, in its turn
        _Release("a-1");
        _Release("a-2");
        await host.WithinBoundAsync(probe.WhenCompletedAsync("a-2", 1), AbortToken);
        await host.RunForAsync(TimeSpan.FromMinutes(2), AbortToken);
        probe.Attempts("a-2").Should().Be(1);
        (await host.ReceivedRowsAsync(AbortToken)).Should().OnlyContain(row => row.StatusName == StatusName.Succeeded);
    }

    [Fact]
    public async Task should_put_a_keyed_message_back_and_run_it_later_when_the_order_check_fails()
    {
        // given: the store fails the first order check
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(probe, AbortToken, _Sequential(concurrency: 4));
        host.Faults.FailStore(StorageOperation.GetSequenceBlock);

        // when
        await _EnqueueAsync(host, "a-1", key: "a");

        // then: without the answer the message does not run; it is put back for retry pickup
        await host.WaitForCallsAsync(StorageOperation.DeferReceivedDispatch, 1, AbortToken);
        probe.Attempts("a-1").Should().Be(0, "a message whose order is unknown must not run");

        // and: the pickup brings it back, the check answers, and it runs once
        var completed = probe.WhenCompletedAsync("a-1", 1);
        await host.RunRetryPickupsUntilAsync(completed, polls: 300, AbortToken);
        probe.Attempts("a-1").Should().Be(1);
    }

    [Fact]
    public async Task should_leave_a_keyed_message_to_retry_pickup_when_putting_it_back_fails()
    {
        // given: the order check fails, and so does the write that puts the message back
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(probe, AbortToken, _Sequential(concurrency: 4));
        host.Faults.FailStore(StorageOperation.GetSequenceBlock);
        host.Faults.FailStore(StorageOperation.DeferReceivedDispatch);

        // when
        await _EnqueueAsync(host, "a-1", key: "a");
        await host.WaitForCallsAsync(StorageOperation.DeferReceivedDispatch, 1, AbortToken);

        // then: the row keeps the due time admission gave it, so the pickup still runs it once
        var completed = probe.WhenCompletedAsync("a-1", 1);
        await host.RunRetryPickupsUntilAsync(completed, polls: 300, AbortToken);
        probe.Attempts("a-1").Should().Be(1);
    }

    [Fact]
    [Guarantee("MSG-LANE-06")]
    public async Task should_hold_the_key_while_a_message_waits_for_a_delayed_retry()
    {
        // given: no immediate retry, so the first failure of a-1 schedules a delayed one
        var order = new ConcurrentQueue<string>();
        var probe = new PipelineProbe
        {
            Behavior = (id, attempt, _) =>
            {
                order.Enqueue($"{id}#{attempt}");
                return string.Equals(id, "a-1", StringComparison.Ordinal) && attempt == 1
                    ? throw new InvalidOperationException("boom")
                    : ValueTask.CompletedTask;
            },
        };
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            setup =>
            {
                setup.DefaultFailurePolicy(policy => policy.Immediate(0).Delayed(2, _RetryDelay, _RetryDelay));
                _Sequential(concurrency: 4)(setup);
            }
        );

        await _EnqueueAsync(host, "a-1", key: "a");
        await host.WaitForRowAsync(static row => row.NextRetryAt is not null && row.Retries == 1, AbortToken);

        // when: later messages of the key, and one of another key, arrive while a-1 waits for its retry
        await _EnqueueAsync(host, "a-2", key: "a");
        await _EnqueueAsync(host, "a-3", key: "a");
        await _EnqueueAsync(host, "b-1", key: "b");

        // then: the other key runs, and the waiting key's backlog is put back without running
        await host.WithinBoundAsync(probe.WhenCompletedAsync("b-1", 1), AbortToken);
        await host.WaitForCallsAsync(StorageOperation.DeferReceivedDispatch, 2, AbortToken);
        probe.Attempts("a-2").Should().Be(0, "a-1 holds the key until its retry finishes");
        probe.Attempts("a-3").Should().Be(0, "a-1 holds the key until its retry finishes");

        // and: the retry runs once it falls due, then the parked backlog resumes in admission order
        var drained = probe.WhenCompletedAsync("a-3", 1);
        await host.RunRetryPickupsUntilAsync(drained, polls: 300, AbortToken);
        order
            .Where(static entry => entry.StartsWith("a-", StringComparison.Ordinal))
            .Should()
            .Equal("a-1#1", "a-1#2", "a-2#1", "a-3#1");
    }

    [Fact]
    [Guarantee("MSG-LANE-06")]
    public async Task should_stall_only_its_key_while_a_message_keeps_failing_and_free_it_once_it_fails_for_good()
    {
        // given: a-1 fails every attempt, so its retries run out
        var order = new ConcurrentQueue<string>();
        var probe = new PipelineProbe
        {
            Behavior = (id, attempt, _) =>
            {
                order.Enqueue($"{id}#{attempt}");
                return string.Equals(id, "a-1", StringComparison.Ordinal)
                    ? throw new InvalidOperationException("boom")
                    : ValueTask.CompletedTask;
            },
        };
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            setup =>
            {
                setup.DefaultFailurePolicy(policy => policy.Immediate(0).Delayed(2, _RetryDelay, _RetryDelay));
                _Sequential(concurrency: 4)(setup);
            }
        );

        await _EnqueueAsync(host, "a-1", key: "a");
        await host.WaitForRowAsync(static row => row.NextRetryAt is not null && row.Retries == 1, AbortToken);

        // when
        await _EnqueueAsync(host, "a-2", key: "a");
        await _EnqueueAsync(host, "b-1", key: "b");

        // then: the other key is not held behind the failing message
        await host.WithinBoundAsync(probe.WhenCompletedAsync("b-1", 1), AbortToken);
        probe.Attempts("a-2").Should().Be(0);

        // and: once a-1 fails for good, the key moves on
        var completed = probe.WhenCompletedAsync("a-2", 1);
        await host.RunRetryPickupsUntilAsync(completed, polls: 300, AbortToken);
        probe.Exhausted.Should().ContainSingle();
        order
            .Where(static entry => entry.StartsWith("a-", StringComparison.Ordinal))
            .Should()
            .Equal("a-1#1", "a-1#2", "a-1#3", "a-2#1");
    }

    [Fact]
    [Guarantee("MSG-LANE-07")]
    public async Task should_not_start_a_message_on_one_host_while_an_earlier_one_of_its_key_runs_on_another()
    {
        // given: two hosts of one application on one broker and one store, and a-1 running on the first
        var order = new ConcurrentQueue<string>();
        var runningOfKey = 0;
        var overlaps = 0;
        var probe = new PipelineProbe
        {
            Behavior = async (id, attempt, cancellationToken) =>
            {
                var ofKey = id.StartsWith("a-", StringComparison.Ordinal);
                if (ofKey)
                {
                    order.Enqueue(id);
                    if (Interlocked.Increment(ref runningOfKey) > 1)
                    {
                        Interlocked.Increment(ref overlaps);
                    }
                }

                try
                {
                    await _HoldUntilReleased(id, attempt, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    if (ofKey)
                    {
                        Interlocked.Decrement(ref runningOfKey);
                    }
                }
            },
        };
        await using var first = await PipelineHosts.StartAsync(probe, AbortToken, _Sequential(concurrency: 4));
        await _EnqueueAsync(first, "a-1", key: "a");
        await first.WithinBoundAsync(probe.WhenAttemptsAsync("a-1", 1), AbortToken);

        var firstClients = first.Broker.ClientsOf(PipelineOrder.Name);
        await using var second = await PipelineHosts.StartBesideAsync(
            first,
            probe,
            AbortToken,
            _Sequential(concurrency: 4)
        );

        // The first host stops taking deliveries, so the broker hands the next ones to the second.
        foreach (var client in firstClients)
        {
            await client.PauseAsync(AbortToken);
        }

        // when: the next message of the key, and one of another key, reach the second host
        await _EnqueueAsync(second, "a-2", key: "a");
        await _EnqueueAsync(second, "b-1", key: "b");

        // then: the second host runs the other key, and puts a-2 back after reading that a-1 is unfinished
        await second.WithinBoundAsync(probe.WhenAttemptsAsync("b-1", 1), AbortToken);
        await second.WaitForCallsAsync(StorageOperation.DeferReceivedDispatch, 1, AbortToken);
        second.Storage.CallsFinished(StorageOperation.GetSequenceBlock).Should().BePositive();
        probe.Attempts("a-2").Should().Be(0, "a-1 still runs on the other host");

        // and: a-2 runs once a-1 finished, never beside it
        _Release("a-1");
        _Release("a-2");
        _Release("b-1");
        await first.WithinBoundAsync(probe.WhenCompletedAsync("a-1", 1), AbortToken);
        var completed = probe.WhenCompletedAsync("a-2", 1);
        await second.RunRetryPickupsUntilAsync(completed, polls: 120, AbortToken);
        Volatile.Read(ref overlaps).Should().Be(0);
        order.Should().Equal("a-1", "a-2");
    }

    [Fact]
    public async Task should_leave_a_row_that_never_started_to_retry_pickup_once_the_host_stops()
    {
        // given: a message still waiting behind its key when the host stops
        var probe = new PipelineProbe { Behavior = _HoldUntilReleased };
        var stopped = await PipelineHosts.StartAsync(probe, AbortToken, _Sequential(concurrency: 1));
        await _EnqueueAsync(stopped, "a-1", key: "a");
        await stopped.WithinBoundAsync(probe.WhenAttemptsAsync("a-1", 1), AbortToken);
        await _EnqueueAsync(stopped, "a-2", key: "a");
        await stopped.WaitForCallsAsync(StorageOperation.AdmitReceivedMessage, 2, AbortToken);
        await stopped.ShutDownAsync(AbortToken);
        probe.Attempts("a-2").Should().Be(0);

        // when: a host starts on the same store, and time passes the lease of the attempt the stop cut off
        _Release("a-1");
        _Release("a-2");
        await using var host = await PipelineHosts.RestartAsync(
            stopped,
            probe,
            AbortToken,
            _Sequential(concurrency: 1)
        );
        var completed = probe.WhenCompletedAsync("a-2", 1);
        await host.RunRetryPickupsUntilAsync(completed, polls: 600, AbortToken);
        probe.Attempts("a-2").Should().Be(1);
        probe.Completions("a-1").Should().Be(1);
    }

    private static Action<MessagingSetupBuilder> _Sequential(byte concurrency) =>
        setup => setup.Tune(PipelineOrderConsumer.Identity, consumer => consumer.Concurrency(concurrency).Sequential());

    private static Task _EnqueueAsync(SimulationHost host, string id, string key) =>
        host.EnqueueAsync(id, new QueueOptions { RoutingAffinityKey = key }, AbortToken);

    // The handler waits for the test to release it, and ends with the host when the host stops first.
    private async ValueTask _HoldUntilReleased(string id, int attempt, CancellationToken cancellationToken)
    {
        await _ReleaseOf(id).Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void _Release(string id) => _ReleaseOf(id).TrySetResult();

    private TaskCompletionSource _ReleaseOf(string id) =>
        _releases.GetOrAdd(
            id,
            static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        );
}
