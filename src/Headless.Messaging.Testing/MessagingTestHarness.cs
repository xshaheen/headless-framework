// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
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
///     services.AddHeadlessMessaging(options =>
///     {
///         options.Subscribe&lt;MyHandler&gt;("my-topic");
///     });
/// });
///
/// await harness.Publisher.PublishAsync(new MyMessage { ... });
/// var recorded = await harness.WaitForConsumed&lt;MyMessage&gt;(TimeSpan.FromSeconds(5));
/// </code>
/// </para>
/// </remarks>
public sealed class MessagingTestHarness : IAsyncDisposable
{
    private readonly ServiceProvider _sp;
    private readonly MessageObservationStore _store;

    private MessagingTestHarness(ServiceProvider sp, MessageObservationStore store)
    {
        _sp = sp;
        _store = store;
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
        var services = new ServiceCollection();

        services.AddLogging();

        // Let the caller register AddHeadlessMessaging + consumers
        configure(services);

        // Force in-memory transport and storage (no-ops if already registered due to TryAdd,
        // but the extensions use AddSingleton so we need to call them on MessagingOptions —
        // they're already expected to be called by the caller inside AddHeadlessMessaging.
        // If the caller omitted them, inject the required markers so bootstrap doesn't throw.
        _EnsureInMemoryInfrastructure(services);

        // Disable parallelism for deterministic single-threaded test execution
        services.Configure<MessagingOptions>(opt =>
        {
            opt.EnablePublishParallelSend = false;
            opt.EnableSubscriberParallelExecute = false;
        });

        // Register the shared observation store as singleton
        var store = new MessageObservationStore();
        services.AddSingleton(store);

        // Decorate ITransport with RecordingTransport
        _DecorateTransport(services, store);

        // Decorate IConsumeExecutionPipeline with RecordingConsumeExecutionPipeline
        _DecoratePipeline(services, store);

        // Expose IMessagePublisher as an alias for IDirectPublisher so harness.Publisher resolves correctly.
        // AddHeadlessMessaging registers IDirectPublisher but not IMessagePublisher directly.
        services.TryAddSingleton<IMessagePublisher>(sp => sp.GetRequiredService<IDirectPublisher>());

        var sp = services.BuildServiceProvider();

        // Bootstrap without hosted-service infrastructure
        var bootstrapper = sp.GetRequiredService<IBootstrapper>();
        await bootstrapper.BootstrapAsync(cancellationToken).ConfigureAwait(false);

        return new MessagingTestHarness(sp, store);
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
    public Task<RecordedMessage> WaitForPublished<T>(TimeSpan timeout, CancellationToken ct = default) =>
        _store.WaitForAsync(typeof(T), MessageObservationType.Published, predicate: null, timeout, ct);

    /// <summary>
    /// Waits until a published message of type <typeparamref name="T"/> satisfies <paramref name="predicate"/>,
    /// or throws <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForPublished<T>(
        Func<T, bool> predicate,
        TimeSpan timeout,
        CancellationToken ct = default
    ) => _store.WaitForAsync(typeof(T), MessageObservationType.Published, obj => predicate((T)obj), timeout, ct);

    // -------------------------------------------------------------------------
    // Awaitable assertions — Consumed
    // -------------------------------------------------------------------------

    /// <summary>
    /// Waits until a message of type <typeparamref name="T"/> is consumed successfully,
    /// or throws <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForConsumed<T>(TimeSpan timeout, CancellationToken ct = default) =>
        _store.WaitForAsync(typeof(T), MessageObservationType.Consumed, predicate: null, timeout, ct);

    /// <summary>
    /// Waits until a consumed message of type <typeparamref name="T"/> satisfies <paramref name="predicate"/>,
    /// or throws <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForConsumed<T>(
        Func<T, bool> predicate,
        TimeSpan timeout,
        CancellationToken ct = default
    ) => _store.WaitForAsync(typeof(T), MessageObservationType.Consumed, obj => predicate((T)obj), timeout, ct);

    // -------------------------------------------------------------------------
    // Awaitable assertions — Faulted
    // -------------------------------------------------------------------------

    /// <summary>
    /// Waits until processing of a message of type <typeparamref name="T"/> faults,
    /// or throws <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForFaulted<T>(TimeSpan timeout, CancellationToken ct = default) =>
        _store.WaitForAsync(typeof(T), MessageObservationType.Faulted, predicate: null, timeout, ct);

    /// <summary>
    /// Waits until a faulted message of type <typeparamref name="T"/> satisfies <paramref name="predicate"/>,
    /// or throws <see cref="MessageObservationTimeoutException"/> if <paramref name="timeout"/> elapses.
    /// </summary>
    public Task<RecordedMessage> WaitForFaulted<T>(
        Func<T, bool> predicate,
        TimeSpan timeout,
        CancellationToken ct = default
    ) => _store.WaitForAsync(typeof(T), MessageObservationType.Faulted, obj => predicate((T)obj), timeout, ct);

    // -------------------------------------------------------------------------
    // Service access
    // -------------------------------------------------------------------------

    /// <summary>Returns a publisher backed by the in-memory transport.</summary>
    public IMessagePublisher Publisher => _sp.GetRequiredService<IMessagePublisher>();

    /// <summary>Resolves an arbitrary service from the harness container.</summary>
    public T GetRequiredService<T>()
        where T : notnull => _sp.GetRequiredService<T>();

    /// <summary>Provides direct access to the harness <see cref="IServiceProvider"/>.</summary>
    public IServiceProvider ServiceProvider => _sp;

    // -------------------------------------------------------------------------
    // Disposal
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Cancel the bootstrapper first — its registered callback stops all processing servers.
        // Directly calling DisposeAsync on processors before the bootstrapper cancels its CTS
        // causes a race where the CTS cancel-callback fires on an already-disposed processor CTS.
        var bootstrapper = _sp.GetService<IBootstrapper>();

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
        await _sp.DisposeAsync().ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Ensures in-memory infrastructure markers are present so the bootstrapper's
    /// requirement check does not throw when the caller already called
    /// <c>UseInMemoryMessageQueue()</c> and <c>UseInMemoryStorage()</c> inside their
    /// <c>AddHeadlessMessaging</c> callback.  If the markers are missing (caller forgot),
    /// this is a no-op — the bootstrapper will surface a clear error.
    /// </summary>
    private static void _EnsureInMemoryInfrastructure(IServiceCollection services)
    {
        // Both extensions register their own marker services via AddSingleton (not TryAdd),
        // so a second call would add duplicates.  We only call them if not already present.
        var hasQueueMarker = services.Any(d =>
            d.ServiceType == typeof(MessageQueueMarkerService) && d.Lifetime == ServiceLifetime.Singleton
        );

        var hasStorageMarker = services.Any(d =>
            d.ServiceType == typeof(MessageStorageMarkerService) && d.Lifetime == ServiceLifetime.Singleton
        );

        if (!hasQueueMarker || !hasStorageMarker)
        {
            // Re-invoking AddHeadlessMessaging here is not safe, so surface a clear error.
            if (!hasQueueMarker)
            {
                throw new InvalidOperationException(
                    "MessagingTestHarness requires an in-memory transport. "
                        + "Call options.UseInMemoryMessageQueue() inside your AddHeadlessMessaging callback."
                );
            }

            if (!hasStorageMarker)
            {
                throw new InvalidOperationException(
                    "MessagingTestHarness requires an in-memory storage. "
                        + "Call options.UseInMemoryStorage() inside your AddHeadlessMessaging callback."
                );
            }
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
