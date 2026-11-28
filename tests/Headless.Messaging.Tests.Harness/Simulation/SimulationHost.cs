// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Simulation;

/// <summary>Shutdown stopped making progress because it waits on simulated time the test is not advancing.</summary>
[PublicAPI]
public sealed class SimulationStallException(string message) : Exception(message);

/// <summary>
/// A messaging host built through the real <c>AddHeadlessMessaging</c> setup, on a <see cref="SimulatedBroker"/> and a
/// fault-injecting in-memory store, with one <see cref="SimulationClock"/> as both its app clock and its back-off clock.
/// Nothing in it waits real time: retries, leases, redelivery delays, and shutdown bounds all move when the test calls
/// <see cref="Advance"/>.
/// </summary>
/// <remarks>
/// <para>
/// The clock is registered before messaging and before anything the caller adds, so every registration that falls back
/// to the system clock with <c>TryAdd</c>, such as distributed-lock setup, finds the simulation clock instead.
/// </para>
/// <para>
/// Hosts started on one broker share its messages and its clock, as processes share a broker. Each host keeps its own
/// store, faulted by its own <see cref="Faults"/>, unless <see cref="StartBesideAsync"/> started it on another host's
/// store; a host that created its broker shares one script with it.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class SimulationHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly Lock _gate = new();
    private Task? _stopTask;
    private long _stopMark;
    private int _disposed;

    private SimulationHost(ServiceProvider services, SimulatedBroker broker, FaultScript faults)
    {
        _services = services;
        Broker = broker;
        Faults = faults;
        Storage = services.GetRequiredService<FaultInjectingDataStorage>();
    }

    /// <summary>Gets the simulated time the host and its broker run on.</summary>
    public SimulationClock Clock => Broker.Clock;

    /// <summary>Gets the broker the host publishes to and consumes from.</summary>
    public SimulatedBroker Broker { get; }

    /// <summary>Gets the fault plan of the host's store.</summary>
    public FaultScript Faults { get; }

    /// <summary>Gets the host's store, as the pipeline sees it.</summary>
    public FaultInjectingDataStorage Storage { get; }

    /// <summary>Gets the host's service provider.</summary>
    public IServiceProvider Services => _services;

    /// <summary>
    /// Gets or sets how long, in real time, <see cref="StopAsync"/> lets shutdown run before it checks whether shutdown
    /// is waiting on the simulation clock. It bounds only a stalled shutdown: one that finishes returns at once.
    /// </summary>
    public TimeSpan ShutdownStallWindow { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Builds and starts a host.</summary>
    /// <param name="configure">Registers consumers and sets messaging options, as an application's own setup does.</param>
    /// <param name="configureServices">Adds the services the consumers need; runs after the clock is registered.</param>
    /// <param name="broker">A broker shared with other hosts; a new one, on a new clock, when omitted.</param>
    /// <param name="cancellationToken">Cancels startup.</param>
    public static Task<SimulationHost> StartAsync(
        Action<MessagingSetupBuilder> configure,
        Action<IServiceCollection>? configureServices = null,
        SimulatedBroker? broker = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(configure);

        var ownsBroker = broker is null;
        broker ??= new SimulatedBroker(new SimulationClock());
        var faults = ownsBroker ? broker.Faults : new FaultScript();

        return _StartAsync(configure, configureServices, broker, faults, survivingStore: null, cancellationToken);
    }

    /// <summary>
    /// Starts a host in place of <paramref name="previous"/>, as a process restarts: on the same broker, the same store
    /// with every row it holds, and the same fault plan, but with a new pipeline and nothing of the old one in memory.
    /// </summary>
    /// <remarks>
    /// Stop or dispose <paramref name="previous"/> first. A host that crashed is one whose attempt was cut off before it
    /// wrote anything; graceful shutdown cancels a running attempt the same way, so stopping it stands in for the crash.
    /// </remarks>
    /// <param name="previous">The stopped host whose broker, store, and faults the new host takes over.</param>
    /// <param name="configure">Registers consumers and sets messaging options, as the restarted application does.</param>
    /// <param name="configureServices">Adds the services the consumers need.</param>
    /// <param name="cancellationToken">Cancels startup.</param>
    public static Task<SimulationHost> RestartAsync(
        SimulationHost previous,
        Action<MessagingSetupBuilder> configure,
        Action<IServiceCollection>? configureServices = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(previous);
        Argument.IsNotNull(configure);

        return _StartAsync(
            configure,
            configureServices,
            previous.Broker,
            previous.Faults,
            previous.Storage.Inner,
            cancellationToken
        );
    }

    /// <summary>
    /// Starts a host beside <paramref name="running"/>, as a second process of the same application: on the same broker
    /// and the same store with every row it holds, with its own pipeline and its own fault plan.
    /// </summary>
    /// <remarks>
    /// Dispose this host before <paramref name="running"/>: the running host owns the store and disposes it with itself.
    /// </remarks>
    /// <param name="running">The host whose broker and store the new host shares.</param>
    /// <param name="configure">Registers consumers and sets messaging options, as the second process does.</param>
    /// <param name="configureServices">Adds the services the consumers need.</param>
    /// <param name="cancellationToken">Cancels startup.</param>
    public static Task<SimulationHost> StartBesideAsync(
        SimulationHost running,
        Action<MessagingSetupBuilder> configure,
        Action<IServiceCollection>? configureServices = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(running);
        Argument.IsNotNull(configure);

        return _StartAsync(
            configure,
            configureServices,
            running.Broker,
            new FaultScript(),
            running.Storage.Inner,
            cancellationToken
        );
    }

    private static async Task<SimulationHost> _StartAsync(
        Action<MessagingSetupBuilder> configure,
        Action<IServiceCollection>? configureServices,
        SimulatedBroker broker,
        FaultScript faults,
        IDataStorage? survivingStore,
        CancellationToken cancellationToken
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(broker.Clock);
        services.AddKeyedSingleton<TimeProvider>(MessagingOptions.BackoffClockServiceKey, broker.Clock);
        configureServices?.Invoke(services);

        services.AddHeadlessMessaging(setup =>
        {
            setup.UseSimulatedBroker(broker);

            // The in-memory store guarantees a process-local inbox only, which the host must accept to start on it.
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
            setup.UseInMemoryStorage();
            configure(setup);
        });

        if (survivingStore is not null)
        {
            // Registered as an instance, so the new provider decorates it and never disposes the rows another host
            // shares or that this host outlives.
            services.AddSingleton(survivingStore);
        }

        FaultInjectingDataStorage.Install(services, faults, broker.Clock);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var host = new SimulationHost(provider, broker, faults);

        try
        {
            await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await host.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return host;
    }

    /// <summary>Moves the simulation clock forward.</summary>
    public void Advance(TimeSpan delta) => Clock.Advance(delta);

    /// <summary>Gets a service from the host.</summary>
    public T GetRequiredService<T>()
        where T : notnull => _services.GetRequiredService<T>();

    /// <summary>
    /// Stops messaging, and fails fast instead of hanging when shutdown waits on simulated time: when shutdown has not
    /// finished within <see cref="ShutdownStallWindow"/>, the clock has not moved since this call began, and waits armed
    /// on the clock since the shutdown began are still pending, nothing will move them while the test is awaiting this
    /// call.
    /// </summary>
    /// <remarks>
    /// To let a shutdown that needs simulated time finish, start this call, advance the clock, then await it. Once the
    /// clock has moved during the call, the call waits for shutdown to end with no stall check, so bound it the way the
    /// test bounds any other wait.
    /// </remarks>
    /// <exception cref="SimulationStallException">Shutdown is waiting on the simulation clock.</exception>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var calledAt = Clock.GetUtcNow();
        var stop = _BeginStop();

        while (true)
        {
            try
            {
                await stop.WaitAsync(ShutdownStallWindow, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (TimeoutException)
            {
                // Checked below: a shutdown can be slow for reasons that have nothing to do with the clock.
            }

            if (Clock.GetUtcNow() != calledAt)
            {
                // The caller is driving the clock, so a pending wait proves no stall: shutdown keeps its own bound armed
                // while the work an advance released runs in real time, which on a loaded machine outlasts any window.
                await stop.WaitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var pending = Clock.PendingWaitsArmedSince(_stopMark);
            if (pending.Count > 0)
            {
                throw new SimulationStallException(
                    $"Shutdown has not finished after {ShutdownStallWindow} and is waiting on the simulation clock: "
                        + $"{pending.Count} wait(s) armed since it began are pending, the nearest due in {pending[0]} "
                        + "of simulated time. Advance the clock while awaiting StopAsync, or remove the fault that holds "
                        + "shutdown."
                );
            }

            // No clock wait yet, but a slow shutdown may still arm one that nothing would move, so look again.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // A hung close would otherwise outlive the test; the scenario that needed it is over.
        Faults.ReleaseHungCloses();
        Broker.Faults.ReleaseHungCloses();

        var stop = _BeginStop();
        var shutdownTimeout = _services.GetRequiredService<IOptions<MessagingOptions>>().Value.ShutdownTimeout;

        // Cleanup may follow a test that left shutdown waiting on the clock; each step past the shutdown bound releases
        // whatever the core bounds, and a few are enough for nested bounds.
        for (var step = 0; step < 5 && !stop.IsCompleted; step++)
        {
            try
            {
                await stop.WaitAsync(ShutdownStallWindow).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Clock.Advance(shutdownTimeout);
            }
        }

        try
        {
            await stop.ConfigureAwait(false);
        }
        finally
        {
            await _services.DisposeAsync().ConfigureAwait(false);
        }
    }

    private Task _BeginStop()
    {
        lock (_gate)
        {
            if (_stopTask is null)
            {
                _stopMark = Clock.WaitMark;
                _stopTask = _services.GetRequiredService<IBootstrapper>().DisposeAsync().AsTask();
            }

            return _stopTask;
        }
    }
}
