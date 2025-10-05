// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Xunit.Sdk;
using Xunit.v3;

namespace Framework.Testing.Retry;

// This class is used for facts, and for serializable pre-enumerated individual data rows in theories.
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
    public int MaxRetries { get; private set; } = maxRetries;

    protected override void Deserialize(IXunitSerializationInfo info)
    {
        base.Deserialize(info);

        MaxRetries = info.GetValue<int>(nameof(MaxRetries));
    }

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
