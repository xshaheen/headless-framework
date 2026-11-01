// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Encryption;
using Headless.Testing.Testcontainers;
using Microsoft.Data.SqlClient;

namespace Tests;

/// <summary>SQL Server leaf fixture for the encryption key-store conformance suite.</summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerEncryptionFixture
    : HeadlessSqlServerFixture,
        ICollectionFixture<SqlServerEncryptionFixture>,
        IEncryptionFixture
{
    private const string _Database = "encryption_test";

    /// <summary>Connection string of the database that holds the keys (the base one points at master).</summary>
    public string KeysConnectionString =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = _Database, MaxPoolSize = 30 }.ToString();

    // The database can only be created once the container accepts logins.
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        await using var master = new SqlConnection(ConnectionString);
        await master.OpenAsync(CancellationToken.None);
        await using var create = new SqlCommand(
            $"IF DB_ID(N'{_Database}') IS NULL CREATE DATABASE [{_Database}];",
            master
        );
        await create.ExecuteNonQueryAsync(CancellationToken.None);
    }

    public void ConfigureKeyStore(HeadlessEncryptionSetupBuilder setup, string tableName)
    {
        setup.UseSqlServer(options =>
        {
            options.ConnectionString = KeysConnectionString;
            options.TableName = tableName;
        });
    }

    public void ConfigureUnreachableKeyStore(HeadlessEncryptionSetupBuilder setup)
    {
        setup.UseSqlServer(
            "Server=127.0.0.1,1;Database=missing;User Id=sa;Password=placeholder-never-used;Connect Timeout=2;TrustServerCertificate=True"
        );
    }
}
