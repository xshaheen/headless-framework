// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.AuditLog;
using Headless.AuditLog.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlAuditLogFixture>]
public sealed class PostgreSqlAuditLogAtomicityTests(PostgreSqlAuditLogFixture fixture)
{
    private const string _Schema = "audit_log_pg_atomicity";

    [Fact]
    public async Task should_enroll_in_ambient_transaction_and_rollback_audit_row_atomically()
    {
        // given — host with a custom ambient accessor that hands the store our test-owned connection
        await _DropSchemaAsync();
        var accessor = new TestAmbientAccessor();
        using var host = _CreateHost(accessor);
        await host.StartAsync(TestContext.Current.CancellationToken);

        await using var sharedConnection = new NpgsqlConnection(fixture.ConnectionString);
        await sharedConnection.OpenAsync(TestContext.Current.CancellationToken);
        await using var sharedTransaction = await sharedConnection.BeginTransactionAsync(
            TestContext.Current.CancellationToken
        );

        accessor.Connection = sharedConnection;
        accessor.Transaction = sharedTransaction;

        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAuditLogStore>();

        // when — store writes through the shared connection, then we roll back without committing
        await store.SaveAsync(
            [_NewEntry(action: "atomicity.rollback")],
            savingContext: new object(),
            TestContext.Current.CancellationToken
        );
        await sharedTransaction.RollbackAsync(TestContext.Current.CancellationToken);

        // then — the row is gone (proof of true transactional enrollment)
        var rowCount = await _CountRowsByActionAsync("atomicity.rollback");
        rowCount.Should().Be(0);
    }

    [Fact]
    public async Task should_enroll_in_ambient_transaction_and_commit_audit_row_atomically()
    {
        // given
        await _DropSchemaAsync();
        var accessor = new TestAmbientAccessor();
        using var host = _CreateHost(accessor);
        await host.StartAsync(TestContext.Current.CancellationToken);

        await using var sharedConnection = new NpgsqlConnection(fixture.ConnectionString);
        await sharedConnection.OpenAsync(TestContext.Current.CancellationToken);
        await using var sharedTransaction = await sharedConnection.BeginTransactionAsync(
            TestContext.Current.CancellationToken
        );

        accessor.Connection = sharedConnection;
        accessor.Transaction = sharedTransaction;

        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAuditLogStore>();

        // when
        await store.SaveAsync(
            [_NewEntry(action: "atomicity.commit")],
            savingContext: new object(),
            TestContext.Current.CancellationToken
        );
        await sharedTransaction.CommitAsync(TestContext.Current.CancellationToken);

        // then
        var rowCount = await _CountRowsByActionAsync("atomicity.commit");
        rowCount.Should().Be(1);
    }

    [Fact]
    public async Task should_fall_back_to_own_connection_when_accessor_returns_null()
    {
        // given — accessor returns nulls so the store opens its own connection
        await _DropSchemaAsync();
        var accessor = new TestAmbientAccessor { Connection = null, Transaction = null };
        using var host = _CreateHost(accessor);
        await host.StartAsync(TestContext.Current.CancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAuditLogStore>();

        // when — no enrollment available, store writes on its own connection and commits
        await store.SaveAsync(
            [_NewEntry(action: "atomicity.standalone")],
            savingContext: new object(),
            TestContext.Current.CancellationToken
        );

        // then
        var rowCount = await _CountRowsByActionAsync("atomicity.standalone");
        rowCount.Should().Be(1);
    }

    [Fact]
    public async Task should_fall_back_to_own_connection_when_accessor_returns_non_npgsql_connection()
    {
        // given — accessor returns a non-Npgsql connection AND non-Npgsql transaction (simulating
        // the consumer's DbContext using a different driver). Both must be non-null to reach the
        // store's runtime type check; a null transaction short-circuits before the mismatch branch
        // and exercises the standalone path instead of what this test is meant to cover.
        await _DropSchemaAsync();
        var fakeConnection = new NonNpgsqlConnectionStub();
        var fakeTransaction = new NonNpgsqlTransactionStub(fakeConnection);
        var accessor = new TestAmbientAccessor { Connection = fakeConnection, Transaction = fakeTransaction };
        using var host = _CreateHost(accessor);
        await host.StartAsync(TestContext.Current.CancellationToken);

        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAuditLogStore>();

        // when — store sees a mismatched connection type, warns, and falls back to its own connection
        await store.SaveAsync(
            [_NewEntry(action: "atomicity.mismatch_fallback")],
            savingContext: new object(),
            TestContext.Current.CancellationToken
        );

        // then — the row is persisted via the fallback path (committed on the store's own connection)
        var rowCount = await _CountRowsByActionAsync("atomicity.mismatch_fallback");
        rowCount.Should().Be(1);
    }

    private IHost _CreateHost(IAmbientDbTransactionAccessor accessor)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = _Schema);
            setup.UsePostgreSql(fixture.ConnectionString);
        });
        // Override the default null/missing accessor with the test-owned one so the store can enroll.
        builder.Services.AddSingleton(accessor);

        return builder.Build();
    }

    private static AuditLogEntryData _NewEntry(string action) =>
        new()
        {
            Action = action,
            ChangeType = AuditChangeType.Created,
            EntityType = "AtomicityTest",
            EntityId = "AT-1",
            TenantId = "tenant-1",
            UserId = "user-1",
            NewValues = new Dictionary<string, object?>(StringComparer.Ordinal) { ["value"] = 1 },
            ChangedFields = ["value"],
            CreatedAt = new DateTimeOffset(2026, 5, 25, 0, 0, 0, TimeSpan.Zero),
        };

    private async Task _DropSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"""DROP SCHEMA IF EXISTS "{_Schema}" CASCADE;""", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<long> _CountRowsByActionAsync(string action)
    {
        // Open a fresh connection so we observe only committed rows (READ COMMITTED isolation).
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            $"""SELECT COUNT(*) FROM "{_Schema}"."audit_log" WHERE "Action" = @action;""",
            connection
        );
        command.Parameters.AddWithValue(nameof(action), action);

        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private sealed class TestAmbientAccessor : IAmbientDbTransactionAccessor
    {
        public DbConnection? Connection { get; set; }
        public DbTransaction? Transaction { get; set; }

        public (DbConnection? Connection, DbTransaction? Transaction) TryResolve(object savingContext) =>
            (Connection, Transaction);
    }

    // Minimal DbConnection stub used only to exercise the provider-mismatch fallback path.
    // The store inspects the runtime type, sees this is not NpgsqlConnection, logs a warning,
    // and falls back to opening its own connection — none of these abstract members ever execute.
    private sealed class NonNpgsqlConnectionStub : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => string.Empty;
        public override string DataSource => string.Empty;
        public override string ServerVersion => string.Empty;
        public override System.Data.ConnectionState State => System.Data.ConnectionState.Closed;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close() { }

        public override void Open() => throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel il) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    // Companion stub paired with NonNpgsqlConnectionStub so accessor returns a (non-null, non-null)
    // pair — both wrong type — and the store's runtime type check actually executes. The store
    // never invokes Commit/Rollback on this since it falls back to its own connection on mismatch.
    private sealed class NonNpgsqlTransactionStub(DbConnection connection) : DbTransaction
    {
        public override System.Data.IsolationLevel IsolationLevel => System.Data.IsolationLevel.Unspecified;
        protected override DbConnection? DbConnection { get; } = connection;

        public override void Commit() => throw new NotSupportedException();

        public override void Rollback() => throw new NotSupportedException();
    }
}
