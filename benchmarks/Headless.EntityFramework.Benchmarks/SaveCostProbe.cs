// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Claims;
using Headless.AuditLog;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Hosting;
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.EntityFramework.Benchmarks.SaveCost;

// ---- entities (same CLR types are mapped by the plain-EF and the Headless contexts) ----

public sealed class PlainRow : Entity<Guid>
{
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
}

public sealed class StampedRow : AuditedEntity<Guid>, IHasConcurrencyStamp
{
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public string? ConcurrencyStamp { get; set; }
}

public sealed class TenantRow : Entity<Guid>, IMultiTenant
{
    public string? TenantId { get; set; }
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
}

public sealed class AuditedRow : Entity<Guid>
{
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public string Note { get; set; } = "";
}

public sealed class OrderPlaced;

public sealed class OrderShipped;

public sealed class OrderRoot : AggregateRoot<Guid>
{
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }

    public void PlaceOrder() => AddDomainEvent(new OrderPlaced());

    public void ShipOrder() => AddIntegrationEvent(new OrderShipped());
}

internal static class ProbeModel
{
    public static void Configure(ModelBuilder mb, bool headless)
    {
        mb.Entity<PlainRow>().Property(e => e.Id).ValueGeneratedNever();
        mb.Entity<StampedRow>(b =>
        {
            b.Property(e => e.Id).ValueGeneratedNever();
            if (!headless)
            {
                b.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
            }
        });
        mb.Entity<TenantRow>().Property(e => e.Id).ValueGeneratedNever();
        mb.Entity<AuditedRow>().Property(e => e.Id).ValueGeneratedNever();
        mb.Entity<OrderRoot>().Property(e => e.Id).ValueGeneratedNever();
    }
}

public sealed class PlainDb(DbContextOptions<PlainDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => ProbeModel.Configure(modelBuilder, false);
}

public abstract class ProbeHeadlessDb(DbContextOptions options) : HeadlessDbContext(options)
{
    public override string DefaultSchema => "";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ProbeModel.Configure(modelBuilder, true);
    }
}

public sealed class BareDb(DbContextOptions options) : ProbeHeadlessDb(options);

public sealed class EventsDb(DbContextOptions options) : ProbeHeadlessDb(options);

public sealed class TenantDb(DbContextOptions options) : ProbeHeadlessDb(options);

