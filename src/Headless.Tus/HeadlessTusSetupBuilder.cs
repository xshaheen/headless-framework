// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Tus.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using tusdotnet.Interfaces;

namespace Headless.Tus;

/// <summary>
/// Root builder for <c>AddHeadlessTus</c>. It selects the one store the host keeps uploads in and collects what
/// surrounds it: the upload endpoints, file locks, and the expired-uploads cleanup. Every contribution is queued;
/// nothing reaches the service collection until the setup gates pass.
/// </summary>
[PublicAPI]
public sealed class HeadlessTusSetupBuilder
{
    internal HeadlessTusSetupBuilder() { }

    internal List<Action<IServiceCollection>> StoreExtensions { get; } = [];

    internal List<Action<IServiceCollection>> CrossCuttingExtensions { get; } = [];

    internal Dictionary<string, TusEndpointRegistration> Endpoints { get; } = new(StringComparer.Ordinal);

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

    /// <summary>
    /// Declares the default upload endpoint, with options bound from <paramref name="configuration"/> and validated
    /// when the host starts. <c>MapHeadlessTus(pattern)</c> maps it to a route.
    /// </summary>
    /// <param name="configuration">The configuration section to bind <see cref="TusUploadEndpointOptions"/> from.</param>
    /// <returns>The builder of the declared endpoint.</returns>
    /// <remarks>
    /// An application declares at most one default endpoint and any number of named ones. Declaring an endpoint
    /// serves nothing until <c>MapHeadlessTus</c> maps it.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the default upload endpoint is already declared.</exception>
    public HeadlessTusUploadEndpointBuilder AddUploadEndpoint(IConfiguration configuration)
    {
        Argument.IsNotNull(configuration);

        return _AddUploadEndpoint(
            Options.DefaultName,
            services =>
                services.Configure<TusUploadEndpointOptions, TusUploadEndpointOptionsValidator>(
                    configuration,
                    Options.DefaultName
                )
        );
    }

    /// <inheritdoc cref="AddUploadEndpoint(IConfiguration)"/>
    /// <param name="configure">Delegate configuring <see cref="TusUploadEndpointOptions"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessTusUploadEndpointBuilder AddUploadEndpoint(Action<TusUploadEndpointOptions> configure)
    {
        Argument.IsNotNull(configure);

        return _AddUploadEndpoint(
            Options.DefaultName,
            services =>
                services.Configure<TusUploadEndpointOptions, TusUploadEndpointOptionsValidator>(
                    configure,
                    Options.DefaultName
                )
        );
    }

    /// <inheritdoc cref="AddUploadEndpoint(IConfiguration)"/>
    /// <param name="configure">
    /// Delegate configuring <see cref="TusUploadEndpointOptions"/> with access to the service provider.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessTusUploadEndpointBuilder AddUploadEndpoint(
        Action<TusUploadEndpointOptions, IServiceProvider> configure
    )
    {
        Argument.IsNotNull(configure);

        return _AddUploadEndpoint(
            Options.DefaultName,
            services =>
                services.Configure<TusUploadEndpointOptions, TusUploadEndpointOptionsValidator>(
                    configure,
                    Options.DefaultName
                )
        );
    }

    /// <summary>
    /// Declares the upload endpoint <paramref name="name"/>, with options bound from
    /// <paramref name="configuration"/> and validated when the host starts. <c>MapHeadlessTus(pattern, name)</c>
    /// maps it to a route.
    /// </summary>
    /// <param name="name">
    /// The endpoint's name, compared by ordinal. It is also the name of the endpoint's
    /// <see cref="TusUploadEndpointOptions"/> instance.
    /// </param>
    /// <param name="configuration">The configuration section to bind <see cref="TusUploadEndpointOptions"/> from.</param>
    /// <returns>The builder of the declared endpoint.</returns>
    /// <remarks><inheritdoc cref="AddUploadEndpoint(IConfiguration)" path="/remarks"/></remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="name"/> or <paramref name="configuration"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is empty or white space.</exception>
    /// <exception cref="InvalidOperationException">Thrown when an upload endpoint with this name is already declared.</exception>
    public HeadlessTusUploadEndpointBuilder AddUploadEndpoint(string name, IConfiguration configuration)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNull(configuration);

        return _AddUploadEndpoint(
            name,
            services =>
                services.Configure<TusUploadEndpointOptions, TusUploadEndpointOptionsValidator>(configuration, name)
        );
    }

    /// <inheritdoc cref="AddUploadEndpoint(string, IConfiguration)"/>
    /// <param name="configure">Delegate configuring <see cref="TusUploadEndpointOptions"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="name"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public HeadlessTusUploadEndpointBuilder AddUploadEndpoint(string name, Action<TusUploadEndpointOptions> configure)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNull(configure);

        return _AddUploadEndpoint(
            name,
            services => services.Configure<TusUploadEndpointOptions, TusUploadEndpointOptionsValidator>(configure, name)
        );
    }

    /// <inheritdoc cref="AddUploadEndpoint(string, IConfiguration)"/>
    /// <param name="configure">
    /// Delegate configuring <see cref="TusUploadEndpointOptions"/> with access to the service provider.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="name"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public HeadlessTusUploadEndpointBuilder AddUploadEndpoint(
        string name,
        Action<TusUploadEndpointOptions, IServiceProvider> configure
    )
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNull(configure);

        return _AddUploadEndpoint(
            name,
            services => services.Configure<TusUploadEndpointOptions, TusUploadEndpointOptionsValidator>(configure, name)
        );
    }

    private HeadlessTusUploadEndpointBuilder _AddUploadEndpoint(
        string name,
        Action<IServiceCollection> configureOptions
    )
    {
        var endpoint = new TusEndpointRegistration(name);

        // Named options would apply both declarations to one instance, so a repeated name is refused instead of
        // merged without a word.
        if (!Endpoints.TryAdd(name, endpoint))
        {
            throw new InvalidOperationException(
                string.Equals(name, Options.DefaultName, StringComparison.Ordinal)
                    ? "The default upload endpoint is already declared. Declare each upload endpoint once."
                    : $"The upload endpoint \"{name}\" is already declared. Declare each upload endpoint once."
            );
        }

        CrossCuttingExtensions.Add(configureOptions);

        return new HeadlessTusUploadEndpointBuilder(this, endpoint);
    }
}
