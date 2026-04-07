// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Security.Claims;
using Headless.Hosting.Initialization;
using Headless.Messaging.Testing;
using Headless.Testing.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
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
public sealed class HeadlessTestServer<TProgram> : IAsyncLifetime, IAsyncDisposable
    where TProgram : class
{
    private readonly Action<IServiceCollection>? _configureTestServices;
    private readonly Action<IWebHostBuilder>? _configureWebHost;
    private readonly TimeSpan _initializerTimeout;
    private readonly List<(Func<IServiceProvider, Task> Check, TimeSpan Timeout)> _readinessChecks = [];
    private Action<DatabaseResetOptions>? _configureDatabaseReset;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly SemaphoreSlim _resetGate = new(1, 1);
    private volatile WebApplicationFactory<TProgram>? _factory;
    private volatile bool _initStarted;
    private DatabaseReset? _databaseReset;
    private DbConnection? _resetConnection;
    private volatile bool _disposed;

    internal Func<DatabaseReset, DbConnection, Task> ResetAction { get; set; } = (r, c) => r.ResetAsync(c);

    public HeadlessTestServer(
        Action<IServiceCollection>? configureTestServices = null,
        Action<IWebHostBuilder>? configureWebHost = null,
        TimeSpan? initializerTimeout = null
    )
    {
        _configureTestServices = configureTestServices;
        _configureWebHost = configureWebHost;
        _initializerTimeout = initializerTimeout ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>The fake time provider registered in the test host.</summary>
    public FakeTimeProvider TimeProvider { get; private set; } = null!;

    /// <summary>The underlying <see cref="WebApplicationFactory{TEntryPoint}"/> for advanced scenarios.</summary>
    public WebApplicationFactory<TProgram> Factory =>
        _factory ?? throw new InvalidOperationException("Server not initialized. Call InitializeAsync() first.");

    /// <summary>The root service provider of the test host.</summary>
    public IServiceProvider Services => Factory.Services;

    /// <summary>Creates an <see cref="HttpClient"/> backed by the in-memory test server.</summary>
    public HttpClient CreateClient() => Factory.CreateClient();

    /// <summary>Creates an <see cref="HttpClient"/> with the specified options.</summary>
    public HttpClient CreateClient(WebApplicationFactoryClientOptions options) => Factory.CreateClient(options);

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
    /// Throws <see cref="TimeoutException"/> if exceeded.
    /// </param>
    public HeadlessTestServer<TProgram> WaitForReadiness(Func<IServiceProvider, Task> check, TimeSpan? timeout = null)
    {
        if (_factory is not null || _initStarted)
        {
            throw new InvalidOperationException("Cannot configure after initialization.");
        }

        _readinessChecks.Add((check, timeout ?? TimeSpan.FromSeconds(30)));
        return this;
    }

    /// <summary>
    /// Configures database reset via <see cref="DatabaseReset"/>. Must be called before
    /// <see cref="InitializeAsync"/>. Requires <see cref="DatabaseResetOptions.ConnectionProvider"/>
    /// to be set.
    /// </summary>
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
    /// <see cref="DatabaseResetOptions.ConnectionProvider"/> is <c>null</c>.
    /// </exception>
    public async Task ResetDatabaseAsync()
    {
        if (_configureDatabaseReset is null)
        {
            throw new InvalidOperationException(
                "Database reset is not configured. Call ConfigureDatabaseReset() before InitializeAsync()."
            );
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        await _resetGate.WaitAsync().ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_databaseReset is null)
            {
                var options = new DatabaseResetOptions();
                _configureDatabaseReset(options);

                if (options.ConnectionProvider is null)
                {
                    throw new InvalidOperationException(
                        $"{nameof(DatabaseResetOptions)}.{nameof(DatabaseResetOptions.ConnectionProvider)} must be set."
                    );
                }

                _resetConnection = options.ConnectionProvider(Services);
                await _resetConnection.OpenAsync().ConfigureAwait(false);

                _databaseReset = await DatabaseReset.CreateAsync(_resetConnection, options).ConfigureAwait(false);
            }

            // Retry for PostgreSQL connection flakiness under high test load
            var retries = 3;
            while (retries > 0)
            {
                try
                {
                    await ResetAction(_databaseReset, _resetConnection!).ConfigureAwait(false);
                    break;
                }
                catch (DbException) when (retries > 1)
                {
                    retries--;
                    await Task.Delay(100).ConfigureAwait(false);

                    // Re-open if closed or broken
                    if (_resetConnection!.State != System.Data.ConnectionState.Open)
                    {
                        await _resetConnection.OpenAsync().ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            _resetGate.Release();
        }
    }

    /// <summary>Creates a DI scope, invokes the delegate, and disposes the scope.</summary>
    public async Task<T> ExecuteScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a DI scope, wires the <paramref name="principal"/> to <see cref="Microsoft.AspNetCore.Http.HttpContext"/>,
    /// invokes the delegate, and disposes the scope.
    /// </summary>
    public async Task<T> ExecuteScopeAsync<T>(Func<IServiceProvider, Task<T>> action, ClaimsPrincipal principal)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.SetHttpContext(principal, (System.Net.IPAddress?)null);
        return await action(scope.ServiceProvider).ConfigureAwait(false);
    }

    /// <summary>Creates a DI scope, invokes the delegate, and disposes the scope.</summary>
    public async Task ExecuteScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a DI scope, wires the <paramref name="principal"/> to <see cref="Microsoft.AspNetCore.Http.HttpContext"/>,
    /// invokes the delegate, and disposes the scope.
    /// </summary>
    public async Task ExecuteScopeAsync(Func<IServiceProvider, Task> action, ClaimsPrincipal principal)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.SetHttpContext(principal, (System.Net.IPAddress?)null);
        await action(scope.ServiceProvider).ConfigureAwait(false);
    }

    /// <summary>
    /// Resets the messaging test harness if it is registered in the service provider.
    /// </summary>
    public void ResetMessagingHarness()
    {
        var harness = Services.GetService<MessagingTestHarness>();
        harness?.Clear();
    }

    /// <summary>Starts the test host, registers the fake time provider, and runs readiness checks.</summary>
    public async ValueTask InitializeAsync()
    {
        _initStarted = true;

        if (_factory is not null)
        {
            return;
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        await _initGate.WaitAsync().ConfigureAwait(false);

        WebApplicationFactory<TProgram>? factory = null;

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_factory is not null)
            {
                return;
            }

#pragma warning disable CA2000 // Ownership transferred to _factory on success; finally disposes on failure
            factory = new ServerFactory(_configureTestServices, _configureWebHost);
#pragma warning restore CA2000

            // Force host startup — triggers ConfigureTestServices
            _ = factory.Services;

            TimeProvider = (FakeTimeProvider)factory.Services.GetRequiredService<TimeProvider>();

            // Await all IInitializer services (e.g. settings/permissions/features sync)
            var initializers = factory.Services.GetServices<IInitializer>();

            foreach (var initializer in initializers)
            {
                using var cts = new CancellationTokenSource(_initializerTimeout);

                try
                {
                    await initializer.WaitForInitializationAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"Initializer '{initializer.GetType().Name}' did not complete within {_initializerTimeout.TotalSeconds:F0}s."
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
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    throw new TimeoutException($"Readiness check timed out after {timeout.TotalSeconds:F0}s.");
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
        Action<IWebHostBuilder>? configureWebHost
    ) : WebApplicationFactory<TProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddTestTimeProvider();
                configureTestServices?.Invoke(services);
            });

            configureWebHost?.Invoke(builder);
        }
    }
}
