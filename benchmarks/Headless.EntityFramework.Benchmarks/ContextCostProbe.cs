// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using File = Headless.Primitives.File;
using Image = Headless.Primitives.Image;

namespace Headless.EntityFramework.Benchmarks.ContextCost;

/// <summary>A row whose Guid key the context generates, with the column shapes the context probe measures.</summary>
public sealed class KeyedRow : Entity<Guid>
{
    public string Name { get; set; } = "";

    public DateTimeOffset At { get; set; }

    public DateOnly Day { get; set; }

    public File? Attachment { get; set; }

    public Image? Picture { get; set; }
}

public sealed class PlainKeyedDb(DbContextOptions<PlainKeyedDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KeyedRow>(b =>
        {
            b.Ignore(e => e.Attachment);
            b.Ignore(e => e.Picture);
        });
    }
}

public sealed class HeadlessKeyedDb(DbContextOptions options) : HeadlessDbContext(options)
{
    public override string DefaultSchema => "";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Mapped before the base call so the framework's key conventions see the type. SQLite cannot compare
        // DateTimeOffset columns; a binary column keeps the date-range predicate translatable.
        modelBuilder.Entity<KeyedRow>().Property(e => e.At).HasConversion(new DateTimeOffsetToBinaryConverter());
        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>
/// Exact per-operation allocations (current thread, warm, minimum of five batches) for context creation, key
/// generation, tracking queries, JSON column comparers, and query compilation. Run with <c>--context-probe</c>.
/// </summary>
internal static class ContextCostProbe
{
    private static object? _sink;

    public static async Task RunAsync()
    {
        await using var plainConnection = new SqliteConnection("Data Source=:memory:");
        await using var headlessConnection = new SqliteConnection("Data Source=:memory:");
        await plainConnection.OpenAsync().ConfigureAwait(false);
        await headlessConnection.OpenAsync().ConfigureAwait(false);

        var compilations = 0;

        var plainServices = new ServiceCollection();
        plainServices.AddDbContext<PlainKeyedDb>(o => o.UseSqlite(plainConnection));
        await using var plain = plainServices.BuildServiceProvider();

        var headlessServices = new ServiceCollection();
        headlessServices.AddLogging();
        headlessServices.AddHeadlessDbContext<HeadlessKeyedDb>(o =>
            o.UseSqlite(headlessConnection)
                .LogTo(
                    _ => compilations++,
                    (id, _) => id == CoreEventId.QueryCompilationStarting,
                    DbContextLoggerOptions.None
                )
        );
        await using var headless = headlessServices.BuildServiceProvider();

        _Seed<PlainKeyedDb>(plain);
        _Seed<HeadlessKeyedDb>(headless);

        _Print("scope+resolve context", "plain", _Measure(() => _InScope<PlainKeyedDb>(plain, _ => { }), 2000));
        _Print(
            "scope+resolve context",
            "headless",
            _Measure(() => _InScope<HeadlessKeyedDb>(headless, _ => { }), 2000)
        );
        _Print(
            "scope+resolve options",
            "headless",
            _Measure(
                () =>
                {
                    using var scope = headless.CreateScope();
                    _sink = scope.ServiceProvider.GetRequiredService<DbContextOptions<HeadlessKeyedDb>>();
                },
                2000
            )
        );

        _Print("add100 generated keys", "plain", _Measure(() => _InScope<PlainKeyedDb>(plain, _AddHundred), 200));
        _Print(
            "add100 generated keys",
            "headless",
            _Measure(() => _InScope<HeadlessKeyedDb>(headless, _AddHundred), 200)
        );

        _Print("list100 tracked", "plain", _Measure(() => _InScope<PlainKeyedDb>(plain, _ListHundred), 200));
        _Print("list100 tracked", "headless", _Measure(() => _InScope<HeadlessKeyedDb>(headless, _ListHundred), 200));

        using (var scope = headless.CreateScope())
        {
            var entityType = scope
                .ServiceProvider.GetRequiredService<HeadlessKeyedDb>()
                .Model.FindEntityType(typeof(KeyedRow))!;
            _ProbeComparer(
                "File",
                entityType.FindProperty(nameof(KeyedRow.Attachment))!.GetValueComparer(),
                _NewFile(),
                _NewFile()
            );
            _ProbeComparer(
                "Image",
                entityType.FindProperty(nameof(KeyedRow.Picture))!.GetValueComparer(),
                _NewImage(),
                _NewImage()
            );
        }

        compilations = 0;
        for (var i = 0; i < 5; i++)
        {
            await using var scope = headless.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HeadlessKeyedDb>();
            var start = new DateTimeOffset(2025, 1 + i, 1, 0, 0, 0, TimeSpan.Zero);
            _sink = await db.Set<KeyedRow>()
                .CountPerMonthAsync(e => e.At, start, start.AddMonths(6))
                .ConfigureAwait(false);
        }

        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"CountPerMonthAsync(DateTimeOffset), 5 date ranges: {compilations} query compilations"
            )
        );

