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
        var entries = await reader.QueryAsync(
            action: "entity.created",
            tenantId: "tenant-1",
            cancellationToken: AbortToken
        );

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
        var roundTripped = await reader.QueryAsync(
            action: "batch.write",
            tenantId: "tenant-batch",
            limit: totalEntries + 10,
            cancellationToken: AbortToken
        );

        // then
        roundTripped.Should().HaveCount(totalEntries);
        roundTripped.Select(e => e.EntityId).Should().BeEquivalentTo(entries.Select(e => e.EntityId));
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
