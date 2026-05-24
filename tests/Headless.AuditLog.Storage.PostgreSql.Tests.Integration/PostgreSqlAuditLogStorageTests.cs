// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting.Initialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlAuditLogFixture>]
public sealed class PostgreSqlAuditLogStorageTests(PostgreSqlAuditLogFixture fixture)
{
    private const string _Schema = "audit_log_pg_raw";

    [Fact]
    public async Task should_initialize_table_and_round_trip_audit_entry()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();

        // when
        await host.StartAsync(TestContext.Current.CancellationToken);
        var initializer = host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single();
        using var scope = host.Services.CreateScope();
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
            TestContext.Current.CancellationToken
        );
        var entries = await reader.QueryAsync(
            action: "entity.created",
            tenantId: "tenant-1",
            cancellationToken: TestContext.Current.CancellationToken
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

    private IHost _CreateHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessAuditLog();
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
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"""DROP SCHEMA IF EXISTS "{_Schema}" CASCADE;""", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<bool> _TableExistsAsync(string tableName)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
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

        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task<string> _JsonColumnTypeAsync(string columnName)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
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

        return (string)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
