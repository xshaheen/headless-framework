// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Tests;

/// <summary>
/// Drives <see cref="AuditLogOptions.MissingTransactionStrategy"/> through a real <see cref="HeadlessDbContext"/> save
/// and <see cref="IAuditLog{TContext}"/> on the same PostgreSQL database.
/// </summary>
[Collection<PostgreSqlAuditLogFixture>]
public sealed class PostgreSqlAuditLogMissingTransactionTests(PostgreSqlAuditLogFixture fixture) : TestBase
{
    private const string _AuditSchema = "audit_log_pg_strict";
    private const string _AppSchema = "audit_log_pg_strict_app";

    [Fact]
    public async Task should_commit_entity_and_audit_row_together_when_strict_save_opens_no_transaction()
    {
        // given — the save pipeline begins its own transaction for an audited save, so the store joins it
        using var host = await _StartHostAsync(MissingTransactionStrategy.Throw);
        var id = Guid.NewGuid();

        // when
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StrictAuditDbContext>();
            db.Notes.Add(new AuditedNote { Id = id, Title = "first" });
            await db.SaveChangesAsync(AbortToken);
        }

        // then
        (await _CountNotesAsync(host, id))
            .Should()
            .Be(1);
        (await _CountAuditRowsByEntityIdAsync(id)).Should().Be(1);
    }

    [Fact]
    public async Task should_leave_no_audit_row_when_the_strict_entity_save_fails()
    {
        // given — a committed note, then a second insert with the same key that fails after the audit row is written
        using var host = await _StartHostAsync(MissingTransactionStrategy.Throw);
        var id = Guid.NewGuid();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StrictAuditDbContext>();
            db.Notes.Add(new AuditedNote { Id = id, Title = "first" });
            await db.SaveChangesAsync(AbortToken);
        }

        // when
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StrictAuditDbContext>();
            db.Notes.Add(new AuditedNote { Id = id, Title = "duplicate" });
            var act = () => db.SaveChangesAsync(AbortToken);

            await act.Should().ThrowAsync<DbUpdateException>();
        }

        // then — only the first save's audit row exists
        (await _CountNotesAsync(host, id))
            .Should()
            .Be(1);
        (await _CountAuditRowsByEntityIdAsync(id)).Should().Be(1);
    }

    [Fact]
    public async Task should_remove_entity_and_audit_row_when_the_caller_rolls_back_a_strict_save()
    {
        // given
        using var host = await _StartHostAsync(MissingTransactionStrategy.Throw);
        var id = Guid.NewGuid();

        // when
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StrictAuditDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);
            db.Notes.Add(new AuditedNote { Id = id, Title = "rolled back" });
            await db.SaveChangesAsync(AbortToken);
            await transaction.RollbackAsync(AbortToken);
        }

        // then
        (await _CountNotesAsync(host, id))
            .Should()
            .Be(0);
        (await _CountAuditRowsByEntityIdAsync(id)).Should().Be(0);
    }

    [Fact]
    public async Task should_throw_and_write_nothing_when_strict_log_runs_outside_a_transaction()
    {
        // given
        using var host = await _StartHostAsync(MissingTransactionStrategy.Throw);
        await using var scope = host.Services.CreateAsyncScope();
        var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLog<StrictAuditDbContext>>();

        // when
        var act = () => auditLog.LogAsync(new AuditLogWriteRequest { Action = "strict.log_outside" }, AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{typeof(StrictAuditDbContext).FullName}*has no active transaction*");
        (await _CountAuditRowsByActionAsync("strict.log_outside")).Should().Be(0);
    }

    [Fact]
    public async Task should_commit_strict_log_with_the_caller_transaction()
    {
        // given
        using var host = await _StartHostAsync(MissingTransactionStrategy.Throw);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StrictAuditDbContext>();
        var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLog<StrictAuditDbContext>>();
        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);

        // when
        await auditLog.LogAsync(new AuditLogWriteRequest { Action = "strict.log_commit" }, AbortToken);
        var visibleBeforeCommit = await _CountAuditRowsByActionAsync("strict.log_commit");
        await transaction.CommitAsync(AbortToken);

        // then
        visibleBeforeCommit.Should().Be(0);
        (await _CountAuditRowsByActionAsync("strict.log_commit")).Should().Be(1);
    }

    [Theory]
    [InlineData(MissingTransactionStrategy.Throw)]
    [InlineData(MissingTransactionStrategy.Continue)]
    public async Task should_roll_back_log_with_the_caller_transaction(MissingTransactionStrategy strategy)
    {
        // given
        using var host = await _StartHostAsync(strategy);
        var action = $"strict.log_rollback_{strategy}";
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StrictAuditDbContext>();
        var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLog<StrictAuditDbContext>>();
        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);

        // when
        await auditLog.LogAsync(new AuditLogWriteRequest { Action = action }, AbortToken);
        await transaction.RollbackAsync(AbortToken);

        // then
        (await _CountAuditRowsByActionAsync(action))
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task should_commit_log_on_its_own_connection_when_strategy_is_continue_and_no_transaction()
    {
        // given
        using var host = await _StartHostAsync(MissingTransactionStrategy.Continue);
        await using var scope = host.Services.CreateAsyncScope();
        var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLog<StrictAuditDbContext>>();

        // when
        await auditLog.LogAsync(new AuditLogWriteRequest { Action = "lenient.log_outside" }, AbortToken);

        // then
        (await _CountAuditRowsByActionAsync("lenient.log_outside"))
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task should_commit_writer_entry_on_its_own_connection_whatever_the_strict_strategy()
    {
        // given — the caller holds a transaction it rolls back; the standalone writer ignores it
        using var host = await _StartHostAsync(MissingTransactionStrategy.Throw);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StrictAuditDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditLogWriter<StrictAuditDbContext>>();
        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);

        // when
        await writer.WriteAsync(new AuditLogWriteRequest { Action = "strict.writer" }, AbortToken);
        await transaction.RollbackAsync(AbortToken);

        // then
        (await _CountAuditRowsByActionAsync("strict.writer"))
            .Should()
            .Be(1);
    }

    private async Task<IHost> _StartHostAsync(MissingTransactionStrategy strategy)
    {
        await _ExecuteAsync(
            $"""DROP SCHEMA IF EXISTS "{_AuditSchema}" CASCADE; DROP SCHEMA IF EXISTS "{_AppSchema}" CASCADE;"""
        );

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessDbContext<StrictAuditDbContext>(options =>
            options.UseNpgsql(fixture.ConnectionString)
        );
        builder.Services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureOptions(options => options.MissingTransactionStrategy = strategy);
            setup.ConfigureStorage(options => options.Schema = _AuditSchema);
            setup.UsePostgreSql(fixture.ConnectionString);
        });

        var host = builder.Build();
        await host.StartAsync(AbortToken);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StrictAuditDbContext>();
        await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(AbortToken);

        return host;
    }

    private static async Task<int> _CountNotesAsync(IHost host, Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StrictAuditDbContext>();

        return await db.Notes.CountAsync(note => note.Id == id, AbortToken);
    }

    private async Task<long> _CountAuditRowsByEntityIdAsync(Guid id)
    {
        return await _ScalarAsync(
            $"""SELECT COUNT(*) FROM "{_AuditSchema}"."audit_log_entries" WHERE "entity_id" = @value;""",
            id.ToString()
        );
    }

    private async Task<long> _CountAuditRowsByActionAsync(string action)
    {
        return await _ScalarAsync(
            $"""SELECT COUNT(*) FROM "{_AuditSchema}"."audit_log_entries" WHERE "action" = @value;""",
            action
        );
    }

    private async Task<long> _ScalarAsync(string sql, object value)
    {
        // A fresh connection sees only committed rows.
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("value", value);

        return (long)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private async Task _ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(AbortToken);
    }
}

public sealed class AuditedNote
{
    public Guid Id { get; set; }

    public string Title { get; set; } = "";
}

public sealed class StrictAuditDbContext(DbContextOptions<StrictAuditDbContext> options) : HeadlessDbContext(options)
{
    public DbSet<AuditedNote> Notes => Set<AuditedNote>();

    public override string DefaultSchema => "audit_log_pg_strict_app";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<AuditedNote>(b =>
        {
            b.ToTable("Notes");
            b.IsAudited();
            b.Property(e => e.Id).ValueGeneratedNever();
        });
    }
}
