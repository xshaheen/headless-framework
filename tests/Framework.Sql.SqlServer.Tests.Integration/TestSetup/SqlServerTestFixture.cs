// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;

namespace Tests.TestSetup;

[CollectionDefinition]
public sealed class SqlServerTestFixture : ICollectionFixture<SqlServerTestFixture>, IAsyncLifetime
{
    private const string _Password = "yourStrong(!)Password";

    // Use Azure SQL Edge for ARM64, SQL Server for x86_64
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

    public string GetConnectionString()
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

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        // Wait for SQL Server to be ready
        await _WaitForSqlServerAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.StopAsync();
        await _container.DisposeAsync();
    }

    private async Task _WaitForSqlServerAsync()
    {
        var connectionString = GetConnectionString();
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
#pragma warning disable ERP022
            }
#pragma warning restore ERP022
        }
        throw new TimeoutException("SQL Server did not become ready in time.");
    }
}
