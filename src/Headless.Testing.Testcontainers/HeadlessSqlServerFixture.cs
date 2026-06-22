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
/// Startup polls the TCP port and attempts a login for up to 60 seconds after the container
/// log reports readiness, to guard against race conditions in slow CI environments.
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

    private readonly IContainer _container = new ContainerBuilder(_Image)
        .WithPortBinding(1433, true)
        .WithEnvironment("ACCEPT_EULA", "Y")
        .WithEnvironment("MSSQL_SA_PASSWORD", _Password)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("SQL Server is now ready"))
        .Build();

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

    /// <summary>Stops and disposes the SQL Server container.</summary>
    public async ValueTask DisposeAsync()
    {
        await _container.StopAsync().ConfigureAwait(false);
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
