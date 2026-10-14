// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;
using Headless.Messaging.Retry;

namespace Headless.Messaging.Benchmarks.Scenarios;

/// <summary>
/// Cost of entering the retry engine for a delivery whose first attempt succeeds, the overwhelmingly common case,
/// against calling the attempt directly.
/// </summary>
[MemoryDiagnoser]
public class RetryEngineBenchmarks
{
    private static readonly Task<MessagingRetryAttempt> _Succeeded = Task.FromResult(
        MessagingRetryAttempt.Completed(OperateResult.Success)
    );

    private static readonly Task<MessagingRetryAttempt> _Failed = Task.FromResult(
        MessagingRetryAttempt.Retryable(OperateResult.Failed(new InvalidOperationException("boom")))
    );

    private readonly MessagingConsumeRetryPipeline _pipeline = new(TimeProvider.System);
    private readonly Func<int, Exception, TimeSpan, CancellationToken, Task<bool>> _onRetry = static (_, _, _, _) =>
        Task.FromResult(true);
    private readonly Func<int, Exception, CancellationToken, Task> _onNonRetryable = static (_, _, _) =>
        Task.CompletedTask;
    private readonly Func<Exception, bool> _isRetryable = static _ => true;
    private readonly Func<int, CancellationToken, Task<MessagingRetryAttempt>> _succeedImmediately = static (_, _) =>
        _Succeeded;
    private readonly Func<int, CancellationToken, Task<MessagingRetryAttempt>> _failOnceThenSucceed = static (n, _) =>
        n == 0 ? _Failed : _Succeeded;

    [Benchmark(Baseline = true)]
    public async Task<OperateResult> DirectFirstTrySuccess()
    {
        var attempt = await _succeedImmediately(0, CancellationToken.None).ConfigureAwait(false);

        return attempt.Result;
    }

    [Benchmark]
    public Task<OperateResult> EngineFirstTrySuccess() =>
        _pipeline.ExecuteAsync(_succeedImmediately, _onRetry, _onNonRetryable, _isRetryable, CancellationToken.None);

    // Reference: what a delivery that fails once pays inside the engine today. A fast path would add one direct
    // attempt in front of this, so this row bounds the extra cost on the failure path.
    [Benchmark]
    public Task<OperateResult> EngineFailOnceThenSucceed() =>
        _pipeline.ExecuteAsync(_failOnceThenSucceed, _onRetry, _onNonRetryable, _isRetryable, CancellationToken.None);
}
