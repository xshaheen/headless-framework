// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Xunit.Sdk;
using Xunit.v3;

namespace Headless.Testing.Retry;

public sealed class RetryTestCaseRunnerContext(
    int maxRetries,
    IXunitTestCase testCase,
    IReadOnlyCollection<IXunitTest> tests,
    IMessageBus messageBus,
    ExceptionAggregator aggregator,
    CancellationTokenSource cancellationTokenSource,
    string displayName,
    string? skipReason,
    ExplicitOption explicitOption,
    object?[] constructorArguments
)
    : XunitTestCaseRunnerBaseContext<IXunitTestCase, IXunitTest>(
        testCase,
        tests,
        messageBus,
        aggregator,
        cancellationTokenSource,
        displayName,
        skipReason,
        explicitOption,
        constructorArguments
    )
{
    public int MaxRetries { get; } = maxRetries;
}
