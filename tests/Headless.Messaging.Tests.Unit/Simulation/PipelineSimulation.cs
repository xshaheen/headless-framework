// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.Processor;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Simulation;

/// <summary>A queue message the pipeline tests publish; its <see cref="Id"/> names it in the probe's records.</summary>
public sealed record PipelineOrder(string Id)
{
    public const string Name = "tests.pipeline.order";
}

/// <summary>Hands every <see cref="PipelineOrder"/> to the test's <see cref="PipelineProbe"/>.</summary>
[QueueConsumer(Identity)]
public sealed class PipelineOrderConsumer(PipelineProbe probe, TimeProvider clock) : IConsume<PipelineOrder>
{
    public const string Identity = "tests.pipeline.orders";

    public ValueTask ConsumeAsync(ConsumeContext<PipelineOrder> context, CancellationToken cancellationToken)
    {
        return probe.HandleAsync(context.Message.Id, clock.GetUtcNow(), cancellationToken);
    }
}

/// <summary>
/// What a pipeline test observes from the outside: each handler attempt, each completed handler, and each
/// <c>OnExhausted</c> call, each one awaitable so a test waits on the effect instead of on time.
/// </summary>
public sealed class PipelineProbe
{
    private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _completions = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<FailedInfo> _exhausted = new();
    private readonly ConcurrentQueue<string> _timeline = new();
    private readonly Lock _gate = new();
    private readonly List<(Func<bool> Reached, TaskCompletionSource Signal)> _waiters = [];

    /// <summary>What the handler does on each attempt; it succeeds when the test sets nothing.</summary>
    public Func<string, int, CancellationToken, ValueTask> Behavior { get; set; } =
        static (_, _, _) => ValueTask.CompletedTask;

    /// <summary>Gets every <c>OnExhausted</c> call, oldest first.</summary>
    public IReadOnlyList<FailedInfo> Exhausted => [.. _exhausted];

    /// <summary>Gets each attempt and its outcome with the simulated time it started, oldest first, for a failure message.</summary>
    public IReadOnlyList<string> Timeline => [.. _timeline];

    /// <summary>Gets how many times the handler started for <paramref name="id"/>.</summary>
    public int Attempts(string id) => _attempts.GetValueOrDefault(id);

    /// <summary>Gets how many times the handler returned without throwing for <paramref name="id"/>.</summary>
    public int Completions(string id) => _completions.GetValueOrDefault(id);

    /// <summary>Completes once the handler has started <paramref name="count"/> times for <paramref name="id"/>.</summary>
    public Task WhenAttemptsAsync(string id, int count) => _When(() => Attempts(id) >= count);

    /// <summary>Completes once the handler has returned <paramref name="count"/> times for <paramref name="id"/>.</summary>
    public Task WhenCompletedAsync(string id, int count) => _When(() => Completions(id) >= count);

    /// <summary>Completes once <c>OnExhausted</c> has been called <paramref name="count"/> times.</summary>
    public Task WhenExhaustedAsync(int count) => _When(() => _exhausted.Count >= count);

    internal async ValueTask HandleAsync(string id, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        var attempt = _attempts.AddOrUpdate(id, 1, static (_, count) => count + 1);
        _timeline.Enqueue($"{startedAt:HH:mm:ss.fff} attempt {attempt} of {id}");
        _Notify();

        try
        {
            await Behavior(id, attempt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _timeline.Enqueue($"  attempt {attempt} of {id} threw {ex.GetType().Name}");
            throw;
        }

        _completions.AddOrUpdate(id, 1, static (_, count) => count + 1);
        _Notify();
    }

    internal Task RecordExhaustedAsync(FailedInfo info, CancellationToken _)
    {
        _timeline.Enqueue($"  OnExhausted for row {info.StorageId}");
        _exhausted.Enqueue(info);
        _Notify();
        return Task.CompletedTask;
    }

    private Task _When(Func<bool> reached)
    {
        lock (_gate)
        {
            if (reached())
            {
                return Task.CompletedTask;
            }

            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((reached, signal));
            return signal.Task;
        }
    }

    private void _Notify()
    {
        lock (_gate)
        {
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (_waiters[i].Reached())
                {
                    _waiters[i].Signal.TrySetResult();
                    _waiters.RemoveAt(i);
                }
            }
        }
    }
}

