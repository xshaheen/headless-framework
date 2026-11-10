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
    /// <summary>Connection string of the fixture's own database, which holds the keys.</summary>
    public string KeysConnectionString =>
        new SqlConnectionStringBuilder(ConnectionString) { MaxPoolSize = 30 }.ToString();

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
