// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace Headless.Testing.Retry;

public sealed class RetryTestCaseRunner
    : XunitTestCaseRunnerBase<RetryTestCaseRunnerContext, IXunitTestCase, IXunitTest>
{
    public static RetryTestCaseRunner Instance { get; } = new();

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
        var tests = await aggregator.RunAsync(testCase.CreateTests, []);

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

        await ctx.InitializeAsync();

        return await Run(ctx);
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

            var result = await XunitTestRunner.Instance.Run(
                test,
                delayedMessageBus,
                ctx.ConstructorArguments,
                ctx.ExplicitOption,
                aggregator,
                ctx.CancellationTokenSource,
                ctx.BeforeAfterTestAttributes
            );

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
