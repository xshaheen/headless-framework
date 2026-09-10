// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Xunit.Sdk;
using Xunit.v3;

namespace Headless.Testing.Retry;

/// <summary>
/// Execution context for <see cref="RetryTestCaseRunner"/>. Extends the base xUnit context
/// with the <see cref="MaxRetries"/> limit.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
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
    object?[] constructorArguments,
    ParallelMode parallelMode,
    ExecutionScheduler scheduler,
    FixtureMappingManager methodFixtureMappings
)
    : XunitTestCaseRunnerBaseContext<IXunitTestCase, IXunitTest>(
        testCase,
        tests,
        explicitOption,
        messageBus,
        aggregator,
        displayName,
        skipReason,
        cancellationTokenSource,
        parallelMode,
        scheduler,
        constructorArguments,
        methodFixtureMappings
    )
{
    /// <summary>Maximum total execution attempts for the test in this context.</summary>
    public int MaxRetries { get; } = maxRetries;
}
