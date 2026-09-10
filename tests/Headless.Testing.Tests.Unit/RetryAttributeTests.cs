// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Testing.Retry;
using Xunit.Sdk;
using Xunit.v3;

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

    [RetryTheory(MaxRetries = 2, DisableDiscoveryEnumeration = true)]
    [InlineData("delayed-row")]
    public void should_retry_delay_enumerated_theory_rows(string row)
    {
        _AssertAttempt(row, expectedSuccessfulAttempt: 2);
    }

    [Fact]
    public void should_round_trip_retry_test_case_serialization()
    {
        var testCase = new RetryTestCase(
            4,
            _CreateTestMethod(nameof(should_retry_fact_until_it_succeeds)),
            "display-name",
            "test-case-id",
            @explicit: false,
            testLabel: "row-label",
            disableParallelization: true
        );

        testCase.TestLabel.Should().Be("row-label");

        var serialized = SerializationHelper.Instance.Serialize(testCase);
        var deserialized = Assert.IsType<RetryTestCase>(SerializationHelper.Instance.Deserialize(serialized));

        deserialized.MaxRetries.Should().Be(4);
        deserialized.DisableParallelization.Should().BeTrue();
    }

    [Fact]
    public void should_round_trip_delay_enumerated_retry_test_case_serialization()
    {
        var testCase = new RetryDelayEnumeratedTestCase(
            5,
            _CreateTestMethod(nameof(should_retry_delay_enumerated_theory_rows)),
            "display-name",
            "test-case-id",
            @explicit: false,
            skipTestWithoutData: true
        );

        var serialized = SerializationHelper.Instance.Serialize(testCase);
        var deserialized = Assert.IsType<RetryDelayEnumeratedTestCase>(
            SerializationHelper.Instance.Deserialize(serialized)
        );

        deserialized.MaxRetries.Should().Be(5);
        deserialized.SkipTestWithoutData.Should().BeTrue();
    }

    private static XunitTestMethod _CreateTestMethod(string methodName)
    {
        var assembly = new XunitTestAssembly(typeof(RetryAttributeTests).Assembly, configFilePath: null);
        var collection = new XunitTestCollection(
            assembly,
            collectionDefinition: null,
            disableParallelization: false,
            displayName: "retry-tests",
            uniqueID: "collection-id"
        );
        var testClass = new XunitTestClass(typeof(RetryAttributeTests), collection, uniqueID: "class-id");
        var method = typeof(RetryAttributeTests).GetMethod(methodName)!;

        return new XunitTestMethod(testClass, method, testMethodArguments: [], uniqueID: "method-id");
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
