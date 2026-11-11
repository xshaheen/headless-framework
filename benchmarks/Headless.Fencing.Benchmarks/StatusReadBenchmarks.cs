// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using Headless.Hosting;
using Headless.Sql;
using Headless.Sql.PostgreSql;
using Headless.Sql.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Headless.Fencing.Benchmarks;

/// <summary>
/// The per-request liveness check at 32 concurrent callers: <see cref="IFencedLeases.GetStatusAsync(FencedLease, CancellationToken)" />
/// on its own pooled connection, the same read on each caller's open connection, and
/// <see cref="IFencedLeases.GetCurrentAsync(string, string, CancellationToken)" />, against a plain primary-key
/// <c>SELECT</c> of the same row on the caller's connection. The table holds 256 live leases and each caller reads its
/// own, so callers never contend on one row. One operation is one read; Op/s is calls per second across all callers.
/// </summary>
public class StatusReadBenchmarks
{
    private const int _Callers = 32;
    private const int _ReadsPerCaller = 64;
    private const int _Operations = _Callers * _ReadsPerCaller;
    private const int _Rows = 256;
    private const string _Kind = "bench";

    private BenchmarkDatabase _database = null!;
    private ServiceProvider _services = null!;
    private IFencedLeases _leases = null!;
    private ISqlDialect _dialect = null!;
    private string _baselineSql = null!;
    private FencedLease[] _callerLeases = null!;
    private DbConnection[] _callerConnections = null!;

    [Params(FencingProvider.PostgreSql, FencingProvider.SqlServer)]
    public FencingProvider Provider { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _database = await BenchmarkDatabase.CreateAsync(Provider);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessFencing(setup =>
        {
            if (Provider == FencingProvider.PostgreSql)
            {
                setup.UsePostgreSql(_database.ConnectionString);
            }
            else
            {
                setup.UseSqlServer(_database.ConnectionString);
            }
        });

        // A host start only verifies the schema, so the benchmark creates it first, as a development host does.
        services.AddDeployStepsOnStartup(o => o.Seed = false);
        _services = services.BuildServiceProvider();

        foreach (var initializer in _services.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await initializer.StartingAsync(CancellationToken.None);
        }

        _leases = _services.GetRequiredService<IFencedLeases>();
        var leases = new FencedLease[_Rows];

        for (var i = 0; i < _Rows; i++)
        {
            var granted = await _leases.GrantAsync(_Kind, _Resource(i), TimeSpan.FromHours(2));
            leases[i] =
                granted.Lease
                ?? throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"Lease {i} was not granted.")
                );
        }

        _callerLeases = leases[.._Callers];
        _callerConnections = new DbConnection[_Callers];

        for (var i = 0; i < _Callers; i++)
        {
            _callerConnections[i] = _database.CreateConnection();
            await _callerConnections[i].OpenAsync();
        }

        // The same row by the same primary key, bound with the same parameter types the lease store binds, without the
        // database clock or the state the status read evaluates.
        (_dialect, _baselineSql) =
            Provider == FencingProvider.PostgreSql
                ? (
                    (ISqlDialect)PostgreSqlDialect.Instance,
                    """SELECT generation FROM "headless"."fencing_leases" WHERE tenant_id = @TenantId AND kind = @Kind AND resource = @Resource;"""
                )
                : (
                    SqlServerDialect.Instance,
                    "SELECT [Generation] FROM [headless].[FencingLeases] WHERE [TenantId] = @TenantId AND [Kind] = @Kind AND [Resource] = @Resource;"
                );

        // Every variant must read a live row, or the run would measure a short-circuit instead of a round trip.
        await PrimaryKeySelect();
        await PrimaryKeySelectOnOwnConnection();
        await StatusOnOwnConnection();
        await StatusOnCallerConnection();
        await CurrentOnOwnConnection();
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        foreach (var connection in _callerConnections ?? [])
        {
            await connection.DisposeAsync();
        }

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    [Benchmark(
        Baseline = true,
        OperationsPerInvoke = _Operations,
        Description = "Primary-key SELECT, caller's connection"
    )]
    public Task PrimaryKeySelect()
    {
        return _RunCallersAsync(caller => _SelectByPrimaryKeyAsync(_callerConnections[caller], _callerLeases[caller]));
    }

    // Separates the cost of checking a pooled connection out per read, which the own-connection reads pay, from the
    // framework's own work on top of it.
    [Benchmark(OperationsPerInvoke = _Operations, Description = "Primary-key SELECT, own pooled connection")]
    public Task PrimaryKeySelectOnOwnConnection()
    {
        return _RunCallersAsync(async caller =>
        {
            await using var connection = _database.CreateConnection();
            await connection.OpenAsync();
            await _SelectByPrimaryKeyAsync(connection, _callerLeases[caller]);
        });
    }

    private async Task _SelectByPrimaryKeyAsync(DbConnection connection, FencedLease lease)
    {
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // A constant statement chosen per provider; the key travels as parameters.
        command.CommandText = _baselineSql;
#pragma warning restore CA2100
        _dialect.AddParameter(
            command,
            "TenantId",
            SqlColumnType.KeyText(FencingFieldLimits.TenantIdMaxLength),
            lease.TenantId ?? ""
        );
        _dialect.AddParameter(command, "Kind", SqlColumnType.KeyText(FencingFieldLimits.KindMaxLength), lease.Kind);
        _dialect.AddParameter(
            command,
            "Resource",
            SqlColumnType.KeyText(FencingFieldLimits.ResourceMaxLength),
            lease.Resource
        );

        if (await command.ExecuteScalarAsync() is not long generation || generation != lease.Generation)
        {
            throw new InvalidOperationException("The baseline read did not find the lease row.");
        }
    }

    [Benchmark(OperationsPerInvoke = _Operations, Description = "GetStatusAsync(lease)")]
    public Task StatusOnOwnConnection()
    {
        return _RunCallersAsync(async caller => _ExpectCurrent(await _leases.GetStatusAsync(_callerLeases[caller])));
    }

    [Benchmark(OperationsPerInvoke = _Operations, Description = "GetStatusAsync(lease, connection)")]
    public Task StatusOnCallerConnection()
    {
        return _RunCallersAsync(async caller =>
            _ExpectCurrent(await _leases.GetStatusAsync(_callerLeases[caller], _callerConnections[caller]))
        );
    }

    [Benchmark(OperationsPerInvoke = _Operations, Description = "GetCurrentAsync(kind, resource)")]
    public Task CurrentOnOwnConnection()
    {
        return _RunCallersAsync(async caller =>
        {
            var lease = _callerLeases[caller];
            var current = await _leases.GetCurrentAsync(lease.Kind, lease.Resource);
            _ExpectCurrent(current?.Status ?? LeaseFenceStatus.Stale);
        });
    }

    private static Task _RunCallersAsync(Func<int, Task> read)
    {
        var callers = new Task[_Callers];

        for (var i = 0; i < _Callers; i++)
        {
            var caller = i;
            callers[i] = Task.Run(async () =>
            {
                for (var n = 0; n < _ReadsPerCaller; n++)
                {
                    await read(caller);
                }
            });
        }

        return Task.WhenAll(callers);
    }

    private static void _ExpectCurrent(LeaseFenceStatus status)
    {
        if (status != LeaseFenceStatus.Current)
        {
            throw new InvalidOperationException($"Expected a live lease, read {status}.");
        }
    }

    private static string _Resource(int index) => string.Create(CultureInfo.InvariantCulture, $"resource-{index:D3}");
}
