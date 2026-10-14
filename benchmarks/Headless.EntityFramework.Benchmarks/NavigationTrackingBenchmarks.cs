// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework.Benchmarks;

#pragma warning disable CA1001 // BenchmarkDotNet releases the host in [GlobalCleanup], not through IDisposable.

/// <summary>
/// A tracking query over entities that have collection navigations, with the navigation-change tracker off (the
/// default) and on. On is the cost every context paid before the tracker became opt-in.
/// </summary>
[MemoryDiagnoser]
public class NavigationTrackingBenchmarks
{
    private const int _RowCount = 10_000;

    private BenchmarkHost _host = null!;

    [Params(false, true)]
    public bool TrackNavigationChanges { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _host = new BenchmarkHost(TrackNavigationChanges);
        using var scope = _host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BenchmarkDbContext>();

        for (var i = 0; i < _RowCount; i++)
        {
            db.Parents.Add(new Parent { Id = Guid.NewGuid(), Name = FormattableString.Invariant($"parent-{i}") });
        }

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.SaveChanges();
    }

    [GlobalCleanup]
    public void Cleanup() => _host.Dispose();

    [Benchmark]
    public async Task<int> TrackingQuery10kRows()
    {
        await using var scope = _host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BenchmarkDbContext>();

        return (await db.Parents.ToListAsync().ConfigureAwait(false)).Count;
    }
}