        compilations = 0;
        for (var i = 0; i < 5; i++)
        {
            await using var scope = headless.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HeadlessKeyedDb>();
            var start = new DateOnly(2025, 1 + i, 1);
            _sink = await db.Set<KeyedRow>()
                .CountPerMonthAsync(e => e.Day, start, start.AddMonths(6))
                .ConfigureAwait(false);
        }

        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"CountPerMonthAsync(DateOnly), 5 date ranges: {compilations} query compilations"
            )
        );
    }

    private static void _Seed<TDb>(IServiceProvider services)
        where TDb : DbContext
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TDb>();
        db.Database.EnsureCreated();
        _AddHundred(db);
        db.SaveChanges();
    }

    private static void _InScope<TDb>(IServiceProvider services, Action<DbContext> action)
        where TDb : DbContext
    {
        using var scope = services.CreateScope();
        action(scope.ServiceProvider.GetRequiredService<TDb>());
    }

    private static void _AddHundred(DbContext db)
    {
        for (var i = 0; i < 100; i++)
        {
            db.Add(
                new KeyedRow
                {
                    Id = Guid.Empty,
                    Name = "n",
                    At = DateTimeOffset.UnixEpoch,
                    Day = DateOnly.MinValue,
                }
            );
        }
    }

    private static void _ListHundred(DbContext db) => _sink = db.Set<KeyedRow>().Take(100).ToList();

    private static void _ProbeComparer(string label, ValueComparer comparer, object left, object right)
    {
        _Print(
            $"{label} comparer Equals",
            comparer.GetType().Name,
            _Measure(() => _sink = comparer.Equals(left, right), 20000)
        );
        _Print(
            $"{label} comparer Snapshot",
            comparer.GetType().Name,
            _Measure(() => _sink = comparer.Snapshot(left), 20000)
        );
        _Print(
            $"{label} comparer GetHashCode",
            comparer.GetType().Name,
            _Measure(() => _sink = comparer.GetHashCode(left), 20000)
        );
    }

    private static File _NewFile() =>
        new()
        {
            Id = "f1",
            DisplayName = "invoice.pdf",
            SavedName = "9f8e7d.pdf",
            Url = "https://cdn.example.com/a/b/9f8e7d.pdf",
            Container = ["tenants", "t1"],
            ContentType = "application/pdf",
            UploadedAt = DateTimeOffset.UnixEpoch,
            Length = 123456,
            Private = true,
            Metadata = new Dictionary<string, object?>(StringComparer.Ordinal) { ["pages"] = 3, ["source"] = "upload" },
        };

    private static Image _NewImage() =>
        new()
        {
            Id = "i1",
            DisplayName = "logo.png",
            SavedName = "1a2b.png",
            Url = "https://cdn.example.com/1a2b.png",
            Container = ["tenants", "t1"],
            ContentType = "image/png",
            UploadedAt = DateTimeOffset.UnixEpoch,
            Length = 2048,
            Private = false,
            Width = 64,
            Height = 64,
            Caption = "logo",
        };

    private static long _Measure(Action operation, int iterations)
    {
        for (var i = 0; i < Math.Max(50, iterations / 2); i++)
        {
            operation();
        }

        var best = long.MaxValue;

        for (var batch = 0; batch < 5; batch++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var before = GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < iterations; i++)
            {
                operation();
            }

            best = Math.Min(best, (GC.GetAllocatedBytesForCurrentThread() - before) / iterations);
        }

        return best;
    }

    private static void _Print(string operation, string variant, long bytes) =>
        Console.WriteLine(
            string.Create(CultureInfo.InvariantCulture, $"{operation, -28} {variant, -28} {bytes, 10:N0} B/op")
        );
}
