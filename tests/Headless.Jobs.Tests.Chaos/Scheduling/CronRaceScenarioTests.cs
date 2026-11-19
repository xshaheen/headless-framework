// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.Scheduling;

/// <summary>
/// S4, the cron race with skew: several Jobs nodes share one store, each on a clock skewed by a seeded offset of up to
/// five minutes, all running the real scheduler, and one recurring definition fires every five seconds for the soak.
/// Each instant the window owes must fire exactly once across the cluster.
/// </summary>
/// <remarks>
/// <see cref="CronInstantVerdict"/> says which instants are owed and why. The skew is the point: dispatch must decide
/// due-ness on the store clock ("Due times follow the store clock"), so a node minutes ahead must not fire an instant
/// early or twice, and a node minutes behind must not miss one.
/// </remarks>
public abstract class CronRaceScenarioTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IStoreChaosFixture
{
    private const string _Scenario = "S4 cron race with skew";
    private const string _Expression = "*/5 * * * * *";
    private static readonly TimeSpan _Cadence = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _MaxSkew = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _SettlePoll = TimeSpan.FromMilliseconds(250);
    private const int _SettlePolls = 240;

    public virtual async Task should_fire_every_owed_instant_exactly_once_when_skewed_nodes_race_one_definition()
    {
        var ct = AbortToken;
        var settings = ChaosRunSettings.Current;
        var seed = settings.SeedFor(_Scenario);
        var random = new Random(seed);
        var nodeCount = settings.Pick(3, 5);
        var soak = settings.SoakDuration(TimeSpan.FromSeconds(90), TimeSpan.FromMinutes(15));
        var skews = Enumerable
            .Range(0, nodeCount)
            .Select(_ => TimeSpan.FromSeconds(random.Next(-(int)_MaxSkew.TotalSeconds, (int)_MaxSkew.TotalSeconds + 1)))
            .ToArray();
        var output = TestContext.Current.TestOutputHelper;
        output?.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{_Scenario}: {settings}, scenario seed {seed}, {nodeCount} nodes, soak {soak}, skews [{string.Join(", ", skews.Select(x => x.TotalSeconds))}] s"
            )
        );

        await fixture.ResetDatabaseAsync(ct);
        var log = new CronFireLog();
        var hosts = Enumerable
            .Range(0, nodeCount)
            .Select(i =>
            {
                var node = string.Create(CultureInfo.InvariantCulture, $"node-{i}({skews[i].TotalSeconds:+0;-0;0}s)");

                return fixture.BuildHost(
                    $"cron-race-{i}",
                    timeProvider: new SkewedTimeProvider(skews[i]),
                    configureJobs: jobs => jobs.AddModule<SchedulingChaosJobsModule>(),
                    runBackgroundServices: true,
                    configureServices: services =>
                    {
                        services.AddSingleton(log);
                        services.AddSingleton(new ChaosNodeName(node));
                        services.AddSingleton(new StoreClock(_StoreUtcNowAsync));
                    }
                );
            })
            .ToList();

        try
        {
            await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(hosts[0], ct);

            // The definition exists before any node starts, so every node's first poll finds it and every node races
            // every wake. Created after the starts, only the creating node would be told to wake, while the others
            // slept towards the next day and left it every instant.
            var definition = new CronJobEntity
            {
                Id = Guid.NewGuid(),
                Function = SchedulingChaosJobsModule.Function(0),
                Description = _Scenario,
                Expression = _Expression,
                Request = [],
            };
            await hosts[0].Services.GetRequiredService<ICronJobManager<CronJobEntity>>().AddAsync(definition, ct);
            var anchor = (await fixture.ReadCronSchedulePositionAsync(definition.Id, ct)).ReconciledThroughUtc;

            foreach (var host in hosts)
            {
                await host.StartAsync(ct);
            }

            foreach (var host in hosts)
            {
                await JobsCoordinationFixtureExtensions.WaitForStampOwnerAsync(host, ct);
            }

            // real-wait: the soak is the scenario; the schedule runs on the database clock, which no fake moves
            await Task.Delay(soak, ct);

            var windowEnd = await _StoreUtcNowAsync(ct);
            var stuck = await _SettleAsync(hosts[0], definition.Id, windowEnd, ct);

            foreach (var host in hosts)
            {
                await host.StopAsync(ct);
            }

            var fires = log.Snapshot();
            var owed = _Owed(anchor, windowEnd);
            var verdict = CronInstantVerdict.Evaluate(owed, _IsOnSchedule, fires);
            List<string> violations = [.. verdict.Violations()];

            if (stuck is not null)
            {
                violations.Add($"the cluster did not settle the window's last instants: {stuck}");
            }

            var perNode = string.Join(
                ", ",
                fires
                    .GroupBy(x => x.Node)
                    .OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => $"{x.Key}={x.Count()}")
            );
            ChaosReport.Record(
                fixture.Store,
                _Scenario,
                violations,
                $"{verdict.Describe()}; window ({anchor:O}, {windowEnd:O}]; per node {perNode}",
                verdict.Evidence(fires),
                line => output?.WriteLine(line)
            );

            // The oracle must also catch a duplicate and a lost instant on this run's own log, not only on a fake one.
            _AssertOracleCatchesSabotage(owed, fires);

            violations.Should().BeEmpty();
        }
        finally
        {
            foreach (var host in hosts)
            {
                host.Dispose();
            }
        }
    }

    private static void _AssertOracleCatchesSabotage(IReadOnlyList<DateTime> owed, IReadOnlyList<CronFire> fires)
    {
        var ordinary = fires.Where(x => !x.IsRecoveryRun).OrderBy(x => x.ScheduledFor).ToList();

        if (ordinary.Count < 3)
        {
            return;
        }

        var duplicated = ordinary[1];

        // The run before the dropped one must be a regular run: a recovery run that fired after the dropped instant
        // legitimately stands in for it, so dropping that instant would prove nothing about the oracle.
        var all = fires.OrderBy(x => x.ScheduledFor).ToList();
        var dropped = Enumerable
            .Range(1, all.Count - 1)
            .Reverse()
            .Select(i => (Fire: all[i], Previous: all[i - 1]))
            .Where(pair =>
                !pair.Fire.IsRecoveryRun
                && !pair.Previous.IsRecoveryRun
                && pair.Fire.ScheduledFor != duplicated.ScheduledFor
                && owed.Contains(pair.Fire.ScheduledFor)
            )
            .Select(pair => pair.Fire)
            .FirstOrDefault();
        List<CronFire> sabotaged =
        [
            .. fires.Where(x => dropped is null || x.ScheduledFor != dropped.ScheduledFor),
            duplicated with
            {
                Node = "sabotage",
            },
        ];

        var verdict = CronInstantVerdict.Evaluate(owed, _IsOnSchedule, sabotaged);
        verdict.Duplicated.Should().Contain(duplicated.ScheduledFor);

        dropped.Should().NotBeNull("a soak this long has two regular runs in a row inside the window");
        verdict.Missing.Should().Contain(dropped!.ScheduledFor);
    }

    private static bool _IsOnSchedule(DateTime instant) => instant.Ticks % _Cadence.Ticks == 0;

    private static List<DateTime> _Owed(DateTime anchor, DateTime windowEnd)
    {
        List<DateTime> owed = [];
        var first = new DateTime(((anchor.Ticks / _Cadence.Ticks) + 1) * _Cadence.Ticks, DateTimeKind.Utc);

        for (var instant = first; instant <= windowEnd; instant += _Cadence)
        {
            owed.Add(instant);
        }

        return owed;
    }

    // Null once the definition's projection is past the window end and every occurrence up to it has finished.
    private static async Task<string?> _SettleAsync(IHost host, Guid id, DateTime windowEnd, CancellationToken ct)
    {
        var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var pending = string.Empty;

        for (var poll = 0; poll < _SettlePolls; poll++)
        {
            var definition = await store.GetCronJobByIdAsync(id, ct);
            var unfinished = (
                await store.GetAllCronJobOccurrencesAsync(x => x.CronJobId == id && x.ExecutionTime <= windowEnd, ct)
            ).Count(x => x.Status is JobStatus.Idle or JobStatus.Queued or JobStatus.InProgress);

            if (definition is not null && definition.NextDueUtc > windowEnd && unfinished == 0)
            {
                return null;
            }

            pending = string.Create(
                CultureInfo.InvariantCulture,
                $"next due {definition?.NextDueUtc:O}, {unfinished} unfinished occurrence(s)"
            );
            // real-wait: the nodes dispatch on the database clock; the store is the only thing that shows they caught up
            await Task.Delay(_SettlePoll, ct);
        }

        return pending;
    }

    private async Task<DateTime> _StoreUtcNowAsync(CancellationToken ct)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {fixture.UtcNowSqlExpression}";
        var value = await command.ExecuteScalarAsync(ct);

        return value switch
        {
            DateTimeOffset offset => offset.UtcDateTime,
            DateTime dateTime => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
            _ => throw new InvalidOperationException($"The store clock read returned {value?.GetType().Name}."),
        };
    }
}

