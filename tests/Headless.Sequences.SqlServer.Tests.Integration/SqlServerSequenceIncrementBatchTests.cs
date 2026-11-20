// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Headless.Hosting;
using Headless.Sequences;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// The fast-path increment is one self-committing batch (<c>SET XACT_ABORT ON; BEGIN TRAN; upsert; COMMIT</c>): one
/// round trip with no client transaction, a value returned only once its batch committed, and session settings that
/// neither outlive the pooled connection nor reach a caller's own unit.
/// </summary>
[Collection<SqlServerSequencesFixture>]
public sealed class SqlServerSequenceIncrementBatchTests(SqlServerSequencesFixture fixture) : TestBase
{
    private const string _Schema = "sequences_mssql_batch";
    private const string _Table = "Sequences";
    private const int _XactAbortOption = 16384;

    [Fact]
    public async Task should_send_a_fast_increment_as_one_command_without_a_client_transaction()
    {
        // given: a host on its own schema, so the captured commands are this store's
        await using var services = await _CreateServicesAsync(fixture.CountersConnectionString);
        var generator = services.GetRequiredService<ISequenceGenerator>();
        using var commands = new SqlCommandRecorder(_Schema);

        // when: the first and second numbers of a new counter are taken
        var first = await generator.NextAsync("one-command", cancellationToken: AbortToken);
        var second = await generator.NextAsync("one-command", cancellationToken: AbortToken);

        // then: the first call inserted the counter, the second updated it
        first.Should().Be(1);
        second.Should().Be(2);

        // and: each call sent exactly one command, outside any client transaction, that opens and commits its own
        var sent = commands.Snapshot();
        sent.Should().HaveCount(2);
        sent.Should()
            .AllSatisfy(command =>
            {
                command.HadTransaction.Should().BeFalse();
                command.Text.Should().StartWith("SET XACT_ABORT ON;");
                command.Text.Should().Contain("BEGIN TRANSACTION;");
                command.Text.TrimEnd().Should().EndWith("COMMIT TRANSACTION;");
            });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_surface_an_error_after_the_decision_row_only_when_the_reader_is_drained(bool drain)
    {
        // The batch's decision row comes before its COMMIT. SqlClient drops an error raised after the first result set
        // when the reader is only disposed, so a batch whose commit failed would still return a rolled-back value and a
        // retry could hand the same number out again. The store therefore reads the remaining results before it
        // returns the value; this pins the driver behavior that makes that read necessary.
        await using var connection = new SqlConnection(fixture.CountersConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            SELECT CAST(1 AS smallint) AS outcome, CAST(7 AS bigint) AS value;
            THROW 50000, 'failed after the decision row', 1;
            COMMIT TRANSACTION;
            """,
            connection
        );

        var read = async () =>
        {
            await using var reader = await command.ExecuteReaderAsync(AbortToken);
            (await reader.ReadAsync(AbortToken)).Should().BeTrue();
            reader.GetInt64(1).Should().Be(7);

            if (drain)
            {
                while (await reader.NextResultAsync(AbortToken)) { }
            }
        };

        if (drain)
        {
            (await read.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(50000);
        }
        else
        {
            await read.Should()
                .NotThrowAsync(
                    "SqlClient discards the error on dispose; if this starts throwing, the store's drain is redundant"
                );
        }

        // and: XACT_ABORT rolled the batch's transaction back rather than leaving it open on the connection
        await using var check = new SqlCommand("SELECT @@TRANCOUNT;", connection);
        Convert.ToInt32(await check.ExecuteScalarAsync(AbortToken), CultureInfo.InvariantCulture).Should().Be(0);
    }

    [Fact]
    public async Task should_not_carry_xact_abort_to_the_next_user_of_a_pooled_connection()
    {
        // given: a pool of one connection, so the next open reuses the session the increment ran on
        var connectionString = new SqlConnectionStringBuilder(fixture.CountersConnectionString)
        {
            ApplicationName = $"sequences-batch-{Guid.NewGuid():N}",
            MaxPoolSize = 1,
        }.ConnectionString;
        await using var services = await _CreateServicesAsync(connectionString);
        var generator = services.GetRequiredService<ISequenceGenerator>();

        // when: a fast increment runs its batch, which turns XACT_ABORT on for its session
        await generator.NextAsync("pooled", cancellationToken: AbortToken);

        // then: the pool resets the setting before it hands the same session out again
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"SELECT @@OPTIONS & {_XactAbortOption}, transaction_isolation_level "
                + "FROM sys.dm_exec_sessions WHERE session_id = @@SPID;",
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        (await reader.ReadAsync(AbortToken)).Should().BeTrue();
        reader.GetInt32(0).Should().Be(0);
        reader.GetInt16(1).Should().Be(2, "the session is back at READ COMMITTED");
    }

    [Fact]
    public async Task should_leave_the_callers_xact_abort_setting_unchanged_on_a_gap_free_increment()
    {
        // given: a gap-free counter and a caller's connection with XACT_ABORT OFF
        await using var services = await _CreateServicesAsync(
            fixture.CountersConnectionString,
            setup => setup.Policy("gap-free", new SequencePolicy { Mode = SequenceMode.GapFree })
        );
        var factory = services.GetRequiredService<IUnitOfWorkFactory>();
        await using var connection = new SqlConnection(fixture.CountersConnectionString);
        await connection.OpenAsync(AbortToken);
        await using (var off = new SqlCommand("SET XACT_ABORT OFF;", connection))
        {
            await off.ExecuteNonQueryAsync(AbortToken);
        }

        using var commands = new SqlCommandRecorder(_Schema);

        // when: a unit enlisted on the caller's own transaction takes a number
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(AbortToken);
        await using var unit = fixture.Enlist(factory, connection, transaction);
        (await unit.Sequences.NextAsync("gap-free", cancellationToken: AbortToken)).Should().Be(1);

        // then: the increment ran the plain upsert in the caller's transaction, not the self-committing batch
        var sent = commands.Snapshot();
        sent.Should().ContainSingle();
        sent[0].HadTransaction.Should().BeTrue();
        sent[0].Text.Should().NotContain("XACT_ABORT").And.NotContain("BEGIN TRANSACTION");

        // and: the caller's session still has XACT_ABORT OFF and its transaction is still the only one open
        await using var check = new SqlCommand(
            $"SELECT @@OPTIONS & {_XactAbortOption}, @@TRANCOUNT;",
            connection,
            transaction
        );
        await using (var reader = await check.ExecuteReaderAsync(AbortToken))
        {
            (await reader.ReadAsync(AbortToken)).Should().BeTrue();
            reader.GetInt32(0).Should().Be(0);
            reader.GetInt32(1).Should().Be(1);
        }

        await transaction.CommitAsync(AbortToken);
        (await fixture.ReadValueAsync(new SequenceKey("", "gap-free", ""), _Schema, _Table, AbortToken)).Should().Be(1);
    }

    private async Task<ServiceProvider> _CreateServicesAsync(
        string connectionString,
        Action<HeadlessSequencesSetupBuilder>? configure = null
    )
    {
        await fixture.ExecuteAsync(SqlServerSequencesFixture.DropSchemaSql(_Schema, _Table), AbortToken);

        var services = new ServiceCollection();
        services.AddLogging();
        fixture.ConfigureUnitOfWork(services);
        services.AddHeadlessSequences(setup =>
        {
            setup.UseSqlServer(options =>
            {
                options.ConnectionString = connectionString;
                options.Schema = _Schema;
            });
            configure?.Invoke(setup);
        });

        // A host start only verifies the schema, so the test host creates it first, as a development host does.
        services.AddDeployStepsOnStartup(o => o.Seed = false);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        foreach (var initializer in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await initializer.StartingAsync(AbortToken);
        }

        return provider;
    }

    /// <summary>Records every SqlClient command that names <c>schema</c>, from SqlClient's diagnostic events.</summary>
    private sealed class SqlCommandRecorder
        : IObserver<DiagnosticListener>,
            IObserver<KeyValuePair<string, object?>>,
            IDisposable
    {
        private const string _ListenerName = "SqlClientDiagnosticListener";
        private const string _CommandBefore = "Microsoft.Data.SqlClient.WriteCommandBefore";

        private readonly string _schema;
        private readonly ConcurrentQueue<SentCommand> _sent = new();
        private readonly ConcurrentBag<IDisposable> _subscriptions = [];
        private readonly IDisposable _allListeners;

        public SqlCommandRecorder(string schema)
        {
            _schema = schema;
            _allListeners = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public IReadOnlyList<SentCommand> Snapshot() => [.. _sent];

        public void OnNext(DiagnosticListener value)
        {
            if (string.Equals(value.Name, _ListenerName, StringComparison.Ordinal))
            {
                _subscriptions.Add(value.Subscribe(this));
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (
                !string.Equals(value.Key, _CommandBefore, StringComparison.Ordinal)
                || value.Value is not IReadOnlyList<KeyValuePair<string, object>> payload
            )
            {
                return;
            }

            var command = payload
                .Where(static p => string.Equals(p.Key, "Command", StringComparison.Ordinal))
                .Select(static p => p.Value)
                .OfType<SqlCommand>()
                .FirstOrDefault();

            if (command?.CommandText.Contains($"[{_schema}]", StringComparison.Ordinal) == true)
            {
                _sent.Enqueue(new SentCommand(command.CommandText, command.Transaction is not null));
            }
        }

        public void OnCompleted() { }

        public void OnError(Exception error) { }

        public void Dispose()
        {
            _allListeners.Dispose();

            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
        }
    }

    private sealed record SentCommand(string Text, bool HadTransaction);
}
