// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.AuditLog;
using Headless.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Headless.EntityFramework.Benchmarks.SaveCost;

/// <summary>A plain EF Core context that maps the audited entity and the audit-log table the Headless one writes.</summary>
public sealed class PlainAuditDb(DbContextOptions<PlainAuditDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ProbeModel.Configure(modelBuilder, headless: false);
        modelBuilder.ConfigureHeadlessAuditLog(
            new AuditLogStorageOptions(),
            HeadlessStorageNaming.ForProvider(Database.ProviderName)
        );

        if (Database.IsSqlite())
        {
            modelBuilder.Entity<AuditLogEntry>(b =>
            {
                b.HasKey(e => e.Id);
                b.Property(e => e.Id).ValueGeneratedOnAdd();
            });
        }
    }
}

/// <summary>
/// Counts the commands and transactions of one audited save next to plain EF Core saving the same statements: the entity
/// row and its audit row in one <c>SaveChanges</c>. Set <c>HEADLESS_BENCH_POSTGRES</c> to run against PostgreSQL.
/// </summary>
internal static class TransactionProbe
{
    private static readonly string? _Pg = Environment.GetEnvironmentVariable("HEADLESS_BENCH_POSTGRES");

    public static async Task RunAsync()
    {
#pragma warning disable CA1303 // Benchmark console output, not user-facing text to localize.
        Console.WriteLine(_Pg is null ? "provider: SQLite in memory" : "provider: PostgreSQL");
#pragma warning restore CA1303

        foreach (var workload in new[] { "insert-1", "update-1" })
        {
            var (plainCommands, plainTxBegin) = await _CountPlainAsync(workload).ConfigureAwait(false);
            var (headlessCommands, headlessTxBegin) = await _CountHeadlessAsync(workload).ConfigureAwait(false);
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {workload, -9} plain EF (entity + audit row): cmds={plainCommands} txBegin={plainTxBegin} | headless audited: cmds={headlessCommands} txBegin={headlessTxBegin}"
                )
            );
        }
    }

    private static async Task<(int Commands, int TxBegin)> _CountPlainAsync(string workload)
    {
        var counter = new ProbeCounter();
        var (options, cleanup) = _Options(counter);
        var services = new ServiceCollection();
        services.AddDbContext<PlainAuditDb>(options);
        await using var provider = services.BuildServiceProvider();

        try
        {
            await using (var setup = provider.CreateAsyncScope())
            {
                await setup.ServiceProvider.GetRequiredService<PlainAuditDb>().Database.EnsureCreatedAsync();
            }

            for (var i = 0; i < 3; i++)
            {
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<PlainAuditDb>();
                var row = await _PrepareRowAsync(db, workload).ConfigureAwait(false);
                counter.Reset();
                db.Add(_AuditRow(row.Id));
                await db.SaveChangesAsync().ConfigureAwait(false);
            }

            // Read before the cleanup below, whose commands the counter would also see.
            return (counter.Commands, counter.TxBegin);
        }
        finally
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<PlainAuditDb>().Database.EnsureDeletedAsync();
            cleanup();
        }
    }

    private static async Task<(int Commands, int TxBegin)> _CountHeadlessAsync(string workload)
    {
        var counter = new ProbeCounter();
        var (options, cleanup) = _Options(counter);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContext<AuditDb>(options);
        services.AddHeadlessAuditLog(setup => setup.UseEntityFramework<AuditDb>());
        await using var provider = services.BuildServiceProvider();

        try
        {
            await using (var setup = provider.CreateAsyncScope())
            {
                await setup.ServiceProvider.GetRequiredService<AuditDb>().Database.EnsureCreatedAsync();
            }

            for (var i = 0; i < 3; i++)
            {
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AuditDb>();
                await _PrepareRowAsync(db, workload).ConfigureAwait(false);
                counter.Reset();
                await db.SaveChangesAsync().ConfigureAwait(false);
            }

            // Read before the cleanup below, whose commands the counter would also see.
            return (counter.Commands, counter.TxBegin);
        }
        finally
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AuditDb>().Database.EnsureDeletedAsync();
            cleanup();
        }
    }

    // Leaves the workload's change pending: a new row to insert, or a stored row with one modified property.
    private static async Task<AuditedRow> _PrepareRowAsync(DbContext db, string workload)
    {
        var row = new AuditedRow
        {
            Id = Guid.NewGuid(),
            Name = "n",
            Amount = 1,
            Note = "x",
        };
        db.Add(row);

        if (string.Equals(workload, "update-1", StringComparison.Ordinal))
        {
            await db.SaveChangesAsync().ConfigureAwait(false);
            row.Note = "y";
        }

        return row;
    }

    private static AuditLogEntry _AuditRow(Guid entityId) =>
        new()
        {
            CreatedAt = DateTime.UtcNow,
            Action = "entity.created",
            ChangeType = AuditChangeType.Created,
            EntityType = typeof(AuditedRow).FullName,
            EntityId = entityId.ToString(),
            NewValues = new Dictionary<string, object?> { ["Note"] = "x" },
        };

    private static (Action<DbContextOptionsBuilder> Options, Action Cleanup) _Options(ProbeCounter counter)
    {
        SqliteConnection? sqlite = null;
        string? pg = null;

        if (_Pg is null)
        {
#pragma warning disable CA2000 // False positive: the returned cleanup disposes it once the probe's database is deleted.
            sqlite = new SqliteConnection("Data Source=:memory:");
#pragma warning restore CA2000
            sqlite.Open();
        }
        else
        {
            pg = new NpgsqlConnectionStringBuilder(_Pg) { Database = $"txprobe_{Guid.NewGuid():N}" }.ConnectionString;
        }

        return (
            o =>
            {
                if (pg is null)
                {
                    o.UseSqlite(sqlite!);
                }
                else
                {
                    o.UseNpgsql(pg);
                }

                o.LogTo(
                    (id, _) => id == RelationalEventId.CommandExecuted || id == RelationalEventId.TransactionStarted,
                    d => counter.Log(d.EventId)
                );
            },
            () => sqlite?.Dispose()
        );
    }
}
