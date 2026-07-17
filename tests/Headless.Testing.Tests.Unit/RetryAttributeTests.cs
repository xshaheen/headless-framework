// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Testing.Retry;

namespace Tests;

public sealed class RetryAttributeTests
{
    private static readonly ConcurrentDictionary<string, int> _Attempts = new(StringComparer.Ordinal);

    [RetryFact(MaxRetries = 2)]
    public void should_retry_fact_until_it_succeeds()
    {
        _AssertAttempt("fact", expectedSuccessfulAttempt: 2);
    }

    [RetryFact(MaxRetries = 0)]
    public void should_use_three_attempts_when_max_retries_is_less_than_one()
    {
        _AssertAttempt("fallback", expectedSuccessfulAttempt: 3);
    }

    [RetryTheory(MaxRetries = 2)]
    [InlineData("first-row")]
    [InlineData("second-row")]
    public void should_retry_each_theory_row_independently(string row)
    {
        _AssertAttempt(row, expectedSuccessfulAttempt: 2);
    }

    private static void _AssertAttempt(string key, int expectedSuccessfulAttempt)
    {
        var attempt = _Attempts.AddOrUpdate(key, 1, static (_, current) => current + 1);

        if (attempt == expectedSuccessfulAttempt)
        {
            _Attempts.TryRemove(key, out _);
        }

        attempt.Should().Be(expectedSuccessfulAttempt);
    }
}
