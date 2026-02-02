// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// SQL Server test fixture that automatically selects the correct container image based on CPU architecture.
/// Uses Azure SQL Edge for ARM64 (Apple Silicon) and SQL Server 2022 for x86_64.
/// </summary>
public class HeadlessSqlServerFixture : IAsyncLifetime
{
    private const string _Password = "YourStrong@Passw0rd";

    // Use Azure SQL Edge for ARM64 (e.g., Apple Silicon), SQL Server 2022 for x86_64
    private static readonly string _Image =
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "mcr.microsoft.com/azure-sql-edge:latest"
            : "mcr.microsoft.com/mssql/server:2022-latest";

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

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await _WaitForSqlServerAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.StopAsync();
        await _container.DisposeAsync();
    }

    private async Task _WaitForSqlServerAsync()
    {
        var connectionString = ConnectionString;
        for (var i = 0; i < 30; i++)
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                return;
            }
            catch
            {
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException("SQL Server did not become ready in time.");
    }
}