public sealed class AuditDb(DbContextOptions options, IOptions<AuditLogStorageOptions> auditLogStorage)
    : ProbeHeadlessDb(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<AuditedRow>().IsAudited();
        modelBuilder.ConfigureHeadlessAuditLog(
            auditLogStorage.Value,
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

internal sealed class NoopOutbox : IHeadlessOutboxDispatcher
{
    public Task DispatchAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyList<EventContext<object>> integrationEvents,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public void Dispatch(IUnitOfWork unitOfWork, IReadOnlyList<EventContext<object>> integrationEvents) { }
}

internal sealed class NoopHandler<T> : IDomainEventHandler<T>
    where T : class
{
    public ValueTask HandleAsync(EventContext<T> context, CancellationToken cancellationToken = default) => default;
}

internal sealed class ProbeCounter
{
    public int Detect { get; private set; }

    public int Saves { get; private set; }

    public int Commands { get; private set; }

    public int TxBegin { get; private set; }

    public int TxCommit { get; private set; }

    public void Reset() => Detect = Saves = Commands = TxBegin = TxCommit = 0;

    public void Log(EventId id)
    {
        if (id.Id == CoreEventId.DetectChangesStarting.Id)
        {
            Detect++;
        }
        else if (id.Id == CoreEventId.SaveChangesStarting.Id)
        {
            Saves++;
        }
        else if (id.Id == RelationalEventId.CommandExecuted.Id)
        {
            Commands++;
        }
        else if (id.Id == RelationalEventId.TransactionStarted.Id)
        {
            TxBegin++;
        }
        else if (id.Id == RelationalEventId.TransactionCommitted.Id)
        {
            TxCommit++;
        }
    }
}

internal enum Kind
{
    Plain,
    Stamped,
    Tenant,
    Audited,
    OrderLifecycle,
    OrderDomainEvent,
    OrderIntegration,
}

internal sealed class Env : IDisposable
{
    private static readonly string? _Pg = Environment.GetEnvironmentVariable("HEADLESS_BENCH_POSTGRES");
    private readonly SqliteConnection? _sqlite;
    private readonly string? _pgDb;
    private readonly ServiceProvider _provider;
    private readonly Type _dbType;

    public Env(string name, Type dbType, bool counted, Action<IServiceCollection>? configure = null)
    {
        Name = name;
        _dbType = dbType;
        Counter = new ProbeCounter();
        string? pg = null;

        if (_Pg is not null)
        {
            _pgDb = $"probe_{Guid.NewGuid():N}";
            pg = new NpgsqlConnectionStringBuilder(_Pg) { Database = _pgDb, Pooling = true }.ConnectionString;
        }
        else
        {
            _sqlite = new SqliteConnection("Data Source=:memory:");
            _sqlite.Open();
        }

        void Options(DbContextOptionsBuilder o)
        {
            if (pg is null)
            {
                o.UseSqlite(_sqlite!);
            }
            else
            {
                o.UseNpgsql(pg);
            }

            if (counted)
            {
                o.LogTo(
                    (id, _) =>
                        id == CoreEventId.DetectChangesStarting
                        || id == CoreEventId.SaveChangesStarting
                        || id == RelationalEventId.CommandExecuted
                        || id == RelationalEventId.TransactionStarted
                        || id == RelationalEventId.TransactionCommitted,
                    d => Counter.Log(d.EventId)
                );
            }
        }

        var services = new ServiceCollection();
        services.AddLogging();

        if (dbType == typeof(PlainDb))
        {
            services.AddDbContext<PlainDb>(Options);
        }
        else
        {
            // Registered by closed generic method below.
            typeof(Env)
                .GetMethod(
                    nameof(_AddHeadless),
                    System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Static
                        | System.Reflection.BindingFlags.DeclaredOnly
                )!
                .MakeGenericMethod(dbType)
                .Invoke(null, [services, (Action<DbContextOptionsBuilder>)Options]);
        }

        configure?.Invoke(services);
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService(dbType).As<DbContext>().Database.EnsureCreated();
    }

    private static void _AddHeadless<TDb>(IServiceCollection services, Action<DbContextOptionsBuilder> options)
        where TDb : HeadlessDbContext
    {
        services.AddHeadlessDbContext<TDb>(options);
    }

    public string Name { get; }

    public ProbeCounter Counter { get; }

    public IServiceProvider Provider => _provider;

    public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

    public DbContext Resolve(IServiceProvider sp) => (DbContext)sp.GetRequiredService(_dbType);

    public void Dispose()
    {
        if (_pgDb is not null)
        {
            using var scope = _provider.CreateScope();
            Resolve(scope.ServiceProvider).Database.EnsureDeleted();
        }

        _provider.Dispose();
        _sqlite?.Dispose();
    }
}

internal static class ObjectExt
{
    public static T As<T>(this object o) => (T)o;
}

internal static class SaveCostProbe
{
    private const int _Tracked = 100;

    public static async Task RunAsync(string[] args)
    {
        var iters = int.Parse(
            Environment.GetEnvironmentVariable("HEADLESS_PROBE_ITERS") ?? "3000",
            CultureInfo.InvariantCulture
        );
        var filter = args.SkipWhile(a => a != "--filter").Skip(1).FirstOrDefault();

        // Counts first (LogTo attached), then clean allocation/time runs (no logger attached).
        var envs = new (string Scenario, Kind Kind, Func<bool, Env> Plain, Func<bool, Env> Headless)[]
        {
            (
                "plain-entity",
                Kind.Plain,
                c => new Env("plain", typeof(PlainDb), c),
                c => new Env("headless-bare", typeof(BareDb), c)
            ),
            (
                "stamped(IHasConcurrencyStamp+IAudit)",
                Kind.Stamped,
                c => new Env("plain", typeof(PlainDb), c),
                c => new Env("headless-bare", typeof(BareDb), c)
            ),
            (
                "tenant-guard",
                Kind.Tenant,
                c => new Env("plain", typeof(PlainDb), c),
                c => new Env("headless-tenant-guard", typeof(TenantDb), c, _GuardOn)
            ),
            (
                "audit-registered,entity-not-audited(plain row)",
                Kind.Plain,
                c => new Env("plain", typeof(PlainDb), c),
                c => new Env("headless-audit-registered", typeof(AuditDb), c, _AuditOn<AuditDb>)
            ),
            (
                "audited-entity",
                Kind.Audited,
                c => new Env("plain", typeof(PlainDb), c),
                c => new Env("headless-audit", typeof(AuditDb), c, _AuditOn<AuditDb>)
            ),
            (
                "aggregate,lifecycle-events,no-handler",
                Kind.OrderLifecycle,
                c => new Env("plain", typeof(PlainDb), c),
                c => new Env("headless-events", typeof(EventsDb), c, _EventsOn)
            ),
            (
                "aggregate,domain-event-raised,no-handler",
                Kind.OrderDomainEvent,
                c => new Env("plain", typeof(PlainDb), c),
                c => new Env("headless-events", typeof(EventsDb), c, _EventsOn)
            ),
            (
                "aggregate,integration-event,stub-outbox",
                Kind.OrderIntegration,
                c => new Env("plain", typeof(PlainDb), c),
                c => new Env("headless-events", typeof(EventsDb), c, _EventsOn)
            ),
        };

        foreach (var (scenario, kind, plain, headless) in envs)
        {
            if (filter is not null && !scenario.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Console.WriteLine($"== {scenario}");
            foreach (var workload in new[] { "noop-save", "update-1", "insert-1", "insert-100" })
            {
                var rows = new List<(string Name, Result R)>();
                foreach (var make in new[] { plain, headless })
                {
                    string name;
                    Counts counts;
                    using (var env = make(true))
                    {
                        name = env.Name;
                        counts = await _CountAsync(env, kind, workload).ConfigureAwait(false);
                    }

                    using var clean = make(false);
                    var r = await _MeasureAsync(clean, kind, workload, iters).ConfigureAwait(false);
                    rows.Add((name, r with { Counts = counts }));
                }

                var basis = rows[0].R;
                foreach (var (name, r) in rows)
                {
                    Console.WriteLine(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"  {workload, -11} {name, -26} {r.BytesPerOp, 9:N0} B/op  {r.MicrosPerOp, 8:N1} us/op  detect={r.Counts.Detect} saves={r.Counts.Saves} cmds={r.Counts.Commands} txBegin={r.Counts.TxBegin} txCommit={r.Counts.TxCommit}"
                        )
                    );
                }

                var h = rows[1].R;
                Console.WriteLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"  {workload, -11} => headless adds {h.BytesPerOp - basis.BytesPerOp, 9:N0} B/op, {h.MicrosPerOp - basis.MicrosPerOp, 8:N1} us/op (noisy)"
                    )
                );
            }
        }

        // Per-scope: scope + context + one insert + dispose. Includes pipeline/processor resolution on first save.
        Console.WriteLine("== per-scope end-to-end (scope, resolve ctx, insert 1, save, dispose)");
        foreach (
            var (label, make) in new (string, Func<bool, Env>)[]
            {
                ("plain", c => new Env("plain", typeof(PlainDb), c)),
                ("headless-bare", c => new Env("headless-bare", typeof(BareDb), c)),
                (
                    "headless-audit-registered",
                    c => new Env("headless-audit-registered", typeof(AuditDb), c, _AuditOn<AuditDb>)
                ),
            }
        )
        {
            if (filter is not null && !"per-scope".Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var env = make(false);
            var withSave = await _MeasureScopeAsync(env, save: true, iters / 3).ConfigureAwait(false);
            var noSave = await _MeasureScopeAsync(env, save: false, iters / 3).ConfigureAwait(false);
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {label, -26} scope+ctx+insert+save {withSave.BytesPerOp, 9:N0} B  {withSave.MicrosPerOp, 8:N1} us | scope+ctx only {noSave.BytesPerOp, 9:N0} B  {noSave.MicrosPerOp, 8:N1} us"
                )
            );
        }
    }

    private static void _GuardOn(IServiceCollection services)
    {
        services.PostConfigure<TenantGuardOptions>(o =>
            typeof(TenantGuardOptions)
                .GetProperty(
                    nameof(TenantGuardOptions.GuardWrites),
                    System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.DeclaredOnly
                )!
                .SetValue(o, true)
        );
    }

    private static void _AuditOn<TDb>(IServiceCollection services)
        where TDb : HeadlessDbContext
    {
        services.AddHeadlessAuditLog(setup => setup.UseEntityFramework<TDb>());
    }

    private static void _EventsOn(IServiceCollection services)
    {
        services.AddHeadlessDomainEventDispatcher();
        services.AddSingleton<IHeadlessOutboxDispatcher, NoopOutbox>();
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Counts(int Detect, int Saves, int Commands, int TxBegin, int TxCommit);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Result(double BytesPerOp, double MicrosPerOp, Counts Counts);

    private static object _New(Kind kind, bool headlessEnv)
    {
        var id = Guid.NewGuid();
        return kind switch
        {
            Kind.Plain => new PlainRow
            {
                Id = id,
                Name = "n",
                Amount = 1,
            },
            Kind.Stamped => new StampedRow
            {
                Id = id,
                Name = "n",
                Amount = 1,
            },
            Kind.Tenant => new TenantRow
            {
                Id = id,
                Name = "n",
                Amount = 1,
                TenantId = headlessEnv ? null : "t1",
            },
            Kind.Audited => new AuditedRow
            {
                Id = id,
                Name = "n",
                Amount = 1,
                Note = "x",
            },
            Kind.OrderLifecycle => new OrderRoot
            {
                Id = id,
                Name = "n",
                Amount = 1,
            },
            Kind.OrderDomainEvent => _WithDomain(
                new OrderRoot
                {
                    Id = id,
                    Name = "n",
                    Amount = 1,
                }
            ),
            Kind.OrderIntegration => _WithIntegration(
                new OrderRoot
                {
                    Id = id,
                    Name = "n",
                    Amount = 1,
                }
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static OrderRoot _WithDomain(OrderRoot o)
    {
        o.PlaceOrder();
        return o;
    }

    private static OrderRoot _WithIntegration(OrderRoot o)
    {
        o.ShipOrder();
        return o;
    }

    private static void _Mutate(object e)
    {
        switch (e)
        {
            case PlainRow p:
                p.Amount++;
                break;
            case StampedRow s:
                s.Amount++;
                break;
            case TenantRow t:
                t.Amount++;
                break;
            case AuditedRow a:
                a.Amount++;
                break;
            case OrderRoot o:
                o.Amount++;
                // events raised per save for the event-scenarios
                break;
        }
    }

    private static void _RaiseFor(Kind kind, object e)
    {
        if (e is OrderRoot o)
        {
            if (kind == Kind.OrderDomainEvent)
            {
                o.PlaceOrder();
            }
            else if (kind == Kind.OrderIntegration)
            {
                o.ShipOrder();
            }
        }
    }

    private sealed class Run : IAsyncDisposable
    {
        public required AsyncServiceScope Scope { get; init; }

        public required DbContext Db { get; init; }

        public required List<object> Tracked { get; init; }

        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }

    private static async Task<Run> _PrepareAsync(Env env, Kind kind, string workload)
    {
        var scope = env.CreateScope();
        var db = env.Resolve(scope.ServiceProvider);
        var tracked = new List<object>();
        var headless = env.Name != "plain";

        for (var i = 0; i < _Tracked; i++)
        {
            var e = _New(kind, headless);
            // The tracked graph carries no pending events: only the per-save workload raises them.
            if (e is OrderRoot o)
            {
                o.ClearDomainEvents();
                o.ClearIntegrationEvents();
            }

            db.Add(e);
            tracked.Add(e);
        }

        await db.SaveChangesAsync().ConfigureAwait(false);

        return new Run
        {
            Scope = scope,
            Db = db,
            Tracked = tracked,
        };
    }

    private static async Task _OpAsync(Run run, Env env, Kind kind, string workload, int i)
    {
        var db = run.Db;
        var headless = env.Name != "plain";

        switch (workload)
        {
            case "noop-save":
                await db.SaveChangesAsync().ConfigureAwait(false);
                break;
            case "update-1":
                var e = run.Tracked[i % run.Tracked.Count];
                _Mutate(e);
                _RaiseFor(kind, e);
                await db.SaveChangesAsync().ConfigureAwait(false);
                break;
            case "insert-1":
                db.ChangeTracker.Clear();
                db.Add(_New(kind, headless));
                await db.SaveChangesAsync().ConfigureAwait(false);
                break;
            case "insert-100":
                db.ChangeTracker.Clear();
                for (var k = 0; k < 100; k++)
                {
                    db.Add(_New(kind, headless));
                }

                await db.SaveChangesAsync().ConfigureAwait(false);
                break;
        }
    }

    private static IDisposable? _EnterTenant(Env env, Kind kind) =>
        kind == Kind.Tenant && env.Name.Contains("tenant", StringComparison.Ordinal)
            ? env.Provider.GetRequiredService<ICurrentTenant>().Change("t1")
            : null;

    private static async Task<Counts> _CountAsync(Env env, Kind kind, string workload)
    {
        // AsyncLocal tenant must be set in this frame, not inside an awaited helper, to flow to the save.
        using var tenant = _EnterTenant(env, kind);
        await using var run = await _PrepareAsync(env, kind, workload).ConfigureAwait(false);
        // warm
        for (var i = 0; i < 5; i++)
        {
            await _OpAsync(run, env, kind, workload, i).ConfigureAwait(false);
        }

        env.Counter.Reset();
        await _OpAsync(run, env, kind, workload, 7).ConfigureAwait(false);
        var c = env.Counter;
        return new Counts(c.Detect, c.Saves, c.Commands, c.TxBegin, c.TxCommit);
    }

    private static async Task<Result> _MeasureAsync(Env env, Kind kind, string workload, int iters)
    {
        using var tenant = _EnterTenant(env, kind);

        if (workload == "insert-100")
        {
            iters /= 10;
        }

        await using var run = await _PrepareAsync(env, kind, workload).ConfigureAwait(false);

        for (var i = 0; i < Math.Max(200, iters / 4); i++)
        {
            await _OpAsync(run, env, kind, workload, i).ConfigureAwait(false);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        double bestBytes = double.MaxValue;
        var times = new List<double>();
        const int batches = 5;
        var per = iters / batches;
        var ctr = 0;

        for (var b = 0; b < batches; b++)
        {
            var before = GC.GetTotalAllocatedBytes(precise: true);
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < per; i++)
            {
                await _OpAsync(run, env, kind, workload, ctr++).ConfigureAwait(false);
            }

            sw.Stop();
            var bytes = (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)per;
            bestBytes = Math.Min(bestBytes, bytes);
            times.Add(sw.Elapsed.TotalMilliseconds * 1000 / per);
        }

        times.Sort();
        return new Result(bestBytes, times[times.Count / 2], default);
    }

    private static async Task<Result> _MeasureScopeAsync(Env env, bool save, int iters)
    {
        async Task OneAsync()
        {
            await using var scope = env.CreateScope();
            var db = env.Resolve(scope.ServiceProvider);
            if (save)
            {
                db.Add(
                    new PlainRow
                    {
                        Id = Guid.NewGuid(),
                        Name = "n",
                        Amount = 1,
                    }
                );
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        for (var i = 0; i < 100; i++)
        {
            await OneAsync().ConfigureAwait(false);
        }

        GC.Collect();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iters; i++)
        {
            await OneAsync().ConfigureAwait(false);
        }

        sw.Stop();
        return new Result(
            (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)iters,
            sw.Elapsed.TotalMilliseconds * 1000 / iters,
            default
        );
    }
}

internal static class StampCheck
{
    public static async Task RunAsync()
    {
        using var env = new Env("headless-bare", typeof(BareDb), false);
        await using var scope = env.CreateScope();
        var db = env.Resolve(scope.ServiceProvider);
        var row = new StampedRow
        {
            Id = Guid.NewGuid(),
            Name = "n",
            Amount = 1,
        };
        db.Add(row);
        await db.SaveChangesAsync().ConfigureAwait(false);
        var s1 = row.ConcurrencyStamp;
        row.Amount = 2;
        await db.SaveChangesAsync().ConfigureAwait(false);
        var s2 = row.ConcurrencyStamp;
        var stored = await db.Set<StampedRow>()
            .AsNoTracking()
            .Where(x => x.Id == row.Id)
            .Select(x => x.ConcurrencyStamp)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        Console.WriteLine(
            $"stampAfterInsert={s1} stampAfterUpdate(entity)={s2} stored={stored} storedEqualsEntity={stored == s2}"
        );
    }
}

internal static class AuditRowCost
{
    public static async Task RunAsync()
    {
        using var env = new Env(
            "headless-audit",
            typeof(AuditDb),
            false,
            s => s.AddHeadlessAuditLog(x => x.UseEntityFramework<AuditDb>())
        );
        await using var scope = env.CreateScope();
        var db = env.Resolve(scope.ServiceProvider);

        static AuditLogEntry NewRow(int i) =>
            new()
            {
                CreatedAt = DateTime.UtcNow,
                UserId = "u1",
                TenantId = "t1",
                CorrelationId = "c",
                Action = "entity.created",
                ChangeType = AuditChangeType.Created,
                EntityType = "Headless.EntityFramework.Benchmarks.SaveCost.AuditedRow",
                EntityId = Guid.NewGuid().ToString(),
                NewValues = new Dictionary<string, object?>
                {
                    ["id"] = Guid.NewGuid(),
                    ["name"] = "n",
                    ["amount"] = 1m,
                    ["note"] = "x",
                },
            };

        foreach (var rows in new[] { 1, 100 })
        {
            for (var w = 0; w < 100; w++)
            {
                db.ChangeTracker.Clear();
                for (var k = 0; k < rows; k++)
                {
                    db.Add(NewRow(k));
                }
                await db.SaveChangesAsync().ConfigureAwait(false);
            }

            GC.Collect();
            var iters = rows == 1 ? 2000 : 100;
            var before = GC.GetTotalAllocatedBytes(true);
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < iters; i++)
            {
                db.ChangeTracker.Clear();
                for (var k = 0; k < rows; k++)
                {
                    db.Add(NewRow(k));
                }
                await db.SaveChangesAsync().ConfigureAwait(false);
            }

            sw.Stop();
            var b = (GC.GetTotalAllocatedBytes(true) - before) / (double)iters;
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"AuditLogEntry x{rows} saved by Headless context directly (no capture/pipeline extras beyond bare, no tx): {b:N0} B/op = {b / rows:N0} B/row, {sw.Elapsed.TotalMilliseconds * 1000 / iters:N1} us/op"
                )
            );
        }

        // The same rows written with one parameterized multi-row INSERT on the context's connection, bypassing the
        // change tracker: the alternative to adding audit rows to the saving context.
        var entityType = db.Model.FindEntityType(typeof(AuditLogEntry))!;
        var table = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
        var columns = entityType
            .GetProperties()
            .Where(p => p.ValueGenerated == ValueGenerated.Never && p.GetColumnName(table) is not null)
            .ToArray();
        var connection = db.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync().ConfigureAwait(false);
        }

        async Task WriteRawAsync(int rows)
        {
            await using var command = connection.CreateCommand();
            var sql = new System.Text.StringBuilder()
                .Append("INSERT INTO \"")
                .Append(table.Name)
                .Append("\" (")
                .AppendJoin(", ", columns.Select(c => "\"" + c.GetColumnName(table) + "\""))
                .Append(") VALUES ");

            for (var r = 0; r < rows; r++)
            {
                var row = NewRow(r);
                sql.Append(r == 0 ? "(" : ", (");

                for (var c = 0; c < columns.Length; c++)
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = string.Create(CultureInfo.InvariantCulture, $"@p{r}_{c}");
                    var value = columns[c].GetGetter().GetClrValueUsingContainingEntity(row);
                    var converter = columns[c].GetTypeMapping().Converter;
                    parameter.Value = (converter is null ? value : converter.ConvertToProvider(value)) ?? DBNull.Value;
                    command.Parameters.Add(parameter);
                    sql.Append(c == 0 ? "" : ", ").Append(parameter.ParameterName);
                }

                sql.Append(')');
            }

            command.CommandText = sql.ToString();
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        foreach (var rows in new[] { 1, 100 })
        {
            for (var w = 0; w < 100; w++)
            {
                await WriteRawAsync(rows).ConfigureAwait(false);
            }

            GC.Collect();
            var iters = rows == 1 ? 2000 : 100;
            var before = GC.GetTotalAllocatedBytes(true);
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < iters; i++)
            {
                await WriteRawAsync(rows).ConfigureAwait(false);
            }

            sw.Stop();
            var b = (GC.GetTotalAllocatedBytes(true) - before) / (double)iters;
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"AuditLogEntry x{rows} written by one raw multi-row INSERT (1 command): {b:N0} B/op = {b / rows:N0} B/row, {sw.Elapsed.TotalMilliseconds * 1000 / iters:N1} us/op"
                )
            );
        }
    }
}
