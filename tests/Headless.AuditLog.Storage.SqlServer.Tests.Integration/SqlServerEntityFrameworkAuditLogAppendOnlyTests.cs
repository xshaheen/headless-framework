// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Entity Framework storage on SQL Server with the table made append-only by the migration helper: direct changes are
/// refused, and the retention purge still passes, with or without a retrying execution strategy.
/// </summary>
[Collection<SqlServerAuditLogFixture>]
public sealed class SqlServerEntityFrameworkAuditLogAppendOnlyTests(SqlServerAuditLogFixture fixture) : TestBase
{
    private const string _Schema = "audit_log_sql_ef_append_only";
    private static readonly DateTime _Expired = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData($"UPDATE [{_Schema}].[AuditLogEntries] SET [Action] = 'tampered';")]
    [InlineData($"DELETE FROM [{_Schema}].[AuditLogEntries];")]
    public async Task should_refuse_changes_to_written_rows_after_the_helper_added_the_trigger(string statement)
    {
        // given
        await using var services = await _CreateAsync(retryOnFailure: false);
        await _AddAsync(services, ("entity.created", _Expired));

        // when
        var act = () => _ExecuteAsync(statement);

        // then
        (await act.Should().ThrowAsync<SqlException>())
            .Which.Message.Should()
            .Contain("append-only");
        (await _ActionsAsync()).Should().Equal("entity.created");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_purge_expired_rows_through_the_trigger_with_or_without_a_retrying_strategy(
        bool retryOnFailure
    )
    {
        // given - a retrying execution strategy refuses a transaction the caller opened itself
        await using var services = await _CreateAsync(retryOnFailure);
        await _AddAsync(
            services,
            ("expired.first", _Expired),
            ("expired.second", _Expired.AddMinutes(1)),
            ("recent", DateTime.UtcNow)
        );
        var retention = services.GetRequiredService<IAuditLogRetention<AuditDbContext>>();

        // when
        var first = await retention.PurgeAsync(TimeSpan.FromDays(30), limit: 1, AbortToken);
        var second = await retention.PurgeAsync(TimeSpan.FromDays(30), limit: 10, AbortToken);
        var third = await retention.PurgeAsync(TimeSpan.FromDays(30), limit: 10, AbortToken);
        var afterPurge = () => _ExecuteAsync($"DELETE FROM [{_Schema}].[AuditLogEntries];");

        // then - and the purge left nothing on the pooled connection that lets a later delete through
        first.Should().Be(1);
        second.Should().Be(1);
        third.Should().Be(0);
        await afterPurge.Should().ThrowAsync<SqlException>();
        (await _ActionsAsync()).Should().Equal("recent");
    }

    private async Task<ServiceProvider> _CreateAsync(bool retryOnFailure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContextFactory<AuditDbContext>(options =>
            options.UseSqlServer(
                fixture.ConnectionString,
                sqlServer =>
                {
                    if (retryOnFailure)
                    {
                        sqlServer.EnableRetryOnFailure();
                    }
                }
            )
        );
        services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = _Schema);
            setup.UseSqlServerEntityFramework<AuditDbContext>();
        });
        var provider = services.BuildServiceProvider();

        // What an application's migration does: the scaffolded table, then the helper.
        var factory = provider.GetRequiredService<IDbContextFactory<AuditDbContext>>();
        await using var context = await factory.CreateDbContextAsync(AbortToken);
        var migrationBuilder = new MigrationBuilder(context.Database.ProviderName);
        migrationBuilder.AddHeadlessAuditLogAppendOnly(schema: _Schema);
        await _ExecuteAsync(
            $"""
            IF OBJECT_ID(N'[{_Schema}].[AuditLogEntries]') IS NOT NULL DROP TABLE [{_Schema}].[AuditLogEntries];
            IF SCHEMA_ID(N'{_Schema}') IS NULL EXEC(N'CREATE SCHEMA [{_Schema}]');
            """
        );
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(AbortToken);
        await _ExecuteAsync(((SqlOperation)migrationBuilder.Operations[0]).Sql);

        return provider;
    }

    private async Task _AddAsync(IServiceProvider services, params (string Action, DateTime CreatedAt)[] entries)
    {
        var factory = services.GetRequiredService<IDbContextFactory<AuditDbContext>>();
        await using var context = await factory.CreateDbContextAsync(AbortToken);

        foreach (var (action, createdAt) in entries)
        {
            context.Set<AuditLogEntry>().Add(new AuditLogEntry { Action = action, CreatedAt = createdAt });
        }

        await context.SaveChangesAsync(AbortToken);
    }

    private async Task _ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task<List<string>> _ActionsAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"SELECT [Action] FROM [{_Schema}].[AuditLogEntries] ORDER BY [CreatedAt], [Id];",
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        var actions = new List<string>();

        while (await reader.ReadAsync(AbortToken))
        {
            actions.Add(reader.GetString(0));
        }

        return actions;
    }

    private sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessAuditLog(
                new AuditLogStorageOptions { Schema = _Schema },
                StorageNamingStyle.PascalCase
            );
        }
    }
}
