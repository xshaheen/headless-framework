// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Hosting.Schema;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

#pragma warning disable CA1303, MA0076 // Console report of a standalone scenario: not localized, and the invariant-culture numbers are only read by a person.

namespace Headless.Messaging.Benchmarks.Scenarios;

/// <summary>
/// Measures an EF Core save that carries integration events into the enlisted outbox, on PostgreSQL and SQL Server:
/// the save's latency and the database round trips it makes, for 1, 10 and 100 events per save. The messaging host
/// is not started, so no relay competes with the saves for the database. Needs Docker:
/// <c>dotnet run -c Release --project benchmarks/Headless.Messaging.Benchmarks -- outbox-save</c>.
/// </summary>
internal static class OutboxSaveScenario
{
    private const string _PostgreSqlImage = "postgres:18.1-alpine3.23";

    // SQL Server ships no ARM image, so an ARM host runs it under emulation: compare its rows with each other, not
    // with PostgreSQL's.
    private const string _SqlServerImage = "mcr.microsoft.com/mssql/server:2022-CU19-ubuntu-22.04";

    private static readonly int[] _EventsPerSave = [1, 10, 100];

    public static async Task RunAsync()
    {
        Console.WriteLine(
            $"{"provider", -11} {"events", 6} {"saves", 6} {"mean ms", 8} {"p50 ms", 8} {"p95 ms", 8} {"trips/save", 11}"
        );

        await _RunPostgreSqlAsync();
        await _RunSqlServerAsync();
    }

    private static async Task _RunPostgreSqlAsync()
    {
        await using var postgreSql = new PostgreSqlBuilder(_PostgreSqlImage).Build();
        await postgreSql.StartAsync();
        await _RunProviderAsync("PostgreSQL", postgreSql.GetConnectionString(), Provider.PostgreSql);
    }

    private static async Task _RunSqlServerAsync()
    {
        await using var sqlServer = new MsSqlBuilder(_SqlServerImage).Build();
        await sqlServer.StartAsync();
        var connectionString = new SqlConnectionStringBuilder(sqlServer.GetConnectionString())
        {
            TrustServerCertificate = true,
        }.ConnectionString;
        await _RunProviderAsync("SQL Server", connectionString, Provider.SqlServer);
    }

    private enum Provider
    {
        PostgreSql,
        SqlServer,
    }

    private static async Task _RunProviderAsync(string label, string connectionString, Provider provider)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Error);
        var services = builder.Services;
        services.AddHeadlessDbContextServices().AddDomainEvents().AddIntegrationEventOutbox();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            if (provider is Provider.PostgreSql)
            {
                setup.UsePostgreSql(connectionString);
            }
            else
            {
                setup.UseSqlServer(connectionString);
            }
        });
        services.ConfigureMessaging(messaging => messaging.Message<OrderPlaced>("orders.placed", "1"));
        services.AddDbContext<SaveBenchmarkDbContext>(options =>
        {
            if (provider is Provider.PostgreSql)
            {
                options.UseNpgsql(connectionString);
            }
            else
            {
                options.UseSqlServer(connectionString);
            }
        });

        using var host = builder.Build();
        await host.Services.GetRequiredService<SchemaRunner>().ApplyAsync();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SaveBenchmarkDbContext>();
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        }

        using var trips = new RoundTripCounter();
        foreach (var events in _EventsPerSave)
        {
            var saves = events >= 100 ? 100 : 300;
            await _SaveAsync(host.Services, events, saves: 20); // warm-up: pools, plans, JIT

            trips.Reset();
            var latencies = new double[saves];
            for (var i = 0; i < saves; i++)
            {
                var started = Stopwatch.GetTimestamp();
                await _SaveAsync(host.Services, events, saves: 1);
                latencies[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }

            Array.Sort(latencies);
            Console.WriteLine(
                $"{label, -11} {events, 6} {saves, 6} {latencies.Average(), 8:F2} {latencies[saves / 2], 8:F2} "
                    + $"{latencies[(int)(saves * 0.95)], 8:F2} {(double)trips.Count / saves, 11:F1}"
            );
        }
    }

    private static async Task _SaveAsync(IServiceProvider services, int events, int saves)
    {
        for (var save = 0; save < saves; save++)
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SaveBenchmarkDbContext>();
            var order = new Order { Name = "order" };
            for (var i = 0; i < events; i++)
            {
                order.Place(new OrderPlaced(order.Id, i, new string('x', 256)));
            }

            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Counts the commands the client sends: Npgsql reports each command or batch as one activity, and SqlClient
    /// writes one diagnostic event before each command or batch it executes.
    /// </summary>
    private sealed class RoundTripCounter
        : IDisposable,
            IObserver<DiagnosticListener>,
            IObserver<KeyValuePair<string, object?>>
    {
        private readonly ActivityListener _npgsql;
        private readonly IDisposable _listeners;
        private readonly List<IDisposable> _subscriptions = [];
        private int _count;

        public RoundTripCounter()
        {
            _npgsql = new ActivityListener
            {
                ShouldListenTo = source => string.Equals(source.Name, "Npgsql", StringComparison.Ordinal),
                Sample = static (ref _) => ActivitySamplingResult.AllData,
                ActivityStarted = _ => Interlocked.Increment(ref _count),
            };
            ActivitySource.AddActivityListener(_npgsql);
            _listeners = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public int Count => Volatile.Read(ref _count);

        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public void OnNext(DiagnosticListener value)
        {
            if (string.Equals(value.Name, "SqlClientDiagnosticListener", StringComparison.Ordinal))
            {
                _subscriptions.Add(value.Subscribe(this));
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _count);
            }
        }

        public void OnCompleted() { }

        public void OnError(Exception error) { }

        public void Dispose()
        {
            _npgsql.Dispose();
            _listeners.Dispose();
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }
        }
    }

    internal sealed record OrderPlaced(Guid OrderId, int Line, string Body);

    internal sealed class Order : AggregateRoot, IEntity<Guid>
    {
        public Guid Id { get; private init; } = Guid.NewGuid();

        public required string Name { get; init; }

        public void Place(OrderPlaced integrationEvent) => AddIntegrationEvent(integrationEvent);

        public override IReadOnlyList<object> GetKeys() => [Id];
    }

    internal sealed class SaveBenchmarkDbContext(DbContextOptions<SaveBenchmarkDbContext> options)
        : HeadlessDbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        public override string DefaultSchema => "";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Order>().Property(e => e.Id).ValueGeneratedNever();
        }
    }
}
