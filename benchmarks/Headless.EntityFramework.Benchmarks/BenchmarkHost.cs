// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Headless.EntityFramework.Benchmarks;

/// <summary>Counts the EF events that show how many passes and round trips one save costs.</summary>
internal sealed class EfCallCounter
{
    public int DetectChanges { get; private set; }

    public int Commands { get; private set; }

    // EF Core's SQLite provider sends one statement per command, so Commands grows with rows whatever the pipeline
    // does; SaveChanges calls are what show whether the audit rows ride in the same batch.
    public int SaveChanges { get; private set; }

    public void Reset()
    {
        SaveChanges = 0;
        DetectChanges = 0;
        Commands = 0;
    }

    public void Log(EventId eventId)
    {
        if (eventId.Id == CoreEventId.DetectChangesStarting.Id)
        {
            DetectChanges++;
        }
        else if (eventId.Id == CoreEventId.SaveChangesStarting.Id)
        {
            SaveChanges++;
        }
        else if (eventId.Id == RelationalEventId.CommandExecuted.Id)
        {
            Commands++;
        }
    }
}

/// <summary>
/// A DI container over one database. By default that is in-memory SQLite, which runs in-process, so absolute times
/// exclude network and server cost: read them as relative cost of the framework's own work, not as production latency.
/// Set <c>HEADLESS_BENCH_POSTGRES</c> to a server connection string (any database name) to run against PostgreSQL
/// instead, in a throwaway database per host, where round trips and transactions are real.
/// </summary>
internal sealed class BenchmarkHost : IDisposable
{
    private static readonly string? _PostgresConnectionString = Environment.GetEnvironmentVariable(
        "HEADLESS_BENCH_POSTGRES"
    );

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly string? _postgresDatabase;
    private readonly ServiceProvider _provider;

    public BenchmarkHost()
    {
        string? postgres = null;

        if (_PostgresConnectionString is not null)
        {
            _postgresDatabase = $"bench_{Guid.NewGuid():N}";
            postgres = new NpgsqlConnectionStringBuilder(_PostgresConnectionString)
            {
                Database = _postgresDatabase,
                Pooling = true,
            }.ConnectionString;
        }
        else
        {
            _connection.Open();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContext<BenchmarkDbContext>(options =>
            (postgres is null ? options.UseSqlite(_connection) : options.UseNpgsql(postgres)).LogTo(
                (id, _) =>
                    id == CoreEventId.DetectChangesStarting
                    || id == CoreEventId.SaveChangesStarting
                    || id == RelationalEventId.CommandExecuted,
                data => Counter.Log(data.EventId)
            )
        );
        services.AddHeadlessAuditLog(setup => setup.UseEntityFramework<BenchmarkDbContext>());
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<BenchmarkDbContext>().Database.EnsureCreated();
    }

    public EfCallCounter Counter { get; } = new();

    public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

    public void Dispose()
    {
        if (_postgresDatabase is not null)
        {
            using var scope = _provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BenchmarkDbContext>().Database.EnsureDeleted();
        }

        _provider.Dispose();
        _connection.Dispose();
    }
}
