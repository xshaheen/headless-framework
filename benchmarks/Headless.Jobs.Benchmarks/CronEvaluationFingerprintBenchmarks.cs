// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;

namespace Headless.Jobs.Benchmarks;

/// <summary>
/// Cost of the evaluation fingerprint dispatch stamps on every schedule advance. The memoized row is what dispatch pays;
/// the fresh row is the SHA-256 over the zone's adjustment rules that the memo saves on each advance.
/// </summary>
public class CronEvaluationFingerprintBenchmarks
{
    private CronScheduleCache _cache = null!;
    private TimeZoneInfo _zone = null!;

    [Params("UTC", "Africa/Cairo")]
    public string ZoneId { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        _zone = ZoneId == "UTC" ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(ZoneId);
        _cache = new CronScheduleCache(_zone);

        // Warm the memo: dispatch reaches it after the first advance in a zone.
        _ = _cache.ComputeEvaluationFingerprint(timeZoneId: null);
    }

    [Benchmark(Baseline = true)]
    public string Memoized()
    {
        return _cache.ComputeEvaluationFingerprint(timeZoneId: null);
    }

    [Benchmark]
    public string Fresh()
    {
        return CronEvaluationFingerprint.Compute(_zone);
    }
}
