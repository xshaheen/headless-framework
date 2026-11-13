// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using tusdotnet.Interfaces;

namespace Headless.Tus;

/// <summary>
/// Root builder for <c>AddHeadlessTus</c>. It selects the one store the host keeps uploads in and collects what
/// surrounds it: file locks and the expired-uploads cleanup. Every contribution is queued; nothing reaches the
/// service collection until the setup gates pass.
/// </summary>
[PublicAPI]
public sealed class HeadlessTusSetupBuilder
{
    internal HeadlessTusSetupBuilder() { }

    internal List<Action<IServiceCollection>> StoreExtensions { get; } = [];

    internal List<Action<IServiceCollection>> CrossCuttingExtensions { get; } = [];

    /// <summary>
    /// Queues a store provider's contribution. Store packages call this from their <c>Use*</c> members; an
    /// application with its own store calls <c>UseStore</c> instead.
    /// </summary>
    /// <typeparam name="TStore">
    /// The store type. <paramref name="action"/> must register it as a singleton under this type.
    /// </typeparam>
    /// <param name="action">The provider's deferred service registration action.</param>
    /// <remarks>
    /// The same instance is also registered as the host's <see cref="ITusStore"/>, so a provider registers only its
    /// own type and whatever that type needs.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public void RegisterStore<TStore>(Action<IServiceCollection> action)
        where TStore : class, ITusStore
    {
        Argument.IsNotNull(action);

        StoreExtensions.Add(services =>
        {
            action(services);

            // A store known only by the interface is already the host's store; forwarding the interface to itself
            // would resolve in a loop.
            if (typeof(TStore) != typeof(ITusStore))
            {
                services.AddSingleton<ITusStore>(static provider => provider.GetRequiredService<TStore>());
            }
        });
    }

    /// <summary>
    /// Queues a contribution applied after the store, for a piece that works with any store, such as a file lock
    /// provider. Unlike <see cref="RegisterStore{TStore}"/>, it does not count as a store.
    /// </summary>
    /// <param name="action">The deferred service registration action.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public void RegisterCrossCuttingExtension(Action<IServiceCollection> action)
    {
        Argument.IsNotNull(action);

        CrossCuttingExtensions.Add(action);
    }

    /// <summary>
    /// Uses an already constructed store, for example tusdotnet's <c>TusDiskStore</c>, as the host's
    /// <see cref="ITusStore"/>. The instance is also resolvable as <typeparamref name="TStore"/>.
    /// </summary>
    /// <typeparam name="TStore">The store type.</typeparam>
    /// <param name="store">The store instance. The container does not dispose it.</param>
    /// <returns>The builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/> is <see langword="null"/>.</exception>
    public HeadlessTusSetupBuilder UseStore<TStore>(TStore store)
        where TStore : class, ITusStore
    {
        Argument.IsNotNull(store);

        RegisterStore<TStore>(services => services.AddSingleton(store));

        return this;
    }

    /// <summary>
    /// Uses the store <paramref name="factory"/> builds, once per host, as the host's <see cref="ITusStore"/>. The
    /// instance is also resolvable as <typeparamref name="TStore"/>.
    /// </summary>
    /// <typeparam name="TStore">The store type.</typeparam>
    /// <param name="factory">Builds the store from the host's services.</param>
    /// <returns>The builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is <see langword="null"/>.</exception>
    public HeadlessTusSetupBuilder UseStore<TStore>(Func<IServiceProvider, TStore> factory)
        where TStore : class, ITusStore
    {
        Argument.IsNotNull(factory);

        RegisterStore<TStore>(services => services.AddSingleton(factory));

        return this;
    }

    /// <summary>
    /// Configures the job that removes expired incomplete uploads. The job runs by default; set
    /// <see cref="TusExpiredUploadsCleanupOptions.Enabled"/> to <see langword="false"/> to turn it off on this host.
    /// </summary>
    /// <param name="configure">Delegate that mutates the cleanup options.</param>
    /// <returns>The builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessTusSetupBuilder ConfigureExpiredUploadsCleanup(Action<TusExpiredUploadsCleanupOptions> configure)
    {
        Argument.IsNotNull(configure);

        // No validator here: AddHeadlessTus attaches it once, whether or not this member is called.
        CrossCuttingExtensions.Add(services => services.Configure(configure));

        return this;
    }
}
