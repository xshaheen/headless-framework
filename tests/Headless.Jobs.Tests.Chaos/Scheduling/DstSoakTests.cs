// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Scheduling;

/// <summary>
/// S5, the DST soak: the real scheduler, background services and all, runs recurring definitions through both DST
/// transitions of four zones, and the instants each definition actually fired must equal its golden list.
/// </summary>
/// <remarks>
/// <para>
/// A relational store's scheduler follows the database's <c>now()</c>, which no test can move to a transition date, so
/// this soak runs on the in-memory store, whose clock is the host's <see cref="TimeProvider"/>. The clock is a
/// <see cref="FakeTimeProvider"/> stepped onto every five-minute mark of the window, and every golden instant sits on
/// such a mark, so each instant is reached on time: none can be late enough to fall into misfire recovery, and a
/// recovery run therefore counts as a violation. After each step the scenario waits until every definition's
/// projection has moved past the step and every occurrence up to it has finished, so a run the scheduler should not
/// have made is recorded before the next step, and a run it should have made but did not is seen as missing. While it
/// waits it nudges the clock forward in 50 ms ticks, because the scheduler's own short waits (the restart debounce, the
/// pause after a restart) run on the same clock and would otherwise never end; the nudges stay far below the 60-second
/// misfire grace and the five minutes between steps.
/// </para>
/// <para>
/// The golden lists are derived by hand from the zones' offsets and the rules in <c>docs/llms/jobs.md</c> "Cron across
/// DST transitions" (gap: shift forward by the gap and merge; overlap: the first pass always fires, the second only when
/// the cadence continues next to the repeated window). Most are the lists <c>CronScheduleCacheDstTests</c> pins on the
/// schedule cache; here they are pinned on what the scheduler dispatches. The scheduler resolves a definition's zone by
/// IANA identifier, so this soak runs on the host's tzdata, not on the synthetic zones the cache tests use; each case
/// first asserts that the host's tzdata places the transition where its golden list assumes.
/// </para>
/// </remarks>
public sealed class DstSoakTests : TestBase
{
    private static readonly TimeSpan _Step = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _Quarter = TimeSpan.FromMinutes(15);

    // Per step: how many times the scenario polls the store for the scheduler to settle, how long it waits between,
    // and how far it moves the clock on each poll. 1200 nudges of 50 ms bound the drift at one minute.
    private const int _SettlePolls = 1200;
    private static readonly TimeSpan _SettlePoll = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan _Nudge = TimeSpan.FromMilliseconds(50);

    public static TheoryData<string> Transitions() => [.. _Cases().Select(x => x.Name)];

    [Theory]
    [MemberData(nameof(Transitions))]
    public async Task should_fire_every_golden_instant_once_when_the_scheduler_crosses_a_dst_transition(string name)
    {
        var transition = _Cases().Single(x => string.Equals(x.Name, name, StringComparison.Ordinal));
        var result = await _RunAsync(transition, sabotage: null);

        ChaosReport.Record(
            "in-memory",
            $"S5 DST soak · {transition.Name}",
            result.Violations,
            result.Counts,
            result.Evidence,
            line => TestContext.Current.TestOutputHelper?.WriteLine(line)
        );

        result.Violations.Should().BeEmpty();
    }

    /// <summary>
    /// The negative control: a definition the soak creates beside the golden ones but leaves out of the expectations, as
    /// a duplicated or extra run would look, must fail the oracle on the real scheduler's output, not only on a fake log.
    /// </summary>
    [Fact]
    public async Task should_catch_runs_the_golden_list_does_not_hold_when_a_definition_is_sabotaged()
    {
        var transition = _Cases()
            .Single(x => string.Equals(x.Name, "America/New_York fall-back", StringComparison.Ordinal));
        var result = await _RunAsync(transition, sabotage: "0 30 1 * * *");

        result.Violations.Should().ContainSingle().Which.Should().Contain("no golden list");
    }

