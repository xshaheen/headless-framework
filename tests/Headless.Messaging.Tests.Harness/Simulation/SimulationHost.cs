// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Internal;
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
/// store, faulted by its own <see cref="Faults"/>; a host that created its broker shares one script with it.
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
    public static async Task<SimulationHost> StartAsync(
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
    /// finished within <see cref="ShutdownStallWindow"/> and waits armed on the clock since it began are still pending,
    /// nothing will move them while the test is awaiting this call.
    /// </summary>
    /// <remarks>
    /// To let a shutdown that needs simulated time finish, start this call, advance the clock, then await it.
    /// </remarks>
    /// <exception cref="SimulationStallException">Shutdown is waiting on the simulation clock.</exception>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var stop = _BeginStop();

        try
        {
            await stop.WaitAsync(ShutdownStallWindow, cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (TimeoutException)
        {
            // Checked below: a shutdown can be slow for reasons that have nothing to do with the clock.
        }

        var pending = Clock.PendingWaitsArmedSince(_stopMark);
        if (pending.Count == 0)
        {
            await stop.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new SimulationStallException(
            $"Shutdown has not finished after {ShutdownStallWindow} and is waiting on the simulation clock: "
                + $"{pending.Count} wait(s) armed since it began are pending, the nearest due in {pending[0]} of "
                + "simulated time. Advance the clock while awaiting StopAsync, or remove the fault that holds shutdown."
        );
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
