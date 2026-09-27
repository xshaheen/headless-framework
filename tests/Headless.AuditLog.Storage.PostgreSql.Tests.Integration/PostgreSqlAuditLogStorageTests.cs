// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting.Initialization;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlAuditLogFixture>]
public sealed class PostgreSqlAuditLogStorageTests(PostgreSqlAuditLogFixture fixture) : TestBase
{
    private const string _Schema = "audit_log_pg_raw";

    [Fact]
    public async Task should_initialize_table_and_round_trip_audit_entry()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();

        // when
        await host.StartAsync(AbortToken);
        var initializer = host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single();
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAuditLogStore>();
        var reader = scope.ServiceProvider.GetRequiredService<IReadAuditLog<object>>();
        var createdAt = new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero);

        await store.SaveAsync(
            [
                new AuditLogEntryData
                {
                    Action = "entity.created",
                    ChangeType = AuditChangeType.Created,
                    EntityType = "Order",
                    EntityId = "ORD-1",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    NewValues = new Dictionary<string, object?>(StringComparer.Ordinal) { ["total"] = 42 },
                    ChangedFields = ["total"],
                    CreatedAt = createdAt,
                },
            ],
            savingContext: new object(),
            AbortToken
        );
        var entries = (
            await reader.QueryAsync(
                new() { Action = "entity.created", TenantId = "tenant-1" },
                cancellationToken: AbortToken
            )
        ).Items;

        // then
        initializer.IsInitialized.Should().BeTrue();
        (await _TableExistsAsync("audit_log")).Should().BeTrue();
        (await _JsonColumnTypeAsync("NewValues")).Should().Be("jsonb");
        entries.Should().ContainSingle();
        entries[0].EntityId.Should().Be("ORD-1");
        entries[0].ChangedFields.Should().Equal("total");
        entries[0].NewValues.Should().ContainKey("total");
    }

    [Fact]
    public async Task should_persist_all_entries_across_multiple_chunks_when_batch_exceeds_chunk_size()
    {
        // given — chunk size is 500 rows; 550 forces two chunks
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAuditLogStore>();
        var reader = scope.ServiceProvider.GetRequiredService<IReadAuditLog<object>>();
        var createdAt = new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero);

        const int totalEntries = 550;
        var entries = Enumerable
            .Range(0, totalEntries)
            .Select(i => new AuditLogEntryData
            {
                Action = "batch.write",
                ChangeType = AuditChangeType.Created,
                EntityType = "Order",
                EntityId = $"ORD-{i:D4}",
                TenantId = "tenant-batch",
                NewValues = new Dictionary<string, object?>(StringComparer.Ordinal) { ["index"] = i },
                CreatedAt = createdAt,
            })
            .ToArray();

        // when
        await store.SaveAsync(entries, savingContext: new object(), AbortToken);
        var roundTripped = (
            await reader.QueryAsync(
                new()
                {
                    Action = "batch.write",
                    TenantId = "tenant-batch",
                    Size = totalEntries + 10,
                },
                cancellationToken: AbortToken
            )
        ).Items;

        // then
        roundTripped.Should().HaveCount(totalEntries);
        roundTripped.Select(e => e.EntityId).Should().BeEquivalentTo(entries.Select(e => e.EntityId));
    }

    [Fact]
    public async Task should_reject_null_read_query()
    {
        // given
        using var host = _CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IReadAuditLog<object>>();

        // when
        var act = () => reader.QueryAsync(null!, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task should_reject_non_positive_read_query_size(int size)
    {
        // given
        using var host = _CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IReadAuditLog<object>>();

        // when
        var act = () => reader.QueryAsync(new() { Size = size }, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(AuditLogSortDirection.NewestFirst)]
    [InlineData(AuditLogSortDirection.OldestFirst)]
    public async Task should_walk_every_entry_exactly_once_across_pages_in_keyset_order(AuditLogSortDirection direction)
    {
        // given — pairs share a timestamp so the Id tie-break decides order at page boundaries, and timestamps
        // one microsecond apart catch any parameter binding that rounds the continuation position
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAuditLogStore>();
        var reader = scope.ServiceProvider.GetRequiredService<IReadAuditLog<object>>();
        var t0 = new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero);
        var entries = Enumerable
            .Range(0, 7)
            .Select(i => new AuditLogEntryData
            {
                Action = "paging.test",
                EntityId = $"e{i}",
                TenantId = "tenant-paging",
                CreatedAt = t0.AddTicks(i / 2 * 10),
            })
            .ToArray();
        await store.SaveAsync(entries, savingContext: new object(), AbortToken);
        var single = await reader.QueryAsync(
            new() { TenantId = "tenant-paging", Direction = direction },
            cancellationToken: AbortToken
        );

        // when
        var walked = new List<AuditLogEntryData>();
        var pageSizes = new List<int>();
        string? token = null;

        do
        {
            var page = await reader.QueryAsync(
                new()
                {
                    TenantId = "tenant-paging",
                    Direction = direction,
                    Size = 3,
                    ContinuationToken = token,
                },
                cancellationToken: AbortToken
            );
            walked.AddRange(page.Items);
            pageSizes.Add(page.Items.Count);
            token = page.ContinuationToken;
        } while (token is not null && pageSizes.Count < 10);

        // then
        single.ContinuationToken.Should().BeNull();
        pageSizes.Should().Equal(3, 3, 1);
        walked.Select(e => e.EntityId).Should().Equal(single.Items.Select(e => e.EntityId));
        walked.Should().OnlyHaveUniqueItems(e => e.EntityId);
        walked.Select(e => e.CreatedAt).Should().BeEquivalentTo(entries.Select(e => e.CreatedAt));

        if (direction == AuditLogSortDirection.NewestFirst)
        {
            walked.Should().BeInDescendingOrder(e => e.CreatedAt);
        }
        else
        {
            walked.Should().BeInAscendingOrder(e => e.CreatedAt);
        }
    }

    [Fact]
    public async Task should_filter_by_account_and_correlation_ids()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAuditLogStore>();
        var reader = scope.ServiceProvider.GetRequiredService<IReadAuditLog<object>>();
        var createdAt = new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero);
        await store.SaveAsync(
            [
                new AuditLogEntryData
                {
                    Action = "filter.test",
                    EntityId = "match",
                    AccountId = "acc-1",
                    CorrelationId = "corr-1",
                    CreatedAt = createdAt,
                },
                new AuditLogEntryData
                {
                    Action = "filter.test",
                    EntityId = "other-account",
                    AccountId = "acc-2",
                    CorrelationId = "corr-1",
                    CreatedAt = createdAt,
                },
                new AuditLogEntryData
                {
                    Action = "filter.test",
                    EntityId = "other-correlation",
                    AccountId = "acc-1",
                    CorrelationId = "corr-2",
                    CreatedAt = createdAt,
                },
            ],
            savingContext: new object(),
            AbortToken
        );

        // when
        var byAccount = await reader.QueryAsync(new() { AccountId = "acc-1" }, cancellationToken: AbortToken);
        var byCorrelation = await reader.QueryAsync(new() { CorrelationId = "corr-1" }, cancellationToken: AbortToken);

        // then
        byAccount.Items.Select(e => e.EntityId).Should().BeEquivalentTo("match", "other-correlation");
        byCorrelation.Items.Select(e => e.EntityId).Should().BeEquivalentTo("match", "other-account");
    }

    [Fact]
    public async Task should_commit_writer_entry_immediately()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        await using var scope = host.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditLogWriter<object>>();
        var reader = scope.ServiceProvider.GetRequiredService<IReadAuditLog<object>>();

        // when
        await writer.WriteAsync(
            new AuditLogWriteRequest { Action = "authorization.forbidden", Success = false },
            AbortToken
        );
        var page = await reader.QueryAsync(new() { Action = "authorization.forbidden" }, cancellationToken: AbortToken);

        // then
        page.Items.Should().ContainSingle().Which.Success.Should().BeFalse();
    }

    private IHost _CreateHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = _Schema);
            setup.UsePostgreSql(fixture.ConnectionString);
        });

        return builder.Build();
    }

    private async Task _DropSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand($"""DROP SCHEMA IF EXISTS "{_Schema}" CASCADE;""", connection);
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task<bool> _TableExistsAsync(string tableName)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema AND table_name = @table
            )
            """,
            connection
        );
        command.Parameters.AddWithValue("schema", _Schema);
        command.Parameters.AddWithValue("table", tableName);

        return (bool)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private async Task<string> _JsonColumnTypeAsync(string columnName)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT data_type
            FROM information_schema.columns
            WHERE table_schema = @schema AND table_name = 'audit_log' AND column_name = @column
            """,
            connection
        );
        command.Parameters.AddWithValue("schema", _Schema);
        command.Parameters.AddWithValue("column", columnName);

        return (string)(await command.ExecuteScalarAsync(AbortToken))!;
    }
}
