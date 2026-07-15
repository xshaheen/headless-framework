// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Runs the coordinated-transaction helper conformance suite against the raw-ADO SQL Server helper.
/// SQL Server commit detection is out-of-band (SqlClient diagnostic), so the diagnostic hosted service
/// must be running before the first commit — the per-test lifecycle below owns that — and the drain
/// lands off-thread, which is exactly why the base scenarios await a <see cref="TaskCompletionSource" />.
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

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await _fixture.StartAsync(AbortToken);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _fixture.StopAsync();
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
/// SQL Server leaf fixture: wraps <c>SqlConnection.ExecuteCoordinatedTransactionAsync</c>. The probe table
/// lives outside any coordinated transaction; probe counting uses an independent connection. The owning test
/// class starts the SqlClient diagnostic hosted service before the first commit and stops it on teardown so
/// observers never accumulate across the run.
/// </summary>
public sealed class SqlServerCoordinatedTransactionFixture(SqlServerCommitCoordinationFixture container)
    : ICoordinatedTransactionFixture
{
    private ServiceProvider _services = null!;
    private SqlServerCommitDiagnosticHostedService _diagnostic = null!;

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSqlServerCommitCoordination();
        _services = collection.BuildServiceProvider();

        // The diagnostic only routes after AllListeners.Subscribe runs; start it before the first commit.
        _diagnostic = _services.GetServices<IHostedService>().OfType<SqlServerCommitDiagnosticHostedService>().Single();
        await _diagnostic.StartAsync(cancellationToken);
    }

    public async ValueTask StopAsync()
    {
        await _diagnostic.StopAsync(CancellationToken.None);
        await _services.DisposeAsync();
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
            // Reach the live transaction through the relational capability — SqlCommand requires the
            // transaction to be assigned explicitly, and this is the designed participant path anyway.
            if (!Coordinator.TryGetCapability<IRelationalCommitContext>(out var relational))
            {
                throw new InvalidOperationException("The helper did not attach IRelationalCommitContext.");
            }

            var transaction =
                (SqlTransaction?)relational.Transaction
                ?? throw new InvalidOperationException("The relational capability exposed no live transaction.");

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