    private async Task<SoakResult> _RunAsync(DstTransition transition, string? sabotage)
    {
        var ct = AbortToken;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(transition.ZoneId);
        var because = $"the host's tzdata must place {transition.ZoneId}'s transition at {transition.TransitionUtc:O}";
        zone.GetUtcOffset(transition.TransitionUtc.AddSeconds(-1)).Should().Be(transition.OffsetBefore, because);
        zone.GetUtcOffset(transition.TransitionUtc).Should().Be(transition.OffsetAfter, because);

        var clock = new FakeTimeProvider(new DateTimeOffset(transition.WindowStart.AddSeconds(-30)));
        var log = new CronFireLog();
        using var host = _BuildHost(clock, log);
        await host.StartAsync(ct);

        try
        {
            await _WaitForActivationAsync(host, ct);
            var manager = host.Services.GetRequiredService<ICronJobManager<CronJobEntity>>();
            var store = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var expected = new Dictionary<string, IReadOnlyList<DateTime>>(StringComparer.Ordinal);
            var definitions = transition.Definitions.Select(x => (x.Expression, (IReadOnlyList<DateTime>?)x.Golden));

            if (sabotage is not null)
            {
                definitions = definitions.Append((sabotage, null));
            }

            var ids = new List<Guid>();
            var index = 0;

            foreach (var (expression, golden) in definitions)
            {
                var function = SchedulingChaosJobsModule.Function(index++);
                var definition = new CronJobEntity
                {
                    Id = Guid.NewGuid(),
                    Function = function,
                    Description = expression,
                    Expression = expression,
                    TimeZoneId = transition.ZoneId,
                    Request = [],
                };
                await manager.AddAsync(definition, ct);
                ids.Add(definition.Id);

                if (golden is not null)
                {
                    expected[function] = golden;
                }
            }

            List<string> violations = [];
            var steps = 0;

            for (var target = transition.WindowStart; target <= transition.WindowEnd; target += _Step)
            {
                clock.SetUtcNow(new DateTimeOffset(target));
                steps++;

                if (await _SettleAsync(clock, store, ids, target, ct) is { } stuck)
                {
                    violations.Add($"{transition.Name}: the scheduler did not settle at {target:O}: {stuck}");
                    break;
                }
            }

            var fires = log.Snapshot();
            violations.AddRange(FireListOracle.Compare(transition.Name, expected, fires));
            var counts = string.Create(
                CultureInfo.InvariantCulture,
                $"definitions {ids.Count}, golden instants {expected.Values.Sum(x => x.Count)}, fires {fires.Count}, steps {steps}"
            );
            var evidence = string.Join(
                '\n',
                fires
                    .OrderBy(fire => fire.Function, StringComparer.Ordinal)
                    .ThenBy(fire => fire.ScheduledFor)
                    .Select(fire =>
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{fire.Function} {fire.ScheduledFor:O} recovery={fire.IsRecoveryRun}"
                        )
                    )
            );

            return new SoakResult(violations, counts, evidence);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // Null once every definition's projection is past the step and every occurrence up to it finished; otherwise what
    // was still pending when the polls ran out.
    private static async Task<string?> _SettleAsync(
        FakeTimeProvider clock,
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        List<Guid> ids,
        DateTime target,
        CancellationToken ct
    )
    {
        string pending = string.Empty;

        for (var poll = 0; poll < _SettlePolls; poll++)
        {
            var behind = (await store.GetCronJobsAsync(x => ids.Contains(x.Id), ct))
                .Where(x => x.NextDueUtc <= target)
                .Select(x => x.Expression)
                .ToList();
            var unfinished = (
                await store.GetAllCronJobOccurrencesAsync(
                    x => ids.Contains(x.CronJobId) && x.ExecutionTime <= target,
                    ct
                )
            )
                .Where(x => x.Status is JobStatus.Idle or JobStatus.Queued or JobStatus.InProgress)
                .Select(x =>
                    string.Create(CultureInfo.InvariantCulture, $"{x.Function}@{x.ExecutionTime:O}={x.Status}")
                )
                .ToList();

            if (behind.Count == 0 && unfinished.Count == 0)
            {
                return null;
            }

            pending = $"behind [{string.Join(", ", behind)}], unfinished [{string.Join(", ", unfinished)}]";
            clock.Advance(_Nudge);
            // real-wait: the scheduler runs on the thread pool after the fake clock fires its timer, and nothing signals
            // when it has dispatched and finished; the store is the only observable, so the scenario polls it
            await Task.Delay(_SettlePoll, ct);
        }

        return pending;
    }

    private static async Task _WaitForActivationAsync(IHost host, CancellationToken ct)
    {
        var health = host.Services.GetRequiredService<HealthCheckService>();

        for (var poll = 0; poll < _SettlePolls; poll++)
        {
            var report = await health.CheckHealthAsync(
                x => string.Equals(x.Name, "jobs", StringComparison.Ordinal),
                ct
            );

            if (report.Status == HealthStatus.Healthy)
            {
                return;
            }

            // real-wait: activation runs on the host's background warm-up, which exposes only its readiness check
            await Task.Delay(_SettlePoll, ct);
        }

        throw new InvalidOperationException("The Jobs host never became ready.");
    }

    private static IHost _BuildHost(TimeProvider clock, CronFireLog log) =>
        new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(clock);
                services.AddSingleton(log);
                services.AddHeadlessJobs(options =>
                    options
                        .AddModule<SchedulingChaosJobsModule>()
                        .ConfigureScheduler(scheduler => scheduler.ShutdownTimeout = TimeSpan.FromSeconds(2))
                );
            })
            .Build();

    #region Golden lists

    private static IEnumerable<DstTransition> _Cases()
    {
        // Cairo falls back at 21:00Z on 29 October 2026: 24:00 (+03:00) becomes 23:00 (+02:00).
        yield return new DstTransition(
            "Africa/Cairo fall-back",
            "Africa/Cairo",
            _Utc(2026, 10, 29, 21, 0),
            TimeSpan.FromHours(3),
            TimeSpan.FromHours(2),
            _Utc(2026, 10, 29, 18, 0),
            _Utc(2026, 10, 29, 23, 0),
            [
                // Every 15 real minutes through both passes of the repeated hour.
                new("0 */15 * * * *", _Every(_Utc(2026, 10, 29, 18, 0), _Utc(2026, 10, 29, 23, 0), _Quarter)),
                // Pinned to the repeated hour: the cadence stops at 24:00, so only the first pass fires.
                new(
                    "0 */15 23 * * *",
                    [
                        _Utc(2026, 10, 29, 20, 0),
                        _Utc(2026, 10, 29, 20, 15),
                        _Utc(2026, 10, 29, 20, 30),
                        _Utc(2026, 10, 29, 20, 45),
                    ]
                ),
                // A fixed time in the overlap fires once, on its first pass.
                new("0 30 23 * * *", [_Utc(2026, 10, 29, 20, 30)]),
                // Hourly 22:00 and 23:00: 23:00 repeats and the hourly cadence continues into it, so it fires twice.
                new(
                    "0 0 22-23 * * *",
                    [_Utc(2026, 10, 29, 19, 0), _Utc(2026, 10, 29, 20, 0), _Utc(2026, 10, 29, 21, 0)]
                ),
            ]
        );

        // Cairo springs forward at 22:00Z on 23 April 2026: 00:00 (+02:00) becomes 01:00 (+03:00).
        yield return new DstTransition(
            "Africa/Cairo spring-forward",
            "Africa/Cairo",
            _Utc(2026, 4, 23, 22, 0),
            TimeSpan.FromHours(2),
            TimeSpan.FromHours(3),
            _Utc(2026, 4, 23, 21, 0),
            _Utc(2026, 4, 23, 23, 0),
            [
                // Shifted occurrences merge with the ones already at their instant: no instant twice, none lost.
                new("0 */15 * * * *", _Every(_Utc(2026, 4, 23, 21, 0), _Utc(2026, 4, 23, 23, 0), _Quarter)),
                // Every occurrence of the missing hour fires shifted forward by the hour.
                new(
                    "0 */15 0 * * *",
                    [
                        _Utc(2026, 4, 23, 22, 0),
                        _Utc(2026, 4, 23, 22, 15),
                        _Utc(2026, 4, 23, 22, 30),
                        _Utc(2026, 4, 23, 22, 45),
                    ]
                ),
                new("0 30 0 * * *", [_Utc(2026, 4, 23, 22, 30)]),
                new("0 0 0 * * *", [_Utc(2026, 4, 23, 22, 0)]),
            ]
        );

        // New York falls back at 06:00Z on 1 November 2026: 02:00 (-04:00) becomes 01:00 (-05:00).
        yield return new DstTransition(
            "America/New_York fall-back",
            "America/New_York",
            _Utc(2026, 11, 1, 6, 0),
            TimeSpan.FromHours(-4),
            TimeSpan.FromHours(-5),
            _Utc(2026, 11, 1, 4, 0),
            _Utc(2026, 11, 1, 9, 0),
            [
                // The cadence continues into the hour after the overlap, so 01:30 fires on both passes.
                new("0 30 1,2 * * *", [_Utc(2026, 11, 1, 5, 30), _Utc(2026, 11, 1, 6, 30), _Utc(2026, 11, 1, 7, 30)]),
                // Every second hour: the windows next to the repeated hour hold no occurrence, so no second pass.
                new("0 0 1/2 * * *", [_Utc(2026, 11, 1, 5, 0), _Utc(2026, 11, 1, 8, 0)]),
                new("0 */15 * * * *", _Every(_Utc(2026, 11, 1, 4, 0), _Utc(2026, 11, 1, 9, 0), _Quarter)),
            ]
        );

        // New York springs forward at 07:00Z on 8 March 2026: 02:00 (-05:00) becomes 03:00 (-04:00).
        yield return new DstTransition(
            "America/New_York spring-forward",
            "America/New_York",
            _Utc(2026, 3, 8, 7, 0),
            TimeSpan.FromHours(-5),
            TimeSpan.FromHours(-4),
            _Utc(2026, 3, 8, 6, 0),
            _Utc(2026, 3, 8, 8, 0),
            [
                new("0 */15 * * * *", _Every(_Utc(2026, 3, 8, 6, 0), _Utc(2026, 3, 8, 8, 0), _Quarter)),
                // 02:30 does not exist and fires shifted by the hour, at 03:30 (-04:00).
                new("0 30 2 * * *", [_Utc(2026, 3, 8, 7, 30)]),
            ]
        );

        // Lord Howe falls back half an hour at 15:00Z on 4 April 2026: 02:00 (+11:00) becomes 01:30 (+10:30).
        yield return new DstTransition(
            "Australia/Lord_Howe fall-back",
            "Australia/Lord_Howe",
            _Utc(2026, 4, 4, 15, 0),
            TimeSpan.FromHours(11),
            new TimeSpan(10, 30, 0),
            _Utc(2026, 4, 4, 13, 0),
            _Utc(2026, 4, 4, 17, 0),
            [
                // The repeated half hour [01:30, 02:00) holds :45 fifteen minutes in, the half hour before it holds none:
                // the cadence breaks, so 01:45 does not fire a second time.
                new("0 45 * * * *", [_Utc(2026, 4, 4, 13, 45), _Utc(2026, 4, 4, 14, 45), _Utc(2026, 4, 4, 16, 15)]),
                // A quarter-hour cadence pinned to 01:xx continues through the repeated half hour.
                new(
                    "0 */15 1 * * *",
                    [
                        _Utc(2026, 4, 4, 14, 0),
                        _Utc(2026, 4, 4, 14, 15),
                        _Utc(2026, 4, 4, 14, 30),
                        _Utc(2026, 4, 4, 14, 45),
                        _Utc(2026, 4, 4, 15, 0),
                        _Utc(2026, 4, 4, 15, 15),
                    ]
                ),
                // Equal counts at different offsets in the repeated window and the one before break the cadence.
                new("0 20,40 1 * * *", [_Utc(2026, 4, 4, 14, 20), _Utc(2026, 4, 4, 14, 40)]),
            ]
        );

        // Lord Howe springs forward half an hour at 15:30Z on 3 October 2026: 02:00 (+10:30) becomes 02:30 (+11:00).
        yield return new DstTransition(
            "Australia/Lord_Howe spring-forward",
            "Australia/Lord_Howe",
            _Utc(2026, 10, 3, 15, 30),
            new TimeSpan(10, 30, 0),
            TimeSpan.FromHours(11),
            _Utc(2026, 10, 3, 15, 0),
            _Utc(2026, 10, 3, 17, 0),
            [
                // 02:20 does not exist and shifts to 02:50 (15:50Z); 02:40 exists (15:40Z), so the later local time fires first.
                new("0 20,40 2 * * *", [_Utc(2026, 10, 3, 15, 40), _Utc(2026, 10, 3, 15, 50)]),
                // 02:00 and 02:15 shift onto 02:30 and 02:45, which exist: merged, every quarter fires once.
                new("0 */15 * * * *", _Every(_Utc(2026, 10, 3, 15, 0), _Utc(2026, 10, 3, 17, 0), _Quarter)),
            ]
        );

        // Santiago falls back at 03:00Z on 5 April 2026: 24:00 (-03:00) becomes 23:00 (-04:00) on 4 April.
        yield return new DstTransition(
            "America/Santiago fall-back",
            "America/Santiago",
            _Utc(2026, 4, 5, 3, 0),
            TimeSpan.FromHours(-3),
            TimeSpan.FromHours(-4),
            _Utc(2026, 4, 5, 1, 0),
            _Utc(2026, 4, 5, 5, 0),
            [
                new("0 */15 * * * *", _Every(_Utc(2026, 4, 5, 1, 0), _Utc(2026, 4, 5, 5, 0), _Quarter)),
                new(
                    "0 */15 23 * * *",
                    [_Utc(2026, 4, 5, 2, 0), _Utc(2026, 4, 5, 2, 15), _Utc(2026, 4, 5, 2, 30), _Utc(2026, 4, 5, 2, 45)]
                ),
            ]
        );

        // Santiago springs forward at 04:00Z on 6 September 2026: 00:00 (-04:00) becomes 01:00 (-03:00).
        yield return new DstTransition(
            "America/Santiago spring-forward",
            "America/Santiago",
            _Utc(2026, 9, 6, 4, 0),
            TimeSpan.FromHours(-4),
            TimeSpan.FromHours(-3),
            _Utc(2026, 9, 6, 3, 0),
            _Utc(2026, 9, 6, 5, 0),
            [
                new(
                    "0 */15 0 * * *",
                    [_Utc(2026, 9, 6, 4, 0), _Utc(2026, 9, 6, 4, 15), _Utc(2026, 9, 6, 4, 30), _Utc(2026, 9, 6, 4, 45)]
                ),
                new("0 0 0 * * *", [_Utc(2026, 9, 6, 4, 0)]),
                new("0 */15 * * * *", _Every(_Utc(2026, 9, 6, 3, 0), _Utc(2026, 9, 6, 5, 0), _Quarter)),
            ]
        );
    }

    private static List<DateTime> _Every(DateTime first, DateTime last, TimeSpan step)
    {
        List<DateTime> instants = [];

        for (var instant = first; instant <= last; instant += step)
        {
            instants.Add(instant);
        }

        return instants;
    }

    private static DateTime _Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    #endregion

    private sealed record GoldenDefinition(string Expression, IReadOnlyList<DateTime> Golden);

    private sealed record DstTransition(
        string Name,
        string ZoneId,
        DateTime TransitionUtc,
        TimeSpan OffsetBefore,
        TimeSpan OffsetAfter,
        DateTime WindowStart,
        DateTime WindowEnd,
        IReadOnlyList<GoldenDefinition> Definitions
    );

    private sealed record SoakResult(List<string> Violations, string Counts, string Evidence);
}
