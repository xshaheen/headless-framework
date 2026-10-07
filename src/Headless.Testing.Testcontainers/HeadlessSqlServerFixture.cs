// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// SQL Server test fixture that automatically selects the correct container image based on CPU architecture.
/// Uses <see cref="TestImages.AzureSqlEdge"/> on ARM64 (Apple Silicon) and
/// <see cref="TestImages.MsSqlServer"/> on x86_64, unless <see cref="ImageOverrideVariable"/> names another image.
/// </summary>
/// <remarks>
/// <para>
/// Startup polls the TCP port and attempts a login for up to 60 seconds after the container
/// log reports readiness, to guard against race conditions in slow CI environments.
/// </para>
/// <para>
/// The container is created with reuse enabled, so when the host opts in
/// (<c>testcontainers.reuse.enable=true</c> in <c>~/.testcontainers.properties</c>, or
/// <c>TESTCONTAINERS_REUSE_ENABLE=true</c>) repeated local runs reattach to the already-warm SQL Server
/// instead of paying the cold-start boot + login wait each time — the single biggest local-iteration cost
/// for the SQL Server integration suites. CI leaves reuse disabled, so reuse becomes a no-op and Ryuk
/// reaps the container as usual.
/// </para>
/// <para>
/// The lifecycle has the same shape as the <c>ContainerFixture</c>-based fixtures: xUnit calls
/// <see cref="IAsyncLifetime.InitializeAsync"/> explicitly, and a subclass overrides <see cref="InitializeAsync"/> to
/// create its own database or clear what a reused container left, after <c>base.InitializeAsync()</c> returns, and
/// overrides <see cref="DisposeAsyncCore"/> for teardown.
/// </para>
/// </remarks>
[PublicAPI]
public class HeadlessSqlServerFixture : IAsyncLifetime
{
    private const string _Password = "YourStrong@Passw0rd";
    private static readonly TimeSpan _StartupTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan _StartupPollInterval = TimeSpan.FromMilliseconds(250);
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    /// <summary>
    /// The environment variable that, when set, replaces the architecture-selected image. It lets an ARM64 host run
    /// the suites against real SQL Server 2022 under x86_64 emulation (for example
    /// <c>mcr.microsoft.com/mssql/server:2022-latest</c> under Rosetta), since Azure SQL Edge is a different engine
    /// build. The image must log "SQL Server is now ready" and accept the <c>sa</c> password the fixture sets.
    /// </summary>
    public const string ImageOverrideVariable = "HEADLESS_SQLSERVER_IMAGE";

    // Use Azure SQL Edge for ARM64 (e.g., Apple Silicon), SQL Server 2022 for x86_64
    private static readonly string _Image =
        Environment.GetEnvironmentVariable(ImageOverrideVariable) is { Length: > 0 } image ? image
        : RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? TestImages.AzureSqlEdge
        : TestImages.MsSqlServer;

    private readonly IContainer _container;

    public HeadlessSqlServerFixture()
    {
        // Per-project, per-checkout reuse labels so each integration project in each checkout reuses its OWN container
        // instead of colliding on the shared `master` database under parallel module execution or across worktrees.
        // See ReuseLabel for the keying rationale.
        _container = new ContainerBuilder(_Image)
            .WithPortBinding(1433, assignRandomHostPort: true)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", _Password)
            .WithLabel(ReuseLabel.Key, ReuseLabel.For(this))
            .WithLabel(ReuseLabel.CheckoutKey, ReuseLabel.Checkout)
            .WithReuse(true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("SQL Server is now ready"))
            .Build();
    }

    /// <summary>Gets the SQL Server connection string.</summary>
    public string ConnectionString
    {
        get
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = $"{_container.Hostname},{_container.GetMappedPublicPort(1433)}",
                InitialCatalog = "master",
                UserID = "sa",
                Password = _Password,
                TrustServerCertificate = true,
            };
            return builder.ConnectionString;
        }
    }

    /// <summary>
    /// Starts the SQL Server container and polls until a login attempt succeeds or the
    /// startup timeout (60 seconds) elapses.
    /// </summary>
    /// <exception cref="TimeoutException">
    /// Thrown when no login succeeds within the startup timeout.
    /// </exception>
    protected virtual async ValueTask InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        await _WaitUntilLoginSucceedsAsync().ConfigureAwait(false);
    }

    ValueTask IAsyncLifetime.InitializeAsync()
    {
        return InitializeAsync();
    }

    /// <summary>Disposes the fixture through <see cref="DisposeAsyncCore"/>.</summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Disposes the SQL Server container.</summary>
    /// <remarks>
    /// Mirrors Testcontainers' own <c>ContainerFixture</c> teardown: a single <c>DisposeAsync</c> on the container.
    /// With reuse enabled the engine stops-but-keeps the container so the next run reattaches by reuse hash;
    /// with reuse disabled (CI) the same call removes it (and Ryuk backstops). An explicit <c>StopAsync</c>
    /// before dispose is redundant and only adds teardown latency.
    /// </remarks>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        await _container.DisposeAsync().ConfigureAwait(false);
    }

    private async Task _WaitUntilLoginSucceedsAsync()
    {
        var deadline = _timeProvider.GetUtcNow().Add(_StartupTimeout);
        Exception? lastException = null;

        while (_timeProvider.GetUtcNow() < deadline)
        {
            try
            {
                await using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);

                return;
            }
            catch (SqlException ex)
            {
                lastException = ex;
                await _timeProvider.Delay(_StartupPollInterval, CancellationToken.None).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                lastException = ex;
                await _timeProvider.Delay(_StartupPollInterval, CancellationToken.None).ConfigureAwait(false);
            }
        }

        throw new TimeoutException(
            "SQL Server container did not accept logins before the startup timeout.",
            lastException
        );
    }
}