/// <summary>Starts simulation hosts that run <see cref="PipelineOrderConsumer"/> through the real pipeline.</summary>
internal static class PipelineHosts
{
    /// <summary>How often retry pickup polls; fixed, so a test steps the clock by it and never skips a poll.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>Bounds an event-driven wait so a broken run fails instead of hanging; a passing test never waits it out.</summary>
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    /// <summary>The longest wait <see cref="StepShortWaitsUntilAsync"/> steps to: back-offs and redelivery delays fit.</summary>
    public static readonly TimeSpan ShortWait = TimeSpan.FromSeconds(10);

    // Far more steps than any scenario's back-offs need, so only a run that is going nowhere reaches it.
    private const int _MaxSteps = 500;

    /// <summary>Sets retry pickup to poll far less often than <see cref="ShortWait"/>, for a test that steps short waits.</summary>
    public static void QuietRetryPickup(MessagingSetupBuilder setup) =>
        setup.Options.RetryProcessor.BaseInterval = TimeSpan.FromMinutes(10);

    public static Task<SimulationHost> StartAsync(
        PipelineProbe probe,
        CancellationToken cancellationToken,
        Action<MessagingSetupBuilder>? configure = null,
        SimulatedBroker? broker = null,
        Action<IServiceCollection>? configureServices = null
    )
    {
        return SimulationHost.StartAsync(
            setup => _Configure(setup, probe, configure),
            services => _ConfigureServices(services, probe, configureServices),
            broker,
            cancellationToken
        );
    }

    private static void _Configure(
        MessagingSetupBuilder setup,
        PipelineProbe probe,
        Action<MessagingSetupBuilder>? configure
    )
    {
        setup.AddConsumer<PipelineOrderConsumer>();
        setup.Options.RetryProcessor.AdaptivePolling = false;
        setup.Options.RetryProcessor.BaseInterval = PollInterval;
        setup.Options.RetryPolicy.OnExhausted = probe.RecordExhaustedAsync;
        configure?.Invoke(setup);
    }

    private static void _ConfigureServices(
        IServiceCollection services,
        PipelineProbe probe,
        Action<IServiceCollection>? configureServices
    )
    {
        services.AddSingleton(probe);
        services.ConfigureMessaging(messaging => messaging.Message<PipelineOrder>(PipelineOrder.Name));
        configureServices?.Invoke(services);
    }

    public static Task EnqueueAsync(this SimulationHost host, string id, CancellationToken cancellationToken) =>
        host.EnqueueAsync(id, options: null, cancellationToken);

    /// <summary>Starts a host in place of <paramref name="previous"/>, on its broker and store, with the same consumer.</summary>
    public static Task<SimulationHost> RestartAsync(
        SimulationHost previous,
        PipelineProbe probe,
        CancellationToken cancellationToken,
        Action<MessagingSetupBuilder>? configure = null
    )
    {
        return SimulationHost.RestartAsync(
            previous,
            setup => _Configure(setup, probe, configure),
            services => _ConfigureServices(services, probe, configureServices: null),
            cancellationToken
        );
    }

