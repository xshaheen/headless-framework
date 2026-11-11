// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Npgsql;

namespace Tests;

/// <summary>
/// A PostgreSQL server of its own with two logins beside the superuser: <see cref="Owner"/>, which creates and owns
/// the audit objects, and <see cref="App"/>, which has no DDL rights. Roles belong to the whole server, so this
/// fixture never attaches to the shared container.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlAuditLogRolesFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlAuditLogRolesFixture>
{
    public const string Owner = "audit_owner";
    public const string App = "audit_app";

    protected override bool ShareContainer => false;

    public string OwnerConnectionString => _ConnectionStringOf(Owner);

    public string AppConnectionString => _ConnectionStringOf(App);

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        await PostgreSqlTestDatabase.ExecuteAsync(
            ConnectionString,
            $"""
            CREATE ROLE "{Owner}" LOGIN PASSWORD '{Owner}';
            CREATE ROLE "{App}" LOGIN PASSWORD '{App}';
            GRANT CREATE ON DATABASE "{DatabaseName}" TO "{Owner}";
            """,
            TestContext.Current.CancellationToken
        );
    }

    private string _ConnectionStringOf(string role)
    {
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Username = role, Password = role }.ToString();
    }
}
