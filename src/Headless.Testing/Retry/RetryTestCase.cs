// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Xunit.Sdk;
using Xunit.v3;

namespace Headless.Testing.Retry;

/// <summary>
/// xUnit v3 test case that re-runs a fact or a single pre-enumerated theory data row up to
/// <see cref="MaxRetries"/> times before accepting a failure. Intermediate failures are suppressed
/// via <see cref="DelayedMessageBus"/>; only the final attempt's result is forwarded.
/// </summary>
// This class is used for facts, and for serializable pre-enumerated individual data rows in theories.
[PublicAPI]
public sealed class RetryTestCase(
    int maxRetries,
    IXunitTestMethod testMethod,
    string testCaseDisplayName,
    string uniqueId,
    bool @explicit,
    Type[]? skipExceptions = null,
    string? skipReason = null,
    Type? skipType = null,
    string? skipUnless = null,
    string? skipWhen = null,
    Dictionary<string, HashSet<string>>? traits = null,
    object?[]? testMethodArguments = null,
    string? sourceFilePath = null,
    int? sourceLineNumber = null,
    int? timeout = null
)
    : XunitTestCase(
        testMethod,
        testCaseDisplayName,
        uniqueId,
        @explicit,
        skipExceptions,
        skipReason,
        skipType,
        skipUnless,
        skipWhen,
        traits,
        testMethodArguments,
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
    /// Executes the test via <see cref="RetryTestCaseRunner"/>, retrying up to
    /// <see cref="MaxRetries"/> times on failure.
    /// </summary>
    public async ValueTask<RunSummary> Run(
        ExplicitOption explicitOption,
        IMessageBus messageBus,
        object?[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource
    )
    {
        return await RetryTestCaseRunner.Instance.Run(
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
