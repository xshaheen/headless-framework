// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace Headless.Testing.Retry;

/// <summary>
/// Singleton runner that executes a single test or theory row with retry semantics.
/// Intermediate failures are buffered by a <see cref="DelayedMessageBus"/> and discarded;
/// only the final attempt's messages are forwarded to the real bus.
/// A diagnostic message is emitted after each non-final failure.
/// </summary>
[PublicAPI]
public sealed class RetryTestCaseRunner
    : XunitTestCaseRunnerBase<RetryTestCaseRunnerContext, IXunitTestCase, IXunitTest>
{
    /// <summary>The shared singleton instance.</summary>
    public static RetryTestCaseRunner Instance { get; } = new();

    /// <summary>
    /// Runs the <paramref name="testCase"/>, retrying up to <paramref name="maxRetries"/> total
    /// attempts on failure.
    /// </summary>
    /// <param name="maxRetries">Total allowed attempts. Values less than 1 are treated as 3.</param>
    /// <param name="testCase">The test case to run.</param>
    /// <param name="messageBus">The real message bus that receives the final result.</param>
    /// <param name="aggregator">Exception aggregator for the test case lifecycle.</param>
    /// <param name="cancellationTokenSource">Cancellation source for the run.</param>
    /// <param name="displayName">Human-readable test name for diagnostic messages.</param>
    /// <param name="skipReason">Non-null causes an immediate skip result without executing.</param>
    /// <param name="explicitOption">Explicit-test opt-in setting.</param>
    /// <param name="constructorArguments">Arguments for the test class constructor.</param>
    /// <returns>A <see cref="RunSummary"/> reflecting the outcome of the final attempt.</returns>
    public async ValueTask<RunSummary> Run(
        int maxRetries,
        IXunitTestCase testCase,
        IMessageBus messageBus,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource,
        string displayName,
        string? skipReason,
        ExplicitOption explicitOption,
        object?[] constructorArguments
    )
    {
        // This code comes from XunitRunnerHelper.RunXunitTestCase, and it's centralized
        // here just so we don't have to duplicate it in both RetryTestCase and
        // RetryDelayEnumeratedTestCase.
        var tests = await aggregator.RunAsync(testCase.CreateTests, []).ConfigureAwait(false);

        if (aggregator.ToException() is { } e)
        {
            if (e.Message.StartsWith(DynamicSkipToken.Value, StringComparison.Ordinal))
            {
                return XunitRunnerHelper.SkipTestCases(
                    messageBus,
                    cancellationTokenSource,
                    [testCase],
                    e.Message[DynamicSkipToken.Value.Length..],
                    sendTestCaseMessages: false
                );
            }

            return XunitRunnerHelper.FailTestCases(
                messageBus,
                cancellationTokenSource,
                [testCase],
                e,
                sendTestCaseMessages: false
            );
        }

        await using var ctx = new RetryTestCaseRunnerContext(
            maxRetries,
            testCase,
            tests,
            messageBus,
            aggregator,
            cancellationTokenSource,
            displayName,
            skipReason,
            explicitOption,
            constructorArguments
        );

        await ctx.InitializeAsync().ConfigureAwait(false);

        return await Run(ctx).ConfigureAwait(false);
    }

    protected override async ValueTask<RunSummary> RunTest(RetryTestCaseRunnerContext ctx, IXunitTest test)
    {
        var runCount = 0;
        var maxRetries = ctx.MaxRetries;

        if (maxRetries < 1)
        {
            maxRetries = 3;
        }

        while (true)
        {
            // This is really the only tricky bit: we need to capture and delay messages (since those will
            // contain run status) until we know we've decided to accept the final result.
            var delayedMessageBus = new DelayedMessageBus(ctx.MessageBus);
            var aggregator = ctx.Aggregator.Clone();

            var result = await XunitTestRunner
                .Instance.Run(
                    test,
                    delayedMessageBus,
                    ctx.ConstructorArguments,
                    ctx.ExplicitOption,
                    aggregator,
                    ctx.CancellationTokenSource,
                    ctx.BeforeAfterTestAttributes
                )
                .ConfigureAwait(false);

            if (!(aggregator.HasExceptions || result.Failed != 0) || ++runCount >= maxRetries)
            {
                delayedMessageBus.Dispose(); // Sends all the delayed messages
                return result;
            }

            TestContext.Current.SendDiagnosticMessage(
                "Execution of '{0}' failed (attempt #{1}), retrying...",
                test.TestDisplayName,
                runCount
            );

            ctx.Aggregator.Clear();
        }
    }
}
