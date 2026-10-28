// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;

namespace Headless.Jobs.Benchmarks;

/// <summary>
/// Cost of computing one cron occurrence, the call the dispatch loop and the missed-run walk make per pending instant.
/// The away-from-transition rows are the hot path and must stay flat; the across-transition rows price the DST
/// mapping a zone pays a few nights a year.
/// </summary>
public class CronNextOccurrenceBenchmarks
{
    private CronScheduleCache _cache = null!;
    private DateTime _cursorUtc;

    [Params("0 */15 * * * *", "0 30 2 * * *", "* * * * * *")]
    public string Expression { get; set; } = null!;

    [Params(CronZoneScenario.Utc, CronZoneScenario.DstAwayFromTransition, CronZoneScenario.DstAcrossTransition)]
    public CronZoneScenario Scenario { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var zone =
            Scenario == CronZoneScenario.Utc ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        _cache = new CronScheduleCache(zone);
        _cursorUtc = Scenario switch
        {
            // Cairo falls back at 21:00Z on 29 October 2026; 20:50Z sits ten minutes before it.
            CronZoneScenario.DstAcrossTransition => new DateTime(2026, 10, 29, 20, 50, 0, DateTimeKind.Utc),
            _ => new DateTime(2026, 7, 15, 10, 7, 0, DateTimeKind.Utc),
        };

        // Parse outside the measurement: the expression cache is warm on every real dispatch.
        _ = _cache.GetNextOccurrenceOrDefault(Expression, _cursorUtc);
    }

    [Benchmark]
    public DateTime? NextOccurrence()
    {
        return _cache.GetNextOccurrenceOrDefault(Expression, _cursorUtc);
    }

    public enum CronZoneScenario
    {
        Utc,
        DstAwayFromTransition,
        DstAcrossTransition,
    }
}
