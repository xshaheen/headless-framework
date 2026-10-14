// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
/// A DI container over one in-memory SQLite connection. SQLite runs in-process, so absolute times exclude network and
/// server cost: read the numbers as relative cost of the framework's own work, not as production latency.
/// </summary>
internal sealed class BenchmarkHost : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _provider;

    public BenchmarkHost(bool trackNavigationChanges)
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContext<BenchmarkDbContext>(
            options =>
                options
                    .UseSqlite(_connection)
                    .LogTo(
                        (id, _) =>
                            id == CoreEventId.DetectChangesStarting
                            || id == CoreEventId.SaveChangesStarting
                            || id == RelationalEventId.CommandExecuted,
                        data => Counter.Log(data.EventId)
                    ),
            headless => headless.TrackNavigationChanges = trackNavigationChanges
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
        _provider.Dispose();
        _connection.Dispose();
    }
}
