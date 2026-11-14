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
/// and a delayed retry releases its key and later takes its turn.
/// </summary>
public sealed class SequentialConsumerTests : TestBase
{
    private static readonly TimeSpan _RetryDelay = TimeSpan.FromSeconds(10);

    // A delayed retry falls due within its delay plus 20% jitter, and retry pickup sees it on its next poll.
    private static readonly TimeSpan _PastRetryDelay = (_RetryDelay * 1.2) + PipelineHosts.PollInterval;

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
    [Guarantee("MSG-LANE-06")]
    public async Task should_release_the_key_while_a_message_waits_for_a_delayed_retry()
    {
        // given: no immediate retry, so the first failure schedules a delayed one
        var order = new ConcurrentQueue<string>();
        var probe = new PipelineProbe
        {
            Behavior = (id, attempt, _) =>
            {
                order.Enqueue($"{id}#{attempt}");
                return id == "a-1" && attempt == 1
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

        // when
        await _EnqueueAsync(host, "a-1", key: "a");
        await _EnqueueAsync(host, "a-2", key: "a");

        // then: the later message of the key runs without waiting for the retry
        await host.WithinBoundAsync(probe.WhenCompletedAsync("a-2", 1), AbortToken);
        probe.Attempts("a-1").Should().Be(1);

        // and: the retry runs once it falls due
        await host.RunRetryPickupAsync(_PastRetryDelay, AbortToken);
        await host.WithinBoundAsync(probe.WhenCompletedAsync("a-1", 1), AbortToken);
        order.Should().Equal("a-1#1", "a-2#1", "a-1#2");
    }

    [Fact]
    [Guarantee("MSG-LANE-06")]
    public async Task should_queue_a_delayed_retry_behind_the_message_its_key_is_running()
    {
        // given
        var probe = new PipelineProbe
        {
            Behavior = (id, attempt, cancellationToken) =>
                id == "a-1" && attempt == 1
                    ? throw new InvalidOperationException("boom")
                    : _HoldUntilReleased(id, attempt, cancellationToken),
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
        await _EnqueueAsync(host, "a-2", key: "a");
        await host.WithinBoundAsync(probe.WhenAttemptsAsync("a-2", 1), AbortToken);

        // when: the retry falls due while another message of its key runs
        await host.RunRetryPickupAsync(_PastRetryDelay, AbortToken);

        // then: it waits for its key
        probe.Attempts("a-1").Should().Be(1, "a-2 is still running on the key");

        _Release("a-2");
        await host.WithinBoundAsync(probe.WhenAttemptsAsync("a-1", 2), AbortToken);
        probe.Completions("a-2").Should().Be(1);
        _Release("a-1");
        await host.WithinBoundAsync(probe.WhenCompletedAsync("a-1", 1), AbortToken);
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

        // when: a host starts on the same store, and time passes the dispatch grace
        _Release("a-1");
        _Release("a-2");
        await using var host = await PipelineHosts.RestartAsync(
            stopped,
            probe,
            AbortToken,
            _Sequential(concurrency: 1)
        );
        var completed = probe.WhenCompletedAsync("a-2", 1);
        for (var poll = 0; poll < 120 && !completed.IsCompleted; poll++)
        {
            await host.RunRetryPickupAsync(PipelineHosts.PollInterval, AbortToken);
        }

        // then
        await host.WithinBoundAsync(completed, AbortToken);
        probe.Attempts("a-2").Should().Be(1);
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