[Collection(StoreChaosPostgreSqlCollection.Name)]
public sealed class PostgreSqlCronRaceTests(StoreChaosPostgreSqlFixture fixture)
    : CronRaceScenarioTests<StoreChaosPostgreSqlFixture>(fixture)
{
    [Fact]
    public override Task should_fire_every_owed_instant_exactly_once_when_skewed_nodes_race_one_definition() =>
        base.should_fire_every_owed_instant_exactly_once_when_skewed_nodes_race_one_definition();
}

[Collection(StoreChaosSqlServerCollection.Name)]
public sealed class SqlServerCronRaceTests(StoreChaosSqlServerFixture fixture)
    : CronRaceScenarioTests<StoreChaosSqlServerFixture>(fixture)
{
    [Fact]
    public override Task should_fire_every_owed_instant_exactly_once_when_skewed_nodes_race_one_definition() =>
        base.should_fire_every_owed_instant_exactly_once_when_skewed_nodes_race_one_definition();
}

/// <summary>A node clock offset from the system clock; timers and timestamps stay on the system clock.</summary>
public sealed class SkewedTimeProvider(TimeSpan offset) : TimeProvider
{
    // wall-clock: the skew is an offset from real time, because the store's clock, which the nodes are skewed against, is real
    public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow() + offset;
}
