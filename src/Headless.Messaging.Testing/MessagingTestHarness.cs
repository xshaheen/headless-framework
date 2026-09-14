// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.CommitCoordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.InMemory;
using Headless.Messaging.Internal;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Serialization;
using Headless.Messaging.Storage.InMemory;
using Headless.Messaging.Testing.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Testing;

/// <summary>
/// A fully isolated in-memory test harness for the Headless Messaging pipeline.
/// Intercepts all published and consumed messages so tests can assert on them without
/// relying on timing-sensitive polling or external infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Typical usage:</strong>
/// <code>
/// await using var harness = await MessagingTestHarness.CreateAsync(services =>
/// {
///     services.AddHeadlessMessaging(setup =>
///     {
///         setup.Subscribe&lt;MyHandler&gt;("my-message-name");
///     });
/// });
///
/// await harness.Publisher.PublishAsync(new MyMessage { ... });
/// var recorded = await harness.WaitForConsumed&lt;MyMessage&gt;();
/// </code>
/// </para>
/// <para>
/// The harness keeps the production delivery default, so a plain publish is store-first: the row is durable
/// when <c>PublishAsync</c> returns and the transport send, the <see cref="Published"/> observation, and
/// consumption follow on dispatcher threads. Assert through the <c>WaitFor*</c> methods; for any one message the
/// <see cref="Published"/> observation is recorded before its <see cref="Consumed"/> or <see cref="Faulted"/> one,
/// so the collections are safe to read once the matching wait returns. When a harness is shared across tests call
/// <see cref="ResetAsync"/> between them — it waits for that in-flight tail before clearing.
/// <see cref="RunCoordinatedAsync(Func{Task})"/> opens a commit-coordination scope so a test can exercise
/// <see cref="DeliveryMode.Coordinated"/>.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class MessagingTestHarness : IAsyncDisposable
{
    /// <summary>Default timeout for <c>WaitFor*</c> methods when no explicit timeout is provided.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly MessageObservationStore _store;
    private readonly bool _ownsSp;

    private MessagingTestHarness(IServiceProvider sp, MessageObservationStore store, bool ownsSp)
    {
        ServiceProvider = sp;
        _store = store;
        _ownsSp = ownsSp;
    }

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    /// <summary>Creates a fully isolated in-memory messaging test harness.</summary>
    /// <param name="configure">
    /// Delegate to configure the service collection.
    /// Call <c>services.AddHeadlessMessaging(…)</c> inside this delegate
    /// to register consumers and set messaging options.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for bootstrap.</param>
    /// <returns>A started <see cref="MessagingTestHarness"/> ready for assertions.</returns>
    public static async Task<MessagingTestHarness> CreateAsync(
        Action<IServiceCollection> configure,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(configure);

        var services = new ServiceCollection();

        services.AddLogging();

        // Let the caller register AddHeadlessMessaging + consumers
        configure(services);

        // Shared setup: observation store, decorators, options
        ConfigureServices(services);

        var sp = services.BuildServiceProvider();

        // Bootstrap without hosted-service infrastructure
        var bootstrapper = sp.GetRequiredService<IBootstrapper>();
        await bootstrapper.BootstrapAsync(cancellationToken).ConfigureAwait(false);

        var store = sp.GetRequiredService<MessageObservationStore>();

        return new MessagingTestHarness(sp, store, ownsSp: true);
    }

    // -------------------------------------------------------------------------
    // DI registration (for hosted scenarios — WebApplicationFactory, IHost, etc.)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers the messaging test harness recording infrastructure into an existing
    /// <see cref="IServiceCollection"/>. Called by the
    /// <see cref="MessagingTestHarnessExtensions.AddMessagingTestHarness"/> extension method.
    /// </summary>
    internal static void ConfigureServices(IServiceCollection services)
    {
        // Idempotency guard — safe to call multiple times (e.g., integration test setup)
        if (services.Any(d => d.ServiceType == typeof(TestHarnessMarkerService)))
        {
            return;
        }

        services.AddSingleton<TestHarnessMarkerService>();

        _EnsureInMemoryInfrastructure(services);

        // A real coordinator lets RunCoordinatedAsync open a scope and lets a type registered
        // WithDeliveryMode(DeliveryMode.Coordinated) pass the bootstrap gate. The call is idempotent and its
        // unconditional ICurrentCommitCoordinator registration wins over Messaging's null-coordinator fallback
        // regardless of which setup call the host invoked first.
        services.AddCommitCoordination();

        // Disable parallelism for deterministic single-threaded test execution
        services.Configure<MessagingOptions>(opt =>
        {
            opt.EnablePublishParallelSend = false;
            opt.EnableSubscriberParallelExecute = false;
        });

        var store = new MessageObservationStore();
        services.AddSingleton(store);

        var busRecorded = _DecorateBusTransport(services, store);
        var queueRecorded = _DecorateQueueTransport(services, store);
        _DecoratePipeline(services, store, awaitPublishedRecord: busRecorded || queueRecorded);
        _DecorateOnExhausted(services, store);

        // Register the harness itself — does NOT own the ServiceProvider.
        services.AddSingleton(sp => new MessagingTestHarness(sp, store, ownsSp: false));
    }

    // -------------------------------------------------------------------------
    // Observable collections (snapshots)
    // -------------------------------------------------------------------------

    /// <summary>Gets a snapshot of all messages that were sent to the transport.</summary>
    public IReadOnlyCollection<RecordedMessage> Published => _store.Published;

    /// <summary>Gets a snapshot of all messages that were consumed successfully.</summary>
    public IReadOnlyCollection<RecordedMessage> Consumed => _store.Consumed;

    /// <summary>Gets a snapshot of all messages whose consumer threw an unhandled exception.</summary>
    public IReadOnlyCollection<RecordedMessage> Faulted => _store.Faulted;

    /// <summary>
    /// Gets a snapshot of all messages whose retry budget was exhausted (the framework invoked
    /// <c>RetryPolicy.OnExhausted</c>). Observed BEFORE the user-supplied callback runs.
    /// </summary>
    public IReadOnlyCollection<RecordedMessage> Exhausted => _store.Exhausted;

    // -------------------------------------------------------------------------
    // Awaitable assertions — Published
    // -------------------------------------------------------------------------

    /// <summary>
    /// Waits until a message of type <typeparamref name="T"/> is published,
    /// or throws <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForPublished<T>(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        return _store.WaitForAsync(
            typeof(T),
            MessageObservationType.Published,
            lane: null,
            predicate: null,
            timeout ?? DefaultTimeout,
            cancellationToken
        );
    }

    /// <summary>
    /// Waits until a published message of type <typeparamref name="T"/> satisfies <paramref name="predicate"/>,
    /// or throws <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForPublished<T>(
        Func<T, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        return _store.WaitForAsync(
            typeof(T),
            MessageObservationType.Published,
            lane: null,
            obj => predicate((T)obj),
            timeout ?? DefaultTimeout,
            cancellationToken
        );
    }

    /// <summary>
    /// Waits until a message of type <typeparamref name="T"/> is published for the given
    /// <paramref name="lane"/> (Bus vs Queue), or throws
    /// <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForPublished<T>(
        MessageLane lane,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        return _store.WaitForAsync(
            typeof(T),
            MessageObservationType.Published,
            lane,
            predicate: null,
            timeout ?? DefaultTimeout,
            cancellationToken
        );
    }

    // -------------------------------------------------------------------------
    // Awaitable assertions — Consumed
    // -------------------------------------------------------------------------

    /// <summary>
    /// Waits until a message of type <typeparamref name="T"/> is consumed successfully,
    /// or throws <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForConsumed<T>(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        return _store.WaitForAsync(
            typeof(T),
            MessageObservationType.Consumed,
            lane: null,
            predicate: null,
            timeout ?? DefaultTimeout,
            cancellationToken
        );
    }

    /// <summary>
    /// Waits until a consumed message of type <typeparamref name="T"/> satisfies <paramref name="predicate"/>,
    /// or throws <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForConsumed<T>(
        Func<T, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        return _store.WaitForAsync(
            typeof(T),
            MessageObservationType.Consumed,
            lane: null,
            obj => predicate((T)obj),
            timeout ?? DefaultTimeout,
            cancellationToken
        );
    }

    /// <summary>
    /// Waits until a message of type <typeparamref name="T"/> is consumed for the given
    /// <paramref name="lane"/> (Bus vs Queue), or throws
    /// <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForConsumed<T>(
        MessageLane lane,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        return _store.WaitForAsync(
            typeof(T),
            MessageObservationType.Consumed,
            lane,
            predicate: null,
            timeout ?? DefaultTimeout,
            cancellationToken
        );
    }

    // -------------------------------------------------------------------------
    // Awaitable assertions — Faulted
    // -------------------------------------------------------------------------

    /// <summary>
    /// Waits until processing of a message of type <typeparamref name="T"/> faults,
    /// or throws <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForFaulted<T>(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        return _store.WaitForAsync(
            typeof(T),
            MessageObservationType.Faulted,
            lane: null,
            predicate: null,
            timeout ?? DefaultTimeout,
            cancellationToken
        );
    }

    /// <summary>
    /// Waits until a faulted message of type <typeparamref name="T"/> satisfies <paramref name="predicate"/>,
    /// or throws <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForFaulted<T>(
        Func<T, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        return _store.WaitForAsync(
            typeof(T),
            MessageObservationType.Faulted,
            lane: null,
            obj => predicate((T)obj),
            timeout ?? DefaultTimeout,
            cancellationToken
        );
    }

    /// <summary>
    /// Waits until processing of a message of type <typeparamref name="T"/> faults for the given
    /// <paramref name="lane"/> (Bus vs Queue), or throws
    /// <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForFaulted<T>(
        MessageLane lane,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        return _store.WaitForAsync(
            typeof(T),
            MessageObservationType.Faulted,
            lane,
            predicate: null,
            timeout ?? DefaultTimeout,
            cancellationToken
        );
    }

    // -------------------------------------------------------------------------
    // Awaitable assertions — Exhausted
    // -------------------------------------------------------------------------

    /// <summary>
    /// Waits until the retry budget for a message of type <typeparamref name="T"/> is exhausted
    /// (the framework invoked <c>RetryPolicy.OnExhausted</c>), or throws
    /// <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForExhausted<T>(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        return _store.WaitForAsync(
            typeof(T),
            MessageObservationType.Exhausted,
            lane: null,
            predicate: null,
            timeout ?? DefaultTimeout,
            cancellationToken
        );
    }

    /// <summary>
    /// Waits until an exhausted message of type <typeparamref name="T"/> satisfies <paramref name="predicate"/>,
    /// or throws <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForExhausted<T>(
        Func<T, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        return _store.WaitForAsync(
            typeof(T),
            MessageObservationType.Exhausted,
            lane: null,
            obj => predicate((T)obj),
            timeout ?? DefaultTimeout,
            cancellationToken
        );
    }

    /// <summary>
    /// Waits until the retry budget for a message of type <typeparamref name="T"/> is exhausted
    /// for the given <paramref name="lane"/> (Bus vs Queue), or throws
    /// <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForExhausted<T>(
        MessageLane lane,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        return _store.WaitForAsync(
            typeof(T),
            MessageObservationType.Exhausted,
            lane,
            predicate: null,
            timeout ?? DefaultTimeout,
            cancellationToken
        );
    }

    // -------------------------------------------------------------------------
    // State management
    // -------------------------------------------------------------------------

    /// <summary>
    /// Waits for in-flight messaging work to settle, then resets all in-memory messaging state: recorded
    /// observations, pending transport messages, and persisted storage rows. Call between tests that share a harness.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default publishes are durable: <c>PublishAsync</c> returns once the row is stored, and the transport send,
    /// the <see cref="Published"/> observation, and consumption all run afterwards on dispatcher threads. A
    /// synchronous clear would race that tail, so this method first waits until no published row is
    /// <c>Scheduled</c> or <c>Queued</c> and no received row is <c>Scheduled</c> — those states bracket every
    /// send and every consumer execution — then drops transport messages no consumer has picked up, waits once
    /// more for anything picked up in between, and only then clears observations and storage.
    /// </para>
    /// <para>
    /// A publish that is not yet due is clock-parked and is not awaited: the dispatcher stores it as <c>Queued</c>
    /// when it is due within a minute and as <c>Delayed</c> beyond that, holds it either way, and publishes it when
    /// its time arrives on the host <see cref="TimeProvider"/>. The wait therefore counts a <c>Queued</c> publish as
    /// in flight only once it is due on that clock. A shared harness should not carry pending delays across tests,
    /// or should advance its <c>FakeTimeProvider</c> past them before resetting. Pending <c>WaitFor*</c> calls
    /// fault when the reset clears the store. A message the transport handed to a consumer just before the reset
    /// still runs afterwards, but it is not awaited and its Consumed or Faulted observation is not recorded.
    /// </para>
    /// </remarks>
    /// <param name="timeout">How long to wait for in-flight work; defaults to <see cref="DefaultTimeout"/>.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <exception cref="TimeoutException">In-flight work did not settle within <paramref name="timeout"/>.</exception>
    public async Task ResetAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var storage = ServiceProvider.GetService<InMemoryDataStorage>();
        // The dispatcher decides when a parked publish is due on the host clock, which a test host may fake.
        var hostClock = ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        var waitBudget = timeout ?? DefaultTimeout;
        var startedAt = TimeProvider.System.GetTimestamp();

        if (storage is not null)
        {
            await _WaitForStorageIdleAsync(storage, hostClock, startedAt, waitBudget, cancellationToken)
                .ConfigureAwait(false);
        }

        ServiceProvider.GetService<MemoryQueue>()?.DrainAllPendingMessages();

        if (storage is not null)
        {
            // A consumer that dequeued a message just before the drain stores its received row next; the second
            // wait covers that hand-off instead of clearing underneath it.
            await _WaitForStorageIdleAsync(storage, hostClock, startedAt, waitBudget, cancellationToken)
                .ConfigureAwait(false);
        }

        _store.Clear();
        storage?.Clear();
    }

    // -------------------------------------------------------------------------
    // Coordinated delivery
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs <paramref name="action"/> inside a commit-coordination scope — the harness stand-in for an application
    /// transaction — signalling commit when it completes and rollback when it throws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scope is non-relational: in-memory storage captures every publish made inside the delegate on it, so a
    /// type registered <c>WithDeliveryMode(DeliveryMode.Coordinated)</c> can be published here (outside a scope the
    /// publish throws <see cref="InvalidOperationException"/>), and a default <see cref="DeliveryMode.Durable"/>
    /// publish enlists in the same scope. Commit stores the captured rows and hands them to the dispatcher, so they
    /// surface through <see cref="WaitForPublished{T}(TimeSpan?, CancellationToken)"/> and
    /// <see cref="WaitForConsumed{T}(TimeSpan?, CancellationToken)"/>; rollback discards them and nothing is
    /// recorded. A coordinated message records <c>RequestedDeliveryMode = Coordinated</c> and
    /// <c>ResolvedDeliveryMode = Durable</c>.
    /// </para>
    /// <para>
    /// The scope joins an ambient coordinator when one is already active, so a nested call follows the
    /// commit-coordination child rules: a child rollback dooms the outer scope.
    /// </para>
    /// </remarks>
    /// <param name="action">The work to run inside the scope.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    public async Task RunCoordinatedAsync(Func<Task> action)
    {
        Argument.IsNotNull(action);

        await RunCoordinatedAsync(async () =>
            {
                await action().ConfigureAwait(false);
                return true;
            })
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs <paramref name="action"/> inside a commit-coordination scope and returns its result; see
    /// <see cref="RunCoordinatedAsync(Func{Task})"/> for the commit and rollback semantics.
    /// </summary>
    /// <typeparam name="TResult">The delegate's result type.</typeparam>
    /// <param name="action">The work to run inside the scope.</param>
    /// <returns>The delegate's result, after the scope has committed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    public async Task<TResult> RunCoordinatedAsync<TResult>(Func<Task<TResult>> action)
    {
        Argument.IsNotNull(action);

        var scopeFactory = ServiceProvider.GetRequiredService<ICommitScopeFactory>();
        await using var scope = scopeFactory.Begin(ServiceProvider, capabilities: null);

        TResult result;
        try
        {
            result = await action().ConfigureAwait(false);
        }
        catch (Exception actionException)
        {
            // Disposal alone would roll back, but signalling here drains the rollback callbacks before the caller
            // observes the exception, so the captured rows are already discarded when the test asserts.
            try
            {
                await scope.SignalAsync(CommitOutcome.RolledBack).ConfigureAwait(false);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "Coordinated action failed and rollback signal also faulted.",
                    actionException,
                    rollbackException
                );
            }

            throw;
        }

        await scope.SignalAsync(CommitOutcome.Committed).ConfigureAwait(false);

        return result;
    }

    // -------------------------------------------------------------------------
    // Service access
    // -------------------------------------------------------------------------

    /// <summary>Returns a bus publisher backed by the in-memory transport.</summary>
    public IBus Publisher => ServiceProvider.GetRequiredService<IBus>();

    /// <summary>Returns a queue publisher backed by the in-memory transport.</summary>
    public IQueue Queue => ServiceProvider.GetRequiredService<IQueue>();

    /// <summary>Resolves an arbitrary service from the harness container.</summary>
    public T GetRequiredService<T>()
        where T : notnull
    {
        return ServiceProvider.GetRequiredService<T>();
    }

    /// <summary>Provides direct access to the harness <see cref="IServiceProvider"/>.</summary>
    public IServiceProvider ServiceProvider { get; }

    // -------------------------------------------------------------------------
    // Disposal
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_ownsSp)
        {
            // The host owns the ServiceProvider — nothing to dispose here.
            // The host's disposal will clean up the bootstrapper and DI container.
            return;
        }

        // Cancel the bootstrapper first — its registered callback stops all processing servers.
        // Directly calling DisposeAsync on processors before the bootstrapper cancels its CTS
        // causes a race where the CTS cancel-callback fires on an already-disposed processor CTS.
        var bootstrapper = ServiceProvider.GetService<IBootstrapper>();

        if (bootstrapper is not null)
        {
            try
            {
                await bootstrapper.DisposeAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        // Dispose the DI container — remaining singletons are released here.
        if (ServiceProvider is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>Marker service for idempotency guard in <see cref="ConfigureServices"/>.</summary>
    private sealed class TestHarnessMarkerService;

    private static readonly TimeSpan _ResetPollInterval = TimeSpan.FromMilliseconds(5);

    private static async Task _WaitForStorageIdleAsync(
        InMemoryDataStorage storage,
        TimeProvider hostClock,
        long startedAt,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inFlight = _DescribeInFlightRows(storage, hostClock.GetUtcNow());

            if (inFlight.Count == 0)
            {
                return;
            }

            if (TimeProvider.System.GetElapsedTime(startedAt) >= timeout)
            {
                throw new TimeoutException(
                    $"MessagingTestHarness.ResetAsync waited {timeout} for {inFlight.Count} in-flight message(s) "
                        + $"that never settled: {string.Join(", ", inFlight.Take(10))}. "
                        + "Await the matching WaitFor* first, or raise the timeout."
                );
            }

            // The system clock on purpose: this waits for dispatcher threads to make real progress, and a host that
            // registers a FakeTimeProvider would otherwise never advance the poll.
            await Task.Delay(_ResetPollInterval, TimeProvider.System, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Rows whose status brackets work still running on a dispatcher thread: a published row is Scheduled from
    /// store until after the send and its Published observation, and a received row is Scheduled from admission
    /// until after the consumer and its Consumed/Faulted observation. A Queued published row is a delayed publish
    /// due within a minute; it counts only once its publish time has arrived on the host clock, because until
    /// then the dispatcher parks it in its scheduler queue and no thread is working on it.
    /// </summary>
    private static List<string> _DescribeInFlightRows(InMemoryDataStorage storage, DateTimeOffset now)
    {
        List<string> inFlight = [];

        foreach (var row in storage.PublishedMessages.Values)
        {
            // For a published row, ExpiresAt carries the scheduled publish time (see InMemoryDataStorage._CreateRow
            // and Dispatcher.EnqueueToScheduler); a Queued row with no time is treated as due.
            var isInFlight = row.StatusName switch
            {
                StatusName.Scheduled => true,
                StatusName.Queued => row.ExpiresAt is not { } publishAt || publishAt <= now,
                _ => false,
            };

            if (isInFlight)
            {
                inFlight.Add($"published '{row.Name}' ({row.StatusName})");
            }
        }

        foreach (var row in storage.ReceivedMessages.Values)
        {
            if (row.StatusName is StatusName.Scheduled)
            {
                inFlight.Add($"received '{row.Name}' ({row.StatusName})");
            }
        }

        return inFlight;
    }

    /// <summary>
    /// Verifies that in-memory queue and storage providers are registered.
    /// Throws <see cref="InvalidOperationException"/> if either marker is missing.
    /// </summary>
    private static void _EnsureInMemoryInfrastructure(IServiceCollection services)
    {
        var hasQueueMarker = services.Any(d =>
            d.ServiceType == typeof(MessageQueueMarkerService) && d.Lifetime == ServiceLifetime.Singleton
        );

        var hasStorageMarker = services.Any(d =>
            d.ServiceType == typeof(MessageStorageMarkerService) && d.Lifetime == ServiceLifetime.Singleton
        );

        if (!hasQueueMarker)
        {
            throw new InvalidOperationException(
                "MessagingTestHarness requires an in-memory transport. "
                    + "Call setup.UseInMemory() inside your AddHeadlessMessaging callback."
            );
        }

        if (!hasStorageMarker)
        {
            throw new InvalidOperationException(
                "MessagingTestHarness requires an in-memory storage. "
                    + "Call setup.UseInMemoryStorage() inside your AddHeadlessMessaging callback."
            );
        }
    }

    /// <summary>Wraps the last registration of <typeparamref name="TService"/> with a decorator.</summary>
    /// <returns>Whether a registration existed to decorate.</returns>
    private static bool _DecorateLast<TService>(
        IServiceCollection services,
        Func<IServiceProvider, Func<TService, TService>> createDecorator
    )
        where TService : class
    {
        var original = services.LastOrDefault(d => d.ServiceType == typeof(TService));

        if (original is null)
        {
            return false;
        }

        services.Remove(original);

        services.Add(
            new ServiceDescriptor(
                typeof(TService),
                sp =>
                {
                    var inner = _ResolveFromDescriptor<TService>(sp, original);
                    return createDecorator(sp)(inner);
                },
                original.Lifetime
            )
        );

        return true;
    }

    private static void _DecoratePipeline(
        IServiceCollection services,
        MessageObservationStore store,
        bool awaitPublishedRecord
    )
    {
        var original = services.FirstOrDefault(d => d.ServiceType == typeof(IConsumeMiddlewarePipeline));

        if (original is null)
        {
            return;
        }

        services.Remove(original);

        services.AddSingleton<IConsumeMiddlewarePipeline>(sp =>
        {
            var inner = _ResolveFromDescriptor<IConsumeMiddlewarePipeline>(sp, original);
            return new RecordingConsumeMiddlewarePipeline(inner, store, awaitPublishedRecord, DefaultTimeout);
        });
    }

    /// <summary>
    /// Wraps <c>RetryPolicy.OnExhausted</c> with a recording decorator that captures the
    /// observation BEFORE invoking the user-supplied callback. <c>PostConfigure</c> runs after
    /// all <c>Configure&lt;MessagingOptions&gt;</c> calls (including the harness's own and the
    /// caller's), so the wrap is always the outermost layer.
    /// </summary>
    private static void _DecorateOnExhausted(IServiceCollection services, MessageObservationStore store)
    {
        services.PostConfigure<MessagingOptions>(opt =>
        {
            var original = opt.RetryPolicy.OnExhausted;
            opt.RetryPolicy.OnExhausted = async (info, ct) =>
            {
                // Record FIRST so a hanging or throwing user callback doesn't lose the observation.
                var payload = info.Message.Value ?? info.Message;
                store.Record(
                    RecordedMessage.FromHeaders(
                        info.Message.Headers,
                        payload,
                        payload.GetType(),
                        store.GetUtcNow(),
                        info.Lane,
                        info.Exception
                    ),
                    MessageObservationType.Exhausted
                );

                if (original is not null)
                {
                    await original(info, ct).ConfigureAwait(false);
                }
            };
        });
    }

    private static bool _DecorateBusTransport(IServiceCollection services, MessageObservationStore store)
    {
        return _DecorateLast<IBusTransport>(
            services,
            sp =>
            {
                var serializer = sp.GetRequiredService<ISerializer>();
                var logger = sp.GetService<ILogger<RecordingBusTransport>>();
                return inner => new RecordingBusTransport(inner, store, serializer, logger);
            }
        );
    }

    private static bool _DecorateQueueTransport(IServiceCollection services, MessageObservationStore store)
    {
        return _DecorateLast<IQueueTransport>(
            services,
            sp =>
            {
                var serializer = sp.GetRequiredService<ISerializer>();
                var logger = sp.GetService<ILogger<RecordingQueueTransport>>();
                return inner => new RecordingQueueTransport(inner, store, serializer, logger);
            }
        );
    }

    /// <summary>
    /// Resolves a service from a captured <see cref="ServiceDescriptor"/>, handling all three
    /// registration shapes: implementation type, factory, and instance.
    /// </summary>
    private static T _ResolveFromDescriptor<T>(IServiceProvider sp, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is T instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (T)descriptor.ImplementationFactory(sp);
        }

        if (descriptor.ImplementationType is not null)
        {
            return (T)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType);
        }

        throw new InvalidOperationException(
            $"Cannot resolve service of type {typeof(T).Name} from descriptor: "
                + "descriptor has no implementation type, factory, or instance."
        );
    }
}
