// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
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
    private readonly List<(Func<IServiceProvider, Task> Check, TimeSpan Timeout)> _readinessChecks = [];
    private Action<DatabaseResetOptions>? _configureDatabaseReset;
    private readonly SemaphoreSlim _resetGate = new(1, 1);
    private WebApplicationFactory<TProgram>? _factory;
    private DatabaseReset? _databaseReset;
    private DbConnection? _resetConnection;
    private bool _disposed;

    public HeadlessTestServer(
        Action<IServiceCollection>? configureTestServices = null,
        Action<IWebHostBuilder>? configureWebHost = null
    )
    {
        _configureTestServices = configureTestServices;
        _configureWebHost = configureWebHost;
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
    public void AdvanceTime(TimeSpan delta) => TimeProvider.Advance(delta);

    /// <summary>Sets <see cref="TimeProvider"/> to the specified UTC time.</summary>
    public void SetTime(DateTimeOffset value) => TimeProvider.SetUtcNow(value);

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

        await _resetGate.WaitAsync().ConfigureAwait(false);

        try
        {
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

            await _databaseReset.ResetAsync(_resetConnection!).ConfigureAwait(false);
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

    /// <summary>Creates a DI scope, invokes the delegate, and disposes the scope.</summary>
    public async Task ExecuteScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider).ConfigureAwait(false);
    }

    /// <summary>Starts the test host, registers the fake time provider, and runs readiness checks.</summary>
    public async ValueTask InitializeAsync()
    {
        try
        {
            _factory = new ServerFactory(_configureTestServices, _configureWebHost);

            // Force host startup — triggers ConfigureTestServices
            _ = _factory.Services;

            TimeProvider = (FakeTimeProvider)_factory.Services.GetRequiredService<TimeProvider>();

            // Execute readiness checks sequentially
            foreach (var (check, timeout) in _readinessChecks)
            {
                using var cts = new CancellationTokenSource(timeout);

                try
                {
                    await check(Services).WaitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    throw new TimeoutException($"Readiness check timed out after {timeout.TotalSeconds:F0}s.");
                }
            }
        }
        catch
        {
            // Clean up partially-created factory on failure
            if (_factory is not null)
            {
                await _factory.DisposeAsync().ConfigureAwait(false);
                _factory = null;
            }

            throw;
        }
    }

    /// <summary>Disposes the underlying factory, database connection, and test host. Idempotent.</summary>
    public async ValueTask DisposeAsync()
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

        _resetGate.Dispose();
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
