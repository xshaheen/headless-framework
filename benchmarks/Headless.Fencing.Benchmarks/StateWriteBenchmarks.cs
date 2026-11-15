// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using BenchmarkDotNet.Attributes;
using Headless.Sql;
using Headless.Sql.PostgreSql;
using Headless.Sql.SqlServer;

namespace Headless.Fencing.Benchmarks;

/// <summary>
/// One fenced-row state write on the caller's open connection, in a transaction of the helper's own, as a durable
/// message store runs one per message: <see cref="FencedRows.WriteAsync{TKey,TAccepted}" /> and
/// <see cref="FencedRows.WriteAndRenewAsync{TKey,TAccepted}" />, which keep the lease, so every call writes the same
/// row under the same generation. The write has the shape of a message state change: a dozen assignments, some through
/// <c>CASE</c>, a condition, and five columns read back. Allocated bytes per write is the figure: the statement text
/// the helper builds for the call is part of it, the round trip is not.
/// </summary>
public class StateWriteBenchmarks
{
    private const string _Table = "bench_messages";

    private static readonly string[] _Returning =
    [
        "lease_generation",
        "lease_state",
        "lease_expires_at",
        "lease_takeover_count",
        "lease_holder",
    ];

    private BenchmarkDatabase _database = null!;
    private DbConnection _connection = null!;
    private FencedRowTable<Guid> _table = null!;
    private FencedRow<Guid> _row = null!;
    private ISqlDialect _dialect = null!;
    private string _set = null!;
    private string _condition = null!;

    [Params(FencingProvider.PostgreSql, FencingProvider.SqlServer)]
    public FencingProvider Provider { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _database = await BenchmarkDatabase.CreateAsync(Provider);
        _dialect = Provider == FencingProvider.PostgreSql ? PostgreSqlDialect.Instance : SqlServerDialect.Instance;

        var (text, timestamp, uuid) =
            Provider == FencingProvider.PostgreSql
                ? ("text", "timestamptz", "uuid")
                : ("nvarchar(max)", "datetimeoffset(7)", "uniqueidentifier");
        var id = Guid.NewGuid();

        _connection = _database.CreateConnection();
        await _connection.OpenAsync();
        await using (var command = _connection.CreateCommand())
        {
#pragma warning disable CA2100 // Column types chosen per provider from constants; the id travels as a parameter.
            command.CommandText = $"""
                CREATE TABLE {_Table} (
                    id {uuid} NOT NULL PRIMARY KEY,
                    content {text} NULL,
                    retries int NOT NULL DEFAULT 0,
                    inline_attempts int NOT NULL DEFAULT 0,
                    expires_at {timestamp} NULL,
                    next_retry_at {timestamp} NULL,
                    status_name {text} NOT NULL,
                    changed_at {timestamp} NULL,
                    exception_info {text} NULL,
                    failure_fingerprint {text} NULL,
                    attempt_id {uuid} NULL,
                    pre_entry_failures int NOT NULL DEFAULT 0,
                    terminal_at {timestamp} NULL,
                    lease_generation bigint NOT NULL DEFAULT 0,
                    lease_state smallint NOT NULL DEFAULT 0,
                    lease_expires_at {timestamp} NULL,
                    lease_holder {text} NULL,
                    lease_takeover_count int NOT NULL DEFAULT 0
                );
                INSERT INTO {_Table} (id, status_name) VALUES (@Id, 'Scheduled');
                """;
#pragma warning restore CA2100
            _dialect.AddParameter(command, "Id", SqlColumnType.Guid, id);
            await command.ExecuteNonQueryAsync();
        }

        _table = FencedRowTable<Guid>.Create(
            _dialect,
            schema: null,
            _Table,
            FencedRowKey<Guid>.Scalar("id", SqlColumnType.Guid)
        );

        var granted = await FencedRows.GrantAsync(
            _table,
            _connection,
            transaction: null,
            id,
            TimeSpan.FromHours(2),
            new LeaseGrantOptions { Holder = "bench" }
        );
        _row = granted?.Row ?? throw new InvalidOperationException("The bench row was not granted.");

        const string now = SqlDialectTokens.Now;
        _set = string.Join(
            ", ",
            "content = @Content",
            "retries = @Retries",
            "inline_attempts = @InlineAttempts",
            "expires_at = @ExpiresAt",
            $"next_retry_at = CASE WHEN @KeepLease = {TrueLiteral} THEN next_retry_at ELSE NULL END",
            "status_name = @StatusName",
            $"changed_at = {now}",
            "exception_info = @ExceptionInfo",
            "failure_fingerprint = @FailureFingerprint",
            $"attempt_id = CASE WHEN @KeepLease = {TrueLiteral} THEN attempt_id ELSE NULL END",
            "pre_entry_failures = @PreEntryFailures",
            $"terminal_at = CASE WHEN @IsTerminal = {TrueLiteral} THEN {now} ELSE terminal_at END"
        );
        _condition = "status_name <> 'Succeeded' AND retries <= @Retries";

        // Each write commits on its own: one long transaction would pile row versions up on the one row and slow
        // every later write, so the run would measure the pile instead of the write.
        await KeepLease();
        await KeepAndRenewLease();
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    [Benchmark(Baseline = true, Description = "WriteAsync(keep lease)")]
    public async Task KeepLease()
    {
        var written = await FencedRows.WriteAsync(
            _table,
            _connection,
            transaction: null,
            _row,
            _Write(),
            static (_, _) => ValueTask.FromResult(true)
        );
        _ExpectAccepted(written.IsAccepted);
    }

    [Benchmark(Description = "WriteAndRenewAsync")]
    public async Task KeepAndRenewLease()
    {
        var written = await FencedRows.WriteAndRenewAsync(
            _table,
            _connection,
            transaction: null,
            _row,
            _Write(),
            TimeSpan.FromHours(2),
            static (_, _) => ValueTask.FromResult(true)
        );
        _ExpectAccepted(written.IsAccepted);
    }

    private string TrueLiteral => Provider == FencingProvider.PostgreSql ? "TRUE" : "1";

    // A new write and bind per call, as a store builds them per message; only the SQL fragments are the same strings.
    private FencedRowWrite _Write()
    {
        return new FencedRowWrite(_set, _condition, _Returning, _Bind);
    }

    private void _Bind(DbCommand command)
    {
        _dialect.AddParameter(command, "Content", SqlColumnType.Text(-1), "{\"value\":42}");
        _dialect.AddParameter(command, "Retries", SqlColumnType.Int32, 1);
        _dialect.AddParameter(command, "InlineAttempts", SqlColumnType.Int32, 1);
        _dialect.AddParameter(command, "ExpiresAt", SqlColumnType.Timestamp, (DateTimeOffset?)null);
        _dialect.AddParameter(command, "KeepLease", SqlColumnType.Boolean, true);
        _dialect.AddParameter(command, "StatusName", SqlColumnType.Text(-1), "Scheduled");
        _dialect.AddParameter(command, "ExceptionInfo", SqlColumnType.Text(-1), (string?)null);
        _dialect.AddParameter(command, "FailureFingerprint", SqlColumnType.Text(-1), (string?)null);
        _dialect.AddParameter(command, "PreEntryFailures", SqlColumnType.Int32, 0);
        _dialect.AddParameter(command, "IsTerminal", SqlColumnType.Boolean, false);
    }

    private static void _ExpectAccepted(bool accepted)
    {
        if (!accepted)
        {
            throw new InvalidOperationException("Expected the fenced write to apply.");
        }
    }
}
