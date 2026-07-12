// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Xunit.Sdk;
using Xunit.v3;

namespace Headless.Testing.Retry;

/// <summary>
/// xUnit v3 delay-enumerated theory test case that re-runs each data row up to
/// <see cref="MaxRetries"/> times on failure. Used when pre-enumeration is disabled or
/// theory data is not serializable. Intermediate failures are suppressed via
/// <see cref="DelayedMessageBus"/>; only the final attempt's result is forwarded.
/// </summary>
// This class is used when pre-enumeration is disabled, or when the theory data was not serializable.
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class RetryDelayEnumeratedTestCase(
    int maxRetries,
    IXunitTestMethod testMethod,
    string testCaseDisplayName,
    string uniqueId,
    bool @explicit,
    bool skipTestWithoutData,
    Type[]? skipExceptions = null,
    string? skipReason = null,
    Type? skipType = null,
    string? skipUnless = null,
    string? skipWhen = null,
    Dictionary<string, HashSet<string>>? traits = null,
    string? sourceFilePath = null,
    int? sourceLineNumber = null,
    int? timeout = null
)
    : XunitDelayEnumeratedTheoryTestCase(
        testMethod,
        testCaseDisplayName,
        uniqueId,
        @explicit,
        skipTestWithoutData,
        skipExceptions,
        skipReason,
        skipType,
        skipUnless,
        skipWhen,
        traits,
        sourceFilePath,
        sourceLineNumber,
        timeout
    ),
        ISelfExecutingXunitTestCase
{
    /// <summary>Maximum number of total execution attempts (including the first run).</summary>
    public int MaxRetries { get; private set; } = maxRetries;

    protected override void Deserialize(IXunitSerializationInfo info)
    {
        base.Deserialize(info);

        MaxRetries = info.GetValue<int>(nameof(MaxRetries));
    }

    /// <summary>
    /// Executes the theory via <see cref="RetryTestCaseRunner"/>, retrying up to
    /// <see cref="MaxRetries"/> times on failure.
    /// </summary>
    public ValueTask<RunSummary> Run(
        ExplicitOption explicitOption,
        IMessageBus messageBus,
        object?[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource
    )
    {
        return RetryTestCaseRunner.Instance.Run(
            MaxRetries,
            this,
            messageBus,
            aggregator.Clone(),
            cancellationTokenSource,
            TestCaseDisplayName,
            SkipReason,
            explicitOption,
            constructorArguments
        );
    }

    protected override void Serialize(IXunitSerializationInfo info)
    {
        base.Serialize(info);

        info.AddValue(nameof(MaxRetries), MaxRetries);
    }
}
