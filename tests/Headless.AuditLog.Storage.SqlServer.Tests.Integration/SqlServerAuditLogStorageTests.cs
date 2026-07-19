// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting.Initialization;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection<SqlServerAuditLogFixture>]
public sealed class SqlServerAuditLogStorageTests(SqlServerAuditLogFixture fixture) : TestBase
{
    private const string _Schema = "audit_log_sql_raw";

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
        var entries = await reader.QueryAsync(
            new() { Action = "entity.created", TenantId = "tenant-1" },
            cancellationToken: AbortToken
        );

        // then
        initializer.IsInitialized.Should().BeTrue();
        (await _TableExistsAsync("audit_log")).Should().BeTrue();
        (await _JsonColumnTypeAsync("NewValues")).Should().Be("nvarchar");
        entries.Should().ContainSingle();
        entries[0].EntityId.Should().Be("ORD-1");
        entries[0].ChangedFields.Should().Equal("total");
        entries[0].NewValues.Should().ContainKey("total");
    }

    [Fact]
    public async Task should_persist_all_entries_across_multiple_chunks_when_batch_exceeds_chunk_size()
    {
        // given — chunk size is 100 rows; 150 forces two chunks
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAuditLogStore>();
        var reader = scope.ServiceProvider.GetRequiredService<IReadAuditLog<object>>();
        var createdAt = new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero);

        const int totalEntries = 150;
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
        var roundTripped = await reader.QueryAsync(
            new()
            {
                Action = "batch.write",
                TenantId = "tenant-batch",
                Limit = totalEntries + 10,
            },
            cancellationToken: AbortToken
        );

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
    public async Task should_reject_non_positive_read_query_limit(int limit)
    {
        // given
        using var host = _CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IReadAuditLog<object>>();

        // when
        var act = () => reader.QueryAsync(new() { Limit = limit }, cancellationToken: AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private IHost _CreateHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = _Schema);
            setup.UseSqlServer(fixture.ConnectionString);
        });

        return builder.Build();
    }

    private async Task _DropSchemaAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"""
            IF OBJECT_ID(N'{_Schema}.audit_log', N'U') IS NOT NULL DROP TABLE [{_Schema}].[audit_log];
            IF EXISTS (SELECT * FROM sys.schemas WHERE name = N'{_Schema}') EXEC(N'DROP SCHEMA [{_Schema}]');
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task<bool> _TableExistsAsync(string tableName)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema AND table_name = @table
            ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
            """,
            connection
        );
        command.Parameters.AddWithValue("@schema", _Schema);
        command.Parameters.AddWithValue("@table", tableName);

        return (bool)await command.ExecuteScalarAsync(AbortToken);
    }

    private async Task<string> _JsonColumnTypeAsync(string columnName)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            """
            SELECT DATA_TYPE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = 'audit_log' AND COLUMN_NAME = @column
            """,
            connection
        );
        command.Parameters.AddWithValue("@schema", _Schema);
        command.Parameters.AddWithValue("@column", columnName);

        return (string)await command.ExecuteScalarAsync(AbortToken);
    }
}
