// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Encryption;
using Headless.Testing.Testcontainers;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests;

/// <summary>PostgreSQL leaf fixture for the encryption key-store conformance suite.</summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlEncryptionFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlEncryptionFixture>,
        IEncryptionFixture
{
    public string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("encryption_test").WithUsername("postgres").WithPassword("postgres");
    }

    public void ConfigureKeyStore(HeadlessEncryptionSetupBuilder setup, string tableName)
    {
        // Each host gets its own pool, and the race tests run several hosts at once.
        var connectionString = new NpgsqlConnectionStringBuilder(ConnectionString) { MaxPoolSize = 30 }.ToString();

        setup.UsePostgreSql(options =>
        {
            options.ConnectionString = connectionString;
            options.TableName = tableName;
        });
    }

    public void ConfigureUnreachableKeyStore(HeadlessEncryptionSetupBuilder setup)
    {
        // Port 1 accepts no connections, so the TCP connect fails before any authentication is attempted.
        setup.UsePostgreSql(
            "Host=127.0.0.1;Port=1;Database=missing;Username=postgres;Password=placeholder-never-used;Timeout=2"
        );
    }
}
