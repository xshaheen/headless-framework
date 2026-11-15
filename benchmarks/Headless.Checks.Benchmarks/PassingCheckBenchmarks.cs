// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace Headless.Checks.Benchmarks;

/// <summary>
/// Cost of a check that passes. A custom message is only read when the check fails, so a passing check should cost
/// the same whether or not the caller wrote an interpolated message.
/// </summary>
[MemoryDiagnoser]
public class PassingCheckBenchmarks
{
    // Instance fields, not constants, so neither the compiler nor the JIT can fold the condition or the message.
    private readonly int _count = 3;
    private readonly int _max = 100;
    private readonly string _what = "The current tenant id";
    private readonly string _tenantId = "tenant-1";

    [Benchmark(Baseline = true)]
    public void EnsureTrueNoMessage()
    {
        Ensure.True(_count < _max);
    }

    [Benchmark]
    public void EnsureTrueInterpolatedMessage()
    {
        Ensure.True(_count < _max, $"Maximum waiters per resource ({_max}) exceeded");
    }

    [Benchmark]
    public void EnsureTrueStringCreateMessage()
    {
        Ensure.True(
            _count < _max,
            string.Create(CultureInfo.InvariantCulture, $"Maximum waiters per resource ({_max}) exceeded")
        );
    }

    [Benchmark]
    public void ArgumentIsTrueInterpolatedMessage()
    {
        Argument.IsTrue(
            !string.IsNullOrWhiteSpace(_tenantId),
            $"{_what} is empty or whitespace-only, so no lease can be keyed by it.",
            "tenantId"
        );
    }
}
