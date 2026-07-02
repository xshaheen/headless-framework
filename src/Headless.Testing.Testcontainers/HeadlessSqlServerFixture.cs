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
/// <see cref="TestImages.MsSqlServer"/> on x86_64.
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
/// </remarks>
[PublicAPI]
public class HeadlessSqlServerFixture : IAsyncLifetime
{
    private const string _Password = "YourStrong@Passw0rd";
    private static readonly TimeSpan _StartupTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan _StartupPollInterval = TimeSpan.FromMilliseconds(250);
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    // Use Azure SQL Edge for ARM64 (e.g., Apple Silicon), SQL Server 2022 for x86_64
    private static readonly string _Image =
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? TestImages.AzureSqlEdge : TestImages.MsSqlServer;

    private readonly IContainer _container;

    public HeadlessSqlServerFixture()
    {
        // Per-project reuse label so each integration project reuses its OWN container instead of colliding on
        // the shared `master` database under parallel module execution. See ReuseLabel for the keying rationale.
        _container = new ContainerBuilder(_Image)
            .WithPortBinding(1433, assignRandomHostPort: true)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", _Password)
            .WithLabel(ReuseLabel.Key, ReuseLabel.For(this))
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
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        await _WaitUntilLoginSucceedsAsync().ConfigureAwait(false);
    }

    /// <summary>Disposes the SQL Server container.</summary>
    /// <remarks>
    /// Mirrors Testcontainers' own <c>ContainerFixture</c> teardown: a single <c>DisposeAsync</c> on the container.
    /// With reuse enabled the engine stops-but-keeps the container so the next run reattaches by reuse hash;
    /// with reuse disabled (CI) the same call removes it (and Ryuk backstops). An explicit <c>StopAsync</c>
    /// before dispose is redundant and only adds teardown latency.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
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
