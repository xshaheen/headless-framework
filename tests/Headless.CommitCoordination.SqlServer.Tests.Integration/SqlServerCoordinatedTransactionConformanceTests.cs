// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Runs the coordinated-transaction helper conformance suite against the raw-ADO SQL Server helper. SQL Server
/// signaling is explicit: the helper itself signals <c>Committed</c> after <c>CommitAsync</c>, and these
/// scenarios fail if it ever stops doing so.
/// </summary>
[Collection<SqlServerCommitCoordinationFixture>]
public sealed class SqlServerCoordinatedTransactionConformanceTests
    : CoordinatedTransactionConformanceTests<SqlServerCoordinatedTransactionFixture>
{
    private readonly SqlServerCoordinatedTransactionFixture _fixture;

    public SqlServerCoordinatedTransactionConformanceTests(SqlServerCommitCoordinationFixture container)
        : this(new SqlServerCoordinatedTransactionFixture(container)) { }

    private SqlServerCoordinatedTransactionConformanceTests(SqlServerCoordinatedTransactionFixture fixture)
        : base(fixture)
    {
        _fixture = fixture;
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _fixture.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public override Task should_drain_buffered_commit_work_and_persist_rows_when_operation_commits()
    {
        return base.should_drain_buffered_commit_work_and_persist_rows_when_operation_commits();
    }

    [Fact]
    public override Task should_discard_buffered_commit_work_and_roll_back_rows_when_operation_throws()
    {
        return base.should_discard_buffered_commit_work_and_roll_back_rows_when_operation_throws();
    }
}

/// <summary>
/// SQL Server leaf fixture: wraps <c>SqlConnection.ExecuteCoordinatedTransactionAsync</c>. The probe table lives
/// outside any coordinated transaction; probe counting uses an independent connection.
/// </summary>
public sealed class SqlServerCoordinatedTransactionFixture(SqlServerCommitCoordinationFixture container)
    : ICoordinatedTransactionFixture,
        IAsyncDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddLogging()
        .AddSqlServerCommitCoordination()
        .BuildServiceProvider();

    public ValueTask DisposeAsync()
    {
        return _services.DisposeAsync();
    }

    public async Task RunCoordinatedAsync(
        Func<ICoordinatedTransactionContext, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new SqlConnection(container.ConnectionString);

        await connection.ExecuteCoordinatedTransactionAsync(
            (conn, ct) => operation(new SqlServerCoordinatedTransactionContext(_services, conn), ct),
            _services,
            cancellationToken: cancellationToken
        );
    }

    public async Task<int> CountProbeRowsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(container.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT count(*) FROM probe_rows", connection);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(container.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            IF OBJECT_ID('dbo.probe_rows', 'U') IS NULL
                CREATE TABLE dbo.probe_rows (id int IDENTITY(1,1) PRIMARY KEY, name nvarchar(256) NULL);
            DELETE FROM dbo.probe_rows;
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class SqlServerCoordinatedTransactionContext(IServiceProvider services, SqlConnection connection)
        : ICoordinatedTransactionContext
    {
        // Lazy on every read: the helper enlists per attempt.
        public ICommitCoordinator Coordinator =>
            services.GetRequiredService<ICurrentCommitCoordinator>().Current
            ?? throw new InvalidOperationException("No ambient coordinator — the helper did not enlist.");

        public async Task InsertProbeRowAsync(string name, CancellationToken cancellationToken)
        {
            // Reach the live transaction through the relational handle — SqlCommand requires the transaction to
            // be assigned explicitly, and this is the designed participant path anyway.
            var transaction =
                (SqlTransaction?)Coordinator.Relational?.Transaction
                ?? throw new InvalidOperationException("The helper exposed no live relational transaction.");

            await using var command = new SqlCommand(
                "INSERT INTO dbo.probe_rows (name) VALUES (@name)",
                connection,
                transaction
            );
            command.Parameters.AddWithValue("@name", name);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
