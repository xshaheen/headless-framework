// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Xunit.Sdk;
using Xunit.v3;

namespace Framework.Testing.Retry;

// This class is used when pre-enumeration is disabled, or when the theory data was not serializable.
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
    public int MaxRetries { get; private set; } = maxRetries;

    protected override void Deserialize(IXunitSerializationInfo info)
    {
        base.Deserialize(info);

        MaxRetries = info.GetValue<int>(nameof(MaxRetries));
    }

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
