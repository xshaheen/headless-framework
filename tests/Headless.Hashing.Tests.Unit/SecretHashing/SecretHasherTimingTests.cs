// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Tests.SecretHashing;

/// <summary>
/// Measures wall-clock verification time, so it only runs when explicitly enabled. CI runs every unit test module with
/// no trait filter, and a shared runner's scheduler jitter would make any elapsed-time assertion flaky there. The
/// deterministic proof that verification does the same work for every wrong secret lives in
/// <see cref="SecretHasherTests" />; this test is the empirical cross-check.
/// </summary>
public sealed class SecretHasherTimingTests
{
    private const string _EnableVariable = "HEADLESS_RUN_TIMING_TESTS";

    [Fact]
    [Trait("Category", "Timing")]
    public void should_take_indistinguishable_time_for_wrong_secrets_of_matching_and_different_length()
    {
        Assert.SkipUnless(
            string.Equals(Environment.GetEnvironmentVariable(_EnableVariable), "1", StringComparison.Ordinal),
            $"Set {_EnableVariable}=1 to run wall-clock timing tests."
        );

        // given — enough iterations that each verify takes several milliseconds, so timer resolution is irrelevant.
        var sut = SecretHashingTestKit.CreateHasher(SecretHashingTestKit.Pbkdf2Options(iterations: 20_000));
        var stored = sut.Hash("correct-horse-12");
        const string sameLength = "wrong-horse-1234";
        const string differentLength = "x";

        for (var i = 0; i < 5; i++)
        {
            sut.Verify(sameLength, stored);
            sut.Verify(differentLength, stored);
        }

        // when — interleave the two cases so drift in machine load affects both equally.
        const int rounds = 60;
        var sameLengthTimes = new double[rounds];
        var differentLengthTimes = new double[rounds];

        for (var i = 0; i < rounds; i++)
        {
            sameLengthTimes[i] = _Measure(() => sut.Verify(sameLength, stored));
            differentLengthTimes[i] = _Measure(() => sut.Verify(differentLength, stored));
        }

        // then
        var ratio = _Median(sameLengthTimes) / _Median(differentLengthTimes);
        ratio.Should().BeInRange(0.85, 1.15);
    }

    private static double _Measure(Action action)
    {
        var start = Stopwatch.GetTimestamp();
        action();

        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private static double _Median(double[] values)
    {
        var sorted = values.Order().ToArray();

        return sorted[sorted.Length / 2];
    }
}
