// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Net.Sockets;
using System.Security.Claims;
using Headless.Checks;
using Headless.Hosting;
using Headless.Hosting.Initialization.Schema;
using Headless.Messaging.Testing;
using Headless.Testing.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Respawn.Graph;
using Xunit;

namespace Headless.Testing.AspNetCore;

/// <summary>
/// A test server wrapping <see cref="WebApplicationFactory{TEntryPoint}"/> with auto-registered
/// <see cref="FakeTimeProvider"/>, DI scope management, readiness waiting, and time advancement helpers.
/// </summary>
/// <remarks>
/// Designed as a building block for collection fixtures. Implements both <see cref="IAsyncLifetime"/>
/// (for xUnit fixture support) and <see cref="IAsyncDisposable"/> (for <c>await using</c> patterns).
/// Dispose is idempotent — safe to call from both xUnit lifecycle and consumer code.
/// </remarks>
[PublicAPI]
public sealed class HeadlessTestServer<TProgram>(
    Action<IServiceCollection>? configureTestServices = null,
    Action<IWebHostBuilder>? configureWebHost = null,
    TimeSpan? initializerTimeout = null
) : IAsyncLifetime
    where TProgram : class
{
    private readonly Action<IServiceCollection>? _configureTestServices = configureTestServices;
    private readonly Action<IWebHostBuilder>? _configureWebHost = configureWebHost;
    private readonly TimeSpan _initializerTimeout = initializerTimeout ?? TimeSpan.FromSeconds(60);
    private readonly List<(Func<IServiceProvider, Task> Check, TimeSpan Timeout)> _readinessChecks = [];
    private Action<DatabaseResetOptions>? _configureDatabaseReset;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly SemaphoreSlim _resetGate = new(1, 1);
    private volatile WebApplicationFactory<TProgram>? _factory;
    private volatile bool _initStarted;
    private DatabaseReset? _databaseReset;
    private DbConnection? _resetConnection;
    private Func<IServiceProvider, DbConnection>? _resetConnectionProvider;
    private Func<Exception, bool>? _additionalTransientExceptionFilter;
    private volatile bool _disposed;
    private FakeTimeProvider? _sharedClock;

    internal Func<DatabaseReset, DbConnection, CancellationToken, Task> ResetAction { get; set; } =
        (reset, connection, cancellationToken) => reset.ResetAsync(connection, cancellationToken);

    /// <summary>The fake time provider registered in the test host.</summary>
    public FakeTimeProvider TimeProvider { get; private set; } = null!;

    /// <summary>The underlying <see cref="WebApplicationFactory{TEntryPoint}"/> for advanced scenarios.</summary>
    /// <remarks>
    /// Do not vary host settings with <c>Factory.WithWebHostBuilder(...)</c>: the derived factory skips the initializer
    /// wait, the readiness checks, the shared clock, and database reset, and it lives until this server is disposed, so
    /// every call keeps another host and its connection pool alive. Use <see cref="DeriveAsync"/> instead.
    /// </remarks>
    public WebApplicationFactory<TProgram> Factory
    {
        get
        {
            // Surface ObjectDisposedException after disposal rather than the misleading
            // "not initialized" message — _factory is also null once disposed.
            Ensure.NotDisposed(_disposed, this);
            return _factory
                ?? throw new InvalidOperationException("Server not initialized. Call InitializeAsync() first.");
        }
    }

    /// <summary>The root service provider of the test host.</summary>
    public IServiceProvider Services => Factory.Services;

    /// <summary>Creates an <see cref="HttpClient"/> backed by the in-memory test server.</summary>
    public HttpClient CreateClient()
    {
        return Factory.CreateClient();
    }

    /// <summary>Creates an <see cref="HttpClient"/> with the specified options.</summary>
    public HttpClient CreateClient(WebApplicationFactoryClientOptions options)
    {
        return Factory.CreateClient(options);
    }

    /// <summary>Advances <see cref="TimeProvider"/> by the specified duration.</summary>
    /// <returns>The new UTC time after advancement.</returns>
    public DateTimeOffset AdvanceTime(TimeSpan delta)
    {
        TimeProvider.Advance(delta);
        return TimeProvider.GetUtcNow();
    }

    /// <summary>Sets <see cref="TimeProvider"/> to the specified UTC time.</summary>
    /// <returns>The new UTC time.</returns>
    public DateTimeOffset SetTime(DateTimeOffset value)
    {
        TimeProvider.SetUtcNow(value);
        return TimeProvider.GetUtcNow();
    }

    /// <summary>
    /// Registers a readiness check that runs after host startup during <see cref="InitializeAsync"/>.
    /// Must be called before <see cref="InitializeAsync"/>.
    /// </summary>
    /// <param name="check">
    /// An async check receiving the root <see cref="IServiceProvider"/>. The check should complete
    /// (return) when the service is ready, or poll internally until ready.
    /// </param>
    /// <param name="timeout">
    /// Maximum time to wait for the check to complete. Defaults to 30 seconds.
    /// </param>
    /// <returns>This instance for method chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when called after <see cref="InitializeAsync"/> has started.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Thrown during <see cref="InitializeAsync"/> when the check does not complete within
    /// <paramref name="timeout"/>.
    /// </exception>
    public HeadlessTestServer<TProgram> AddReadinessCheck(Func<IServiceProvider, Task> check, TimeSpan? timeout = null)
    {
        if (_factory is not null || _initStarted)
        {
            throw new InvalidOperationException("Cannot configure after initialization.");
        }

        _readinessChecks.Add((check, timeout ?? TimeSpan.FromSeconds(30)));
        return this;
    }

    /// <summary>
    /// Starts a second server for the same application with extra settings layered on this server's configuration,
    /// for a test that needs the host configured differently. The variant gets the same lifecycle guarantees as this
    /// server: it awaits every <see cref="IInitializer"/>, runs this server's readiness checks, and supports
    /// <see cref="ResetDatabaseAsync"/> with this server's reset configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The variant runs this server's <c>configureTestServices</c> and <c>configureWebHost</c> delegates first, then
    /// <paramref name="configureTestServices"/> and <paramref name="configureWebHost"/>, so the extra settings win and
    /// the variant reaches the same containers and databases. It shares this server's <see cref="TimeProvider"/>:
    /// advancing either clock advances both, so rows one host writes and the other reads agree on the time.
    /// </para>
    /// <para>
    /// The variant is a separate host with its own background services, connection pools, and DI container. The
    /// caller owns it: dispose it at the end of the test (<c>await using</c>) to stop its host and release its
    /// connections. Disposing it never affects this server.
    /// </para>
    /// </remarks>
    /// <param name="configureTestServices">DI registrations applied after this server's own.</param>
    /// <param name="configureWebHost">Web host configuration applied after this server's own.</param>
    /// <returns>The initialized variant.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this server is not initialized, or when the variant's initialization fails (see
    /// <see cref="InitializeAsync"/>).
    /// </exception>
    /// <exception cref="ObjectDisposedException">Thrown when this server has been disposed.</exception>
    /// <exception cref="TimeoutException">
    /// Thrown when an <c>IInitializer</c> or a readiness check of the variant does not complete in time.
    /// </exception>
    public async Task<HeadlessTestServer<TProgram>> DeriveAsync(
        Action<IServiceCollection>? configureTestServices = null,
        Action<IWebHostBuilder>? configureWebHost = null
    )
    {
        Ensure.NotDisposed(_disposed, this);

        if (_factory is null)
        {
            throw new InvalidOperationException("Server not initialized. Call InitializeAsync() before DeriveAsync().");
        }

        var baseConfigureTestServices = _configureTestServices;
        var baseConfigureWebHost = _configureWebHost;

        var variant = new HeadlessTestServer<TProgram>(
            services =>
            {
                baseConfigureTestServices?.Invoke(services);
                configureTestServices?.Invoke(services);
            },
            builder =>
            {
                baseConfigureWebHost?.Invoke(builder);
                configureWebHost?.Invoke(builder);
            },
            _initializerTimeout
        )
        {
            _sharedClock = TimeProvider,
            _configureDatabaseReset = _configureDatabaseReset,
        };
        variant._readinessChecks.AddRange(_readinessChecks);

        try
        {
            await variant.InitializeAsync().ConfigureAwait(false);
        }
        catch
        {
            await variant.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return variant;
    }

    /// <summary>
    /// Configures database reset via <see cref="DatabaseReset"/>. Must be called before
    /// <see cref="InitializeAsync"/>. Requires <see cref="DatabaseResetOptions.ConnectionProvider"/>
    /// to be set.
    /// </summary>
    /// <param name="configure">Delegate that configures the <see cref="DatabaseResetOptions"/>.</param>
    /// <returns>This instance for method chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when called after <see cref="InitializeAsync"/> has started.
    /// </exception>
    public HeadlessTestServer<TProgram> ConfigureDatabaseReset(Action<DatabaseResetOptions> configure)
    {
        if (_factory is not null || _initStarted)
        {
            throw new InvalidOperationException("Cannot configure after initialization.");
        }

        _configureDatabaseReset = configure;
        return this;
    }

    /// <summary>
    /// Resets the database using the configured <see cref="DatabaseReset"/>. The underlying
    /// <see cref="Respawn.Respawner"/> is created lazily on the first call (after migrations
    /// have completed during host startup). Thread-safe for concurrent test execution.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="ConfigureDatabaseReset"/> was not called or
    /// <see cref="DatabaseResetOptions.ConnectionProvider"/> is <see langword="null"/>.
    /// </exception>
    /// <param name="cancellationToken">
    /// Token used to cancel connection opening, retry delays, and waiting for Respawn. When omitted,
    /// <see cref="TestContext.Current"/> supplies the active test's cancellation token.
    /// </param>
    public async Task ResetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        if (_configureDatabaseReset is null)
        {
            throw new InvalidOperationException(
                "Database reset is not configured. Call ConfigureDatabaseReset() before InitializeAsync()."
            );
        }

        cancellationToken = DatabaseResetOperation.ResolveCancellationToken(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Ensure.NotDisposed(_disposed, this);

        await _resetGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Ensure.NotDisposed(_disposed, this);

            if (_databaseReset is null)
            {
                var options = new DatabaseResetOptions();
                _configureDatabaseReset(options);

                if (options.PreserveHostStateTables)
                {
                    _AddHostStateTables(options);
                }

                _resetConnectionProvider =
                    options.ConnectionProvider
                    ?? throw new InvalidOperationException(
                        $"{nameof(DatabaseResetOptions)}.{nameof(DatabaseResetOptions.ConnectionProvider)} must be set."
                    );
                _additionalTransientExceptionFilter = options.AdditionalTransientExceptionFilter;
                _resetConnection = _resetConnectionProvider(Services);

                try
                {
                    await _resetConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

                    _databaseReset = await DatabaseReset
                        .CreateAsync(_resetConnection, options, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception initializationException)
                {
                    var failedConnection = _resetConnection;
                    _resetConnection = null;

                    try
                    {
                        await failedConnection.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception disposalException)
                    {
                        // Preserve the initialization failure while retaining cleanup diagnostics.
                        initializationException.Data["ResetConnectionDisposalException"] = disposalException;
                    }

                    throw;
                }
            }

            // Retry transient database and transport failures under high test load.
            var retries = 3;
            var replaceConnection = _resetConnection!.State != System.Data.ConnectionState.Open;

            while (retries > 0)
            {
                try
                {
                    if (replaceConnection)
                    {
                        await _ReplaceResetConnectionAsync(cancellationToken).ConfigureAwait(false);
                    }

                    await ResetAction(_databaseReset, _resetConnection!, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex) when (_IsTransientResetException(ex) && retries > 1)
                {
                    retries--;
                    replaceConnection = true;
                    await Task.Delay(TimeSpan.FromMilliseconds(100), System.TimeProvider.System, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (_IsTransientResetException(ex))
                {
                    // Surface retry-exhaustion context instead of a provider-specific transport exception.
                    throw new InvalidOperationException("Database reset failed after 3 attempts.", ex);
                }
            }
        }
        finally
        {
            _resetGate.Release();
        }
    }

    private void _AddHostStateTables(DatabaseResetOptions options)
    {
        // Features declare the tables they write once at startup or keep live while the host runs; the running host
        // never rewrites them, so wiping them breaks every later test.
        foreach (var contribution in Services.GetServices<SchemaContribution>())
        {
            foreach (var table in contribution.HostStateTables)
            {
                options.TablesToPreserve.Add(new Table(contribution.Schema, table));
            }
        }
    }

    private bool _IsTransientResetException(Exception exception)
    {
        return exception is DbException or IOException or SocketException
            || (exception.InnerException is not null && _IsTransientResetException(exception.InnerException))
            || _additionalTransientExceptionFilter?.Invoke(exception) == true;
    }

    private async Task _ReplaceResetConnectionAsync(CancellationToken cancellationToken)
    {
        await _resetConnection!.DisposeAsync().ConfigureAwait(false);
        _resetConnection = _resetConnectionProvider!(Services);
        await _resetConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a DI scope, invokes <paramref name="action"/>, and disposes the scope.</summary>
    /// <param name="action">Delegate that receives the scoped <see cref="IServiceProvider"/>.</param>
    /// <returns>The value returned by <paramref name="action"/>.</returns>
    public async Task<T> ExecuteScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a DI scope, wires <paramref name="principal"/> to an ambient
    /// <see cref="Microsoft.AspNetCore.Http.HttpContext"/> via <c>TestHttpContextExtensions.SetHttpContext</c>,
    /// invokes <paramref name="action"/>, and disposes the scope.
    /// </summary>
    /// <param name="action">Delegate that receives the scoped <see cref="IServiceProvider"/>.</param>
    /// <param name="principal">The claims principal to associate with the request context.</param>
    /// <returns>The value returned by <paramref name="action"/>.</returns>
    public async Task<T> ExecuteScopeAsync<T>(Func<IServiceProvider, Task<T>> action, ClaimsPrincipal principal)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.SetHttpContext(principal, (System.Net.IPAddress?)null);
        return await action(scope.ServiceProvider).ConfigureAwait(false);
    }

    /// <summary>Creates a DI scope, invokes <paramref name="action"/>, and disposes the scope.</summary>
    /// <param name="action">Delegate that receives the scoped <see cref="IServiceProvider"/>.</param>
    public async Task ExecuteScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a DI scope, wires <paramref name="principal"/> to an ambient
    /// <see cref="Microsoft.AspNetCore.Http.HttpContext"/> via <c>TestHttpContextExtensions.SetHttpContext</c>,
    /// invokes <paramref name="action"/>, and disposes the scope.
    /// </summary>
    /// <param name="action">Delegate that receives the scoped <see cref="IServiceProvider"/>.</param>
    /// <param name="principal">The claims principal to associate with the request context.</param>
    public async Task ExecuteScopeAsync(Func<IServiceProvider, Task> action, ClaimsPrincipal principal)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.SetHttpContext(principal, (System.Net.IPAddress?)null);
        await action(scope.ServiceProvider).ConfigureAwait(false);
    }

    /// <summary>
    /// Resets the messaging test harness if it is registered in the service provider, after its in-flight publish
    /// and consume work has settled (see <see cref="MessagingTestHarness.ResetAsync"/>).
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait for in-flight work.</param>
    public Task ResetMessagingHarnessAsync(CancellationToken cancellationToken = default)
    {
        var harness = Services.GetService<MessagingTestHarness>();

        return harness?.ResetAsync(cancellationToken: cancellationToken) ?? Task.CompletedTask;
    }

    /// <summary>Starts the test host, registers the fake time provider, and runs readiness checks.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the host does not register a <see cref="FakeTimeProvider"/>, when an
    /// <c>IInitializer</c> faults, or when <see cref="DisposeAsync"/> has already been called.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Thrown when an <c>IInitializer</c> or a registered readiness check does not complete
    /// within its configured timeout.
    /// </exception>
    public async ValueTask InitializeAsync()
    {
        _initStarted = true;

        if (_factory is not null)
        {
            return;
        }

        Ensure.NotDisposed(_disposed, this);

        await _initGate.WaitAsync().ConfigureAwait(false);

        WebApplicationFactory<TProgram>? factory = null;

        try
        {
            Ensure.NotDisposed(_disposed, this);

            if (_factory is not null)
            {
                return;
            }

#pragma warning disable CA2000 // Ownership transferred to _factory on success; finally disposes on failure
            factory = new ServerFactory(_configureTestServices, _configureWebHost, _sharedClock);
#pragma warning restore CA2000

            // Force host startup — triggers ConfigureTestServices
            _ = factory.Services;

            TimeProvider =
                factory.Services.GetRequiredService<TimeProvider>() as FakeTimeProvider
                ?? throw new InvalidOperationException(
                    "Expected a FakeTimeProvider to be registered. Ensure AddTestTimeProvider() was not overridden by configureTestServices."
                );

            // Await all IInitializer services (e.g. settings/permissions/features sync)
            var initializers = factory.Services.GetServices<IInitializer>();

            foreach (var initializer in initializers)
            {
                using var cts = new CancellationTokenSource(_initializerTimeout);

                try
                {
                    await initializer.WaitForInitializationAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Initializer '{initializer.GetType().Name}' did not complete within {_initializerTimeout.TotalSeconds:F0}s."
                        ),
                        ex
                    );
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new InvalidOperationException(
                        $"Initializer '{initializer.GetType().Name}' faulted during test initialization.",
                        ex
                    );
                }
            }

            // Execute readiness checks sequentially
            foreach (var (check, timeout) in _readinessChecks)
            {
                using var cts = new CancellationTokenSource(timeout);

                try
                {
                    await check(factory.Services).WaitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Readiness check timed out after {timeout.TotalSeconds:F0}s."
                        ),
                        ex
                    );
                }
            }

            _factory = factory;
            factory = null; // ownership transferred; suppress CA2000
        }
        finally
        {
            if (factory is not null)
            {
                await factory.DisposeAsync().ConfigureAwait(false);
            }

            _initGate.Release();
        }
    }

    /// <summary>Disposes the underlying factory, database connection, and test host. Idempotent.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        // Acquire both gates to ensure no initialization or reset is in flight.
        await _initGate.WaitAsync().ConfigureAwait(false);
        await _resetGate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_resetConnection is not null)
            {
                await _resetConnection.DisposeAsync().ConfigureAwait(false);
                _resetConnection = null;
            }

            if (_factory is not null)
            {
                await _factory.DisposeAsync().ConfigureAwait(false);
                _factory = null;
            }
        }
        finally
        {
            _resetGate.Release();
            _initGate.Release();
            _initGate.Dispose();
            _resetGate.Dispose();
        }
    }

    private sealed class ServerFactory(
        Action<IServiceCollection>? configureTestServices,
        Action<IWebHostBuilder>? configureWebHost,
        FakeTimeProvider? sharedClock
    ) : WebApplicationFactory<TProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                if (sharedClock is null)
                {
                    services.AddTestTimeProvider();
                }
                else
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(sharedClock);
                }

                configureTestServices?.Invoke(services);
            });

            configureWebHost?.Invoke(builder);
        }
    }
}
