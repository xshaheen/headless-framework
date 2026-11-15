// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using BenchmarkDotNet.Attributes;
using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Headless.Sequences.Benchmarks;

/// <summary>
/// Fast-mode <see cref="ISequenceGenerator.NextAsync" /> at 32 concurrent callers, all on one counter (they queue on
/// its row) or each on its own partition (they never contend). One operation is one number; Op/s is numbers per second
/// across all callers.
/// </summary>
public class NextBenchmarks
{
    private const int _Callers = 32;
    private const int _CallsPerCaller = 64;
    private const int _Operations = _Callers * _CallsPerCaller;
    private const string _Name = "bench";

    private static readonly string[] _Partitions = Enumerable
        .Range(0, _Callers)
        .Select(static i => string.Create(CultureInfo.InvariantCulture, $"p{i:D2}"))
        .ToArray();

    private BenchmarkDatabase _database = null!;
    private ServiceProvider _services = null!;
    private ISequenceGenerator _generator = null!;

    [Params(SequencesProvider.PostgreSql, SequencesProvider.SqlServer)]
    public SequencesProvider Provider { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _database = await BenchmarkDatabase.CreateAsync(Provider);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessSequences(setup =>
        {
            if (Provider == SequencesProvider.PostgreSql)
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

        _generator = _services.GetRequiredService<ISequenceGenerator>();

        // Create every row up front, so the measured calls take the update branch, as a warm counter does.
        await OneKey();
        await DistinctKeys();
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = _Operations, Description = "NextAsync, one key")]
    public Task OneKey()
    {
        return _RunCallersAsync(_ => _generator.NextAsync(_Name));
    }

    [Benchmark(OperationsPerInvoke = _Operations, Description = "NextAsync, a key per caller")]
    public Task DistinctKeys()
    {
        return _RunCallersAsync(caller => _generator.NextAsync(_Name, _Partitions[caller]));
    }

    private static Task _RunCallersAsync(Func<int, ValueTask<long>> next)
    {
        var callers = new Task[_Callers];

        for (var i = 0; i < _Callers; i++)
        {
            var caller = i;
            callers[i] = Task.Run(async () =>
            {
                for (var n = 0; n < _CallsPerCaller; n++)
                {
                    if (await next(caller) <= 0)
                    {
                        throw new InvalidOperationException("A counter returned a non-positive value.");
                    }
                }
            });
        }

        return Task.WhenAll(callers);
    }
}