    /// <summary>
    /// Stops the host gracefully, moving the clock past each wait shutdown arms (its drain and close bounds) until it
    /// finishes; waits armed before the stop began are left alone.
    /// </summary>
    public static async Task ShutDownAsync(this SimulationHost host, CancellationToken cancellationToken)
    {
        var mark = host.Clock.WaitMark;
        await host.FinishShutdownAsync(host.StopAsync(cancellationToken), mark, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Drives a shutdown that began at <paramref name="mark"/> to its end; see <see cref="ShutDownAsync"/>.</summary>
    public static async Task FinishShutdownAsync(
        this SimulationHost host,
        Task stopping,
        long mark,
        CancellationToken cancellationToken
    )
    {
        for (var step = 0; !stopping.IsCompleted; step++)
        {
            if (step == _MaxSteps)
            {
                throw new TimeoutException($"Shutdown did not finish after {step} steps.");
            }

            var armed = host.Clock.WaitMark;
            var nearest = host.Clock.PendingWaitsArmedSince(mark).FirstOrDefault(static due => due > TimeSpan.Zero);
            if (nearest > TimeSpan.Zero)
            {
                host.Advance(nearest);
                continue;
            }

            await host.WithinBoundAsync(
                    Task.WhenAny(stopping, host.Clock.WhenWaitArmedAsync(armed, cancellationToken)),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        await stopping.ConfigureAwait(false);
    }

    public static async Task EnqueueAsync(
        this SimulationHost host,
        string id,
        QueueOptions? options,
        CancellationToken cancellationToken
    )
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IQueue>()
            .EnqueueAsync(new PipelineOrder(id), options, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Moves the clock to each short wait in turn until <paramref name="effect"/> happens: a wait due within
    /// <see cref="ShortWait"/>, such as an admission back-off or a broker's redelivery delay. Between steps it waits,
    /// without moving time, for the next short wait to be armed, which the pipeline does only once the work the last
    /// step released has run as far as its next wait.
    /// </summary>
    /// <remarks>
    /// Only a host whose polling loops wait longer than <see cref="ShortWait"/> can be stepped this way, so its own
    /// polls are never mistaken for the work in flight; start it with <see cref="QuietRetryPickup"/>. Start stepping
    /// only once the pipeline waits on time.
    /// </remarks>
    public static async Task StepShortWaitsUntilAsync(
        this SimulationHost host,
        Task effect,
        CancellationToken cancellationToken
    )
    {
        for (var step = 0; !effect.IsCompleted; step++)
        {
            if (step == _MaxSteps)
            {
                throw new TimeoutException(
                    $"The awaited effect did not happen after {step} steps.{Environment.NewLine}"
                        + await host.DescribeAsync(cancellationToken).ConfigureAwait(false)
                );
            }

            var mark = host.Clock.WaitMark;
            var nearest = host.Clock.PendingWaitsArmedSince(0).FirstOrDefault(static due => due > TimeSpan.Zero);
            if (nearest > TimeSpan.Zero && nearest <= ShortWait)
            {
                host.Advance(nearest);
                continue;
            }

            await host.WithinBoundAsync(
                    Task.WhenAny(effect, host.Clock.WhenWaitArmedAsync(mark, cancellationToken)),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        await effect.ConfigureAwait(false);
    }

    /// <summary>
    /// Waits, without moving time, until <paramref name="reached"/> holds about the broker, checking it after each broker
    /// change.
    /// </summary>
    public static async Task WaitUntilAsync(
        this SimulationHost host,
        Func<bool> reached,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            var seen = host.Broker.Events.Count;
            if (reached())
            {
                return;
            }

            await host.WithinBoundAsync(
                    host.Broker.WaitForEventsAsync(static _ => true, seen + 1, cancellationToken),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    /// <summary>Waits, without moving time, until <paramref name="count"/> calls of <paramref name="operation"/> finished.</summary>
    public static async Task WaitForCallsAsync(
        this SimulationHost host,
        StorageOperation operation,
        int count,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            var mark = host.Storage.CallMark;
            if (host.Storage.CallsFinished(operation) >= count)
            {
                return;
            }

            await host.WithinBoundAsync(host.Storage.WhenCallFinishedAsync(mark, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Gets the circuit of <see cref="PipelineOrderConsumer"/>, which every pipeline host registers.</summary>
    public static CircuitBreakerSnapshot ConsumerCircuit(this SimulationHost host)
    {
        return host.GetRequiredService<ICircuitBreakerMonitor>()
                .GetSnapshot(MessageLane.Queue, PipelineOrderConsumer.Identity)
            ?? throw new InvalidOperationException($"{PipelineOrderConsumer.Identity} has no circuit.");
    }

    /// <summary>Waits, without moving time, until a wait due within <paramref name="due"/> is armed after <paramref name="mark"/>.</summary>
    public static Task WhenWaitArmedAsync(
        this SimulationHost host,
        long mark,
        TimeSpan due,
        CancellationToken cancellationToken
    ) => host.WhenWaitArmedAsync(mark, pending => pending > TimeSpan.Zero && pending <= due, cancellationToken);

    /// <summary>Waits, without moving time, until a wait armed after <paramref name="mark"/> is due as <paramref name="match"/> says.</summary>
    public static async Task WhenWaitArmedAsync(
        this SimulationHost host,
        long mark,
        Func<TimeSpan, bool> match,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            var next = host.Clock.WaitMark;
            if (host.Clock.PendingWaitsArmedSince(mark).Any(match))
            {
                return;
            }

            await host.WithinBoundAsync(host.Clock.WhenWaitArmedAsync(next, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Moves the clock <paramref name="advance"/> once retry pickup is parked, then waits, without moving time, until
    /// the pickup that advance released has claimed due rows and run every attempt it claimed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pickup runs its claim and its attempts after the advance returns, on other threads, so time must not move again
    /// until it finishes: a later poll would find the pickup still running and skip it, and a long run of steps could
    /// outrun the attempt's lease. Waiting on the pickup itself keeps every step at rest without waiting real time.
    /// </para>
    /// <para>
    /// A retry dispatch awaits its attempt, so a pickup that went idle has written the attempt's outcome.
    /// </para>
    /// </remarks>
    public static async Task RunRetryPickupAsync(
        this SimulationHost host,
        TimeSpan advance,
        CancellationToken cancellationToken
    )
    {
        await host.WhenRetryPickupParkedAsync(cancellationToken).ConfigureAwait(false);

        var claims = host.Storage.CallsFinished(StorageOperation.GetReceivedMessagesOfNeedRetry);
        host.Advance(advance);

        // The mark is read before the count, so a claim finishing between the two still ends the wait.
        while (true)
        {
            var mark = host.Storage.CallMark;
            if (host.Storage.CallsFinished(StorageOperation.GetReceivedMessagesOfNeedRetry) != claims)
            {
                break;
            }

            await host.WithinBoundAsync(host.Storage.WhenCallFinishedAsync(mark, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }

        await host.WhenRetryPickupParkedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs one retry pickup per poll interval for <paramref name="duration"/>, each one to completion.</summary>
    public static async Task RunForAsync(
        this SimulationHost host,
        TimeSpan duration,
        CancellationToken cancellationToken
    )
    {
        var until = host.Clock.GetUtcNow() + duration;

        while (host.Clock.GetUtcNow() < until)
        {
            await host.RunRetryPickupAsync(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Completes once retry pickup is at rest: no quadrant is picking up, and its next poll is armed. A poll is never
    /// further away than one interval, so a pending wait that close is that poll.
    /// </summary>
    public static async Task WhenRetryPickupParkedAsync(this SimulationHost host, CancellationToken cancellationToken)
    {
        var retry = host.Services.GetRequiredService<MessageNeedToRetryProcessor>();

        while (true)
        {
            var mark = host.Clock.WaitMark;
            foreach (var direction in (MessageType[])[MessageType.Publish, MessageType.Subscribe])
            {
                foreach (var lane in (MessageLane[])[MessageLane.Bus, MessageLane.Queue])
                {
                    await host.WithinBoundAsync(
                            retry.WaitForQuadrantIdleForTestAsync(direction, lane),
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }
            }

            if (host.Clock.PendingWaitsArmedSince(0).Any(due => due > TimeSpan.Zero && due <= PollInterval))
            {
                return;
            }

            await host.WithinBoundAsync(host.Clock.WhenWaitArmedAsync(mark, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits, without moving time, until the received row of <see cref="PipelineOrder"/> matches
    /// <paramref name="predicate"/>. Rows change only through the store, so it reads them again after each store call.
    /// </summary>
    public static async Task<MessageView> WaitForRowAsync(
        this SimulationHost host,
        Func<MessageView, bool> predicate,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            var mark = host.Storage.CallMark;
            var rows = await host.ReceivedRowsAsync(cancellationToken).ConfigureAwait(false);
            if (rows.FirstOrDefault(predicate) is { } row)
            {
                return row;
            }

            await host.WithinBoundAsync(host.Storage.WhenCallFinishedAsync(mark, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Awaits <paramref name="effect"/> within <see cref="Bound"/>, failing with the stored rows and the broker's record,
    /// which is what a reader needs to see where the run stopped.
    /// </summary>
    public static async Task WithinBoundAsync(
        this SimulationHost host,
        Task effect,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await effect.WaitAsync(Bound, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"The awaited effect did not happen.{Environment.NewLine}"
                    + await host.DescribeAsync(cancellationToken).ConfigureAwait(false),
                ex
            );
        }
    }

    /// <summary>Describes the host's received rows and the broker's record, for a failure message.</summary>
    public static async Task<string> DescribeAsync(this SimulationHost host, CancellationToken cancellationToken)
    {
        var received = await host.ReceivedRowsAsync(cancellationToken).ConfigureAwait(false);
        var published = await host.PublishedRowsAsync(cancellationToken).ConfigureAwait(false);
        var lines = received
            .Select(row => ("received", row))
            .Concat(published.Select(row => ("published", row)))
            .Select(entry =>
                $"  {entry.Item1} row {entry.row.StorageId}: {entry.row.StatusName}, retries {entry.row.Retries}, "
                + $"next retry {entry.row.NextRetryAt}, locked until {entry.row.LockedUntil}"
            )
            .Concat(
                host.Broker.Events.Select(e =>
                    $"  broker {e.At:HH:mm:ss.fff} {e.Kind} {e.Subscription} {e.MessageId} #{e.DeliveryCount}"
                )
            );

        var pending = string.Join(", ", host.Clock.PendingWaitsArmedSince(0));
        var timeline = host.Services.GetService<PipelineProbe>()?.Timeline ?? [];

        return $"Simulated now {host.Clock.GetUtcNow():HH:mm:ss.fff}; pending waits due in {pending}."
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, timeline.Select(entry => $"  handler {entry}").Concat(lines))}";
    }

    /// <summary>Returns the received rows of <see cref="PipelineOrder"/>.</summary>
    public static Task<IReadOnlyList<MessageView>> ReceivedRowsAsync(
        this SimulationHost host,
        CancellationToken cancellationToken
    ) => host.RowsAsync(MessageType.Subscribe, cancellationToken);

    /// <summary>Returns the published rows of <see cref="PipelineOrder"/>.</summary>
    public static Task<IReadOnlyList<MessageView>> PublishedRowsAsync(
        this SimulationHost host,
        CancellationToken cancellationToken
    ) => host.RowsAsync(MessageType.Publish, cancellationToken);

    /// <summary>Waits, without moving time, until a published row of <see cref="PipelineOrder"/> matches <paramref name="predicate"/>.</summary>
    public static async Task<MessageView> WaitForPublishedRowAsync(
        this SimulationHost host,
        Func<MessageView, bool> predicate,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            var mark = host.Storage.CallMark;
            var rows = await host.PublishedRowsAsync(cancellationToken).ConfigureAwait(false);
            if (rows.FirstOrDefault(predicate) is { } row)
            {
                return row;
            }

            await host.WithinBoundAsync(host.Storage.WhenCallFinishedAsync(mark, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<MessageView>> RowsAsync(
        this SimulationHost host,
        MessageType messageType,
        CancellationToken cancellationToken
    )
    {
        var page = await host
            .Storage.GetMonitoringApi()
            .GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = messageType,
                    Name = PipelineOrder.Name,
                    CurrentPage = 0,
                    PageSize = 100,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return page.Items;
    }
}
