// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.InMemoryQueue;
using Headless.Messaging.InMemoryStorage;
using Headless.Messaging.Internal;
using Headless.Messaging.Serialization;
using Headless.Messaging.Testing.Internal;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
///         setup.Subscribe&lt;MyHandler&gt;("my-topic");
///     });
/// });
///
/// await harness.Publisher.PublishAsync(new MyMessage { ... });
/// var recorded = await harness.WaitForConsumed&lt;MyMessage&gt;();
/// </code>
/// </para>
/// </remarks>
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

        // Disable parallelism for deterministic single-threaded test execution
        services.Configure<MessagingOptions>(opt =>
        {
            opt.EnablePublishParallelSend = false;
            opt.EnableSubscriberParallelExecute = false;
        });

        var store = new MessageObservationStore();
        services.AddSingleton(store);

        _DecorateTransport(services, store);
        _DecoratePipeline(services, store);

        services.TryAddSingleton<IMessagePublisher>(sp => sp.GetRequiredService<IDirectPublisher>());

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
    ) =>
        _store.WaitForAsync(
            typeof(T),
            MessageObservationType.Published,
            predicate: null,
            timeout ?? DefaultTimeout,
            cancellationToken
        );

    /// <summary>
    /// Waits until a published message of type <typeparamref name="T"/> satisfies <paramref name="predicate"/>,
    /// or throws <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForPublished<T>(
        Func<T, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    ) =>
        _store.WaitForAsync(
            typeof(T),
            MessageObservationType.Published,
            obj => predicate((T)obj),
            timeout ?? DefaultTimeout,
            cancellationToken
        );

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
    ) =>
        _store.WaitForAsync(
            typeof(T),
            MessageObservationType.Consumed,
            predicate: null,
            timeout ?? DefaultTimeout,
            cancellationToken
        );

    /// <summary>
    /// Waits until a consumed message of type <typeparamref name="T"/> satisfies <paramref name="predicate"/>,
    /// or throws <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForConsumed<T>(
        Func<T, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    ) =>
        _store.WaitForAsync(
            typeof(T),
            MessageObservationType.Consumed,
            obj => predicate((T)obj),
            timeout ?? DefaultTimeout,
            cancellationToken
        );

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
    ) =>
        _store.WaitForAsync(
            typeof(T),
            MessageObservationType.Faulted,
            predicate: null,
            timeout ?? DefaultTimeout,
            cancellationToken
        );

    /// <summary>
    /// Waits until a faulted message of type <typeparamref name="T"/> satisfies <paramref name="predicate"/>,
    /// or throws <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForFaulted<T>(
        Func<T, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    ) =>
        _store.WaitForAsync(
            typeof(T),
            MessageObservationType.Faulted,
            obj => predicate((T)obj),
            timeout ?? DefaultTimeout,
            cancellationToken
        );

    // -------------------------------------------------------------------------
    // State management
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resets all in-memory messaging state: recorded observations, pending queue messages,
    /// and persisted storage data. Call between tests when using a shared fixture.
    /// </summary>
    /// <remarks>
    /// Assumes no active message processing is in flight. Call this at test boundaries
    /// (e.g., between tests) when consumers are idle. If you need to ensure quiescence
    /// programmatically, pause consumers via <c>PauseAsync</c> before calling this method.
    /// </remarks>
    public void Clear()
    {
        _store.Clear();

        ServiceProvider.GetService<MemoryQueue>()?.DrainAllPendingMessages();
        ServiceProvider.GetService<InMemoryDataStorage>()?.Clear();
    }

    // -------------------------------------------------------------------------
    // Service access
    // -------------------------------------------------------------------------

    /// <summary>Returns a publisher backed by the in-memory transport.</summary>
    public IMessagePublisher Publisher => ServiceProvider.GetRequiredService<IMessagePublisher>();

    /// <summary>Resolves an arbitrary service from the harness container.</summary>
    public T GetRequiredService<T>()
        where T : notnull => ServiceProvider.GetRequiredService<T>();

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
                    + "Call setup.UseInMemoryMessageQueue() inside your AddHeadlessMessaging callback."
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

    private static void _DecorateTransport(IServiceCollection services, MessageObservationStore store)
    {
        var original = services.FirstOrDefault(d => d.ServiceType == typeof(ITransport));

        if (original is null)
        {
            return;
        }

        services.Remove(original);

        services.AddSingleton<ITransport>(sp =>
        {
            var inner = _ResolveFromDescriptor<ITransport>(sp, original);
            var serializer = sp.GetRequiredService<ISerializer>();
            return new RecordingTransport(inner, store, serializer);
        });
    }

    private static void _DecoratePipeline(IServiceCollection services, MessageObservationStore store)
    {
        var original = services.FirstOrDefault(d => d.ServiceType == typeof(IConsumeExecutionPipeline));

        if (original is null)
        {
            return;
        }

        services.Remove(original);

        services.AddSingleton<IConsumeExecutionPipeline>(sp =>
        {
            var inner = _ResolveFromDescriptor<IConsumeExecutionPipeline>(sp, original);
            return new RecordingConsumeExecutionPipeline(inner, store);
        });
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
