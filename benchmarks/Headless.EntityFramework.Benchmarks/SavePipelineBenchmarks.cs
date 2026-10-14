// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework.Benchmarks;

#pragma warning disable CA1001 // BenchmarkDotNet releases the host in [GlobalCleanup], not through IDisposable.

/// <summary>
/// One audited save over a context that tracks many unchanged entities and changes one, which is where every extra
/// <c>ChangeTracker.Entries()</c> pass (and its change detection) shows.
/// </summary>
[MemoryDiagnoser]
public class SavePipelineBenchmarks
{
    private BenchmarkHost _host = null!;
    private AsyncServiceScope _scope;
    private BenchmarkDbContext _db = null!;
    private AuditedOrder[] _orders = null!;

    private const int _UnchangedCount = 1_000;

    [GlobalSetup]
    public async Task Setup()
    {
        _host = new BenchmarkHost(trackNavigationChanges: false);
        _scope = _host.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<BenchmarkDbContext>();

        for (var i = 0; i <= _UnchangedCount; i++)
        {
            _db.Orders.Add(
                new AuditedOrder
                {
                    Id = Guid.NewGuid(),
                    CustomerName = FormattableString.Invariant($"customer-{i}"),
                    Amount = i,
                }
            );
        }

        await _db.SaveChangesAsync().ConfigureAwait(false);
        _orders = [.. _db.Orders.Local];
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _scope.DisposeAsync().ConfigureAwait(false);
        _host.Dispose();
    }

    [Benchmark]
    public async Task<int> SaveOneModifiedAmongUnchanged()
    {
        _orders[0].Amount++;

        return await _db.SaveChangesAsync().ConfigureAwait(false);
    }
}

/// <summary>Audited inserts with client-generated keys: the number of statements the audit rows add per save.</summary>
[MemoryDiagnoser]
public class AuditedInsertBenchmarks
{
    private BenchmarkHost _host = null!;

    [Params(1, 10, 100)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup() => _host = new BenchmarkHost(trackNavigationChanges: false);

    [GlobalCleanup]
    public void Cleanup() => _host.Dispose();

    [Benchmark]
    public async Task<int> InsertAudited()
    {
        await using var scope = _host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BenchmarkDbContext>();

        for (var i = 0; i < Rows; i++)
        {
            db.Orders.Add(
                new AuditedOrder
                {
                    Id = Guid.NewGuid(),
                    CustomerName = "customer",
                    Amount = i,
                }
            );
        }

        return await db.SaveChangesAsync().ConfigureAwait(false);
    }
}
