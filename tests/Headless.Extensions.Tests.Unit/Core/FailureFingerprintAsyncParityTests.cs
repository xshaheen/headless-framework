// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless;
using Headless.Extensions.NetStandardFixture;
using Headless.Testing.Tests;

namespace Tests.Core;

/// <summary>
/// Stored fingerprints must not move when the runtime changes how async methods run. On .NET 11 the compiler can hand
/// async methods to the runtime (runtime async) instead of generating state machines, which changes the frames an
/// exception's stack trace carries. These tests pin literal fingerprints recorded on .NET 10, where every async method
/// was a state machine, for a failure that crosses a lambda, a local function, and an async iterator, and for one that
/// crosses a netstandard2.0 state machine. A failing value here means stored rows would regroup: that is a new
/// fingerprint version, never a new literal under the same prefix.
/// </summary>
public sealed class FailureFingerprintAsyncParityTests : TestBase
{
    // Hashes "System.InvalidOperationException", then "Tests.Core.FailureFingerprintAsyncParityTests._LambdaFrameAsync",
    // "Tests.Core.FailureFingerprintAsyncParityTests._LocalFunctionFrameAsync" and
    // "Tests.Core.FailureFingerprintAsyncParityTests._IteratorFrameAsync".
    private const string _ChainBaseline = "v1:944f40b2a3b8cf39";

    // Hashes "System.TimeoutException", then "Tests.Core.FailureFingerprintAsyncParityTests._ThrowAfterAsync",
    // "Headless.Extensions.NetStandardFixture.NetStandardAsyncFrame.RunAsync" and
    // "Tests.Core.FailureFingerprintAsyncParityTests._CatchAsync".
    private const string _NetStandardBaseline = "v1:f21d4b51dd0a0412";

    // Hashes "System.NotSupportedException", then "Tests.Core.FailureFingerprintAsyncParityTests._FailValueTaskAsync",
    // "Tests.Core.FailureFingerprintAsyncParityTests._AwaitValueTaskForwarderAsync" and
    // "Tests.Core.FailureFingerprintAsyncParityTests._CatchAsync".
    private const string _ValueTaskForwarderBaseline = "v1:d9f89e134799c229";

    // Hashes "System.FormatException", then "Tests.Core.FailureFingerprintAsyncParityTests._FailSynchronously",
    // "Tests.Core.FailureFingerprintAsyncParityTests._ForwardToSynchronousFailure" and
    // "Tests.Core.FailureFingerprintAsyncParityTests.should_keep_a_forwarder_frame_when_its_callee_is_not_async".
    private const string _SynchronousForwarderBaseline = "v1:9851050bdadc81b5";

    // Hashes "System.TimeoutException", then "Tests.Core.FailureFingerprintAsyncParityTests._ThrowAfterAsync",
    // "Tests.Core.FailureFingerprintAsyncParityTests._BlockOn" and
    // "Tests.Core.FailureFingerprintAsyncParityTests.should_keep_a_blocking_caller_frame_when_it_waits_on_an_async_failure".
    private const string _BlockingCallerBaseline = "v1:bfb4bed139c81472";

    // Hashes "System.TimeoutException", then "Tests.Core.FailureFingerprintAsyncParityTests._ThrowAfterAsync",
    // "Tests.Core.FailureFingerprintAsyncParityTests._BlockOnTask" and
    // "Tests.Core.FailureFingerprintAsyncParityTests.should_keep_a_task_returning_blocking_caller_frame_when_it_waits_on_an_async_failure".
    private const string _BlockingTaskCallerBaseline = "v1:21f1d92b8f82e13c";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_keep_the_net10_fingerprint_when_a_failure_crosses_lambda_local_function_and_iterator_frames(
        bool suspend
    )
    {
        var exception = await _CatchAsync(() => _DrainAsync(_IteratorFrameAsync(suspend)));

        _WriteFrames(exception);
        FailureFingerprint.Compute(exception).Should().Be(_ChainBaseline, because: exception.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_reduce_a_netstandard_state_machine_frame_to_its_declaring_method(bool suspend)
    {
        var exception = await _CatchAsync(() =>
            NetStandardAsyncFrame.RunAsync(() => _ThrowAfterAsync(suspend), suspend)
        );

        _WriteFrames(exception);
        // The netstandard2.0 frame is a state machine on every runtime, so the reduction is what keeps it stable.
        _RawFrames(exception).Should().Contain(frame => frame.StartsWith("<RunAsync>d__", StringComparison.Ordinal));
        FailureFingerprint.Compute(exception).Should().Be(_NetStandardBaseline, because: exception.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_skip_a_value_task_forwarder_when_its_callee_fails_before_it_suspends(bool suspend)
    {
        var exception = await _CatchAsync(() => _AwaitValueTaskForwarderAsync(suspend));

        _WriteFrames(exception);
        FailureFingerprint.Compute(exception).Should().Be(_ValueTaskForwarderBaseline, because: exception.ToString());
    }

    [Fact]
    public async Task should_keep_a_forwarder_frame_when_its_callee_is_not_async()
    {
        var exception = await _CatchAsync(() => _ForwardToSynchronousFailure());

        _WriteFrames(exception);
        FailureFingerprint.Compute(exception).Should().Be(_SynchronousForwarderBaseline, because: exception.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_keep_a_blocking_caller_frame_when_it_waits_on_an_async_failure(bool suspend)
    {
        Exception? exception = null;
        try
        {
            _BlockOn(suspend);
        }
        catch (TimeoutException ex)
        {
            exception = ex;
        }

        exception.Should().NotBeNull();
        _WriteFrames(exception!);
        FailureFingerprint.Compute(exception!).Should().Be(_BlockingCallerBaseline, because: exception!.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_keep_a_task_returning_blocking_caller_frame_when_it_waits_on_an_async_failure(bool suspend)
    {
        // Returns a task like a forwarder, but blocks on its callee instead of handing that callee's task back.
        Exception? exception = null;
        try
        {
            _ = _BlockOnTask(suspend);
        }
        catch (TimeoutException ex)
        {
            exception = ex;
        }

        exception.Should().NotBeNull();
        _WriteFrames(exception!);
        FailureFingerprint.Compute(exception!).Should().Be(_BlockingTaskCallerBaseline, because: exception!.ToString());
    }

    #region Helpers

    // Type and method names as the runtime reports them, before any reduction: the test output shows whether a frame
    // ran as a state machine (<Method>d__N::MoveNext) or as the method itself.
    private static IEnumerable<string> _RawFrames(Exception exception) =>
        new StackTrace(exception, fNeedFileInfo: false)
            .GetFrames()
            .Select(frame => frame.GetMethod())
            .Where(method => method is not null)
            .Select(method =>
                method!.DeclaringType?.Name + "::" + method.Name + " [" + method.MethodImplementationFlags + "]"
            );

    private static void _WriteFrames(Exception exception)
    {
        foreach (var frame in _RawFrames(exception))
        {
            TestContext.Current.TestOutputHelper?.WriteLine(frame);
        }
    }

    private static async Task<Exception> _CatchAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
#pragma warning disable ERP022 // False positive: the caught exception is the helper's return value, which every test asserts on.
            return ex;
#pragma warning restore ERP022
        }

        throw new InvalidOperationException("The action did not throw.");
    }

    private static async Task _DrainAsync(IAsyncEnumerable<int> sequence)
    {
        await foreach (var _ in sequence.WithCancellation(AbortToken))
        {
            // The sequence fails before it yields.
        }
    }

    // The third hashed frame: an async iterator that awaits the local function.
    private static async IAsyncEnumerable<int> _IteratorFrameAsync(bool suspend)
    {
        if (suspend)
        {
            await Task.Yield();
        }

        await _LocalFunctionFrameAsync(suspend);

        yield return 0;
    }

    // The second hashed frame: an async local function that awaits the lambda.
    private static Task _LocalFunctionFrameAsync(bool suspend)
    {
        return awaitLambdaAsync();

        async Task awaitLambdaAsync()
        {
            if (suspend)
            {
                await Task.Yield();
            }

            await _LambdaFrameAsync(suspend);
        }
    }

    // The first hashed frame, where the failure starts: an async lambda that captures state, so it compiles to a
    // closure method.
    private static Task _LambdaFrameAsync(bool suspend)
    {
        Func<Task> lambda = async () =>
        {
            if (suspend)
            {
                await Task.Yield();
            }

            throw new InvalidOperationException("lambda failed");
        };

        return lambda();
    }

    private static async Task _AwaitValueTaskForwarderAsync(bool suspend)
    {
        if (suspend)
        {
            await Task.Yield();
        }

        await _ValueTaskForwarder(suspend);
    }

    // Not async: it hands back the task of an async callee.
    private static ValueTask _ValueTaskForwarder(bool suspend) => _FailValueTaskAsync(suspend);

    private static async ValueTask _FailValueTaskAsync(bool suspend)
    {
        if (suspend)
        {
            await Task.Yield();
        }

        throw new NotSupportedException("value task failed");
    }

    // Neither method is async, so both stay on the stack whatever the runtime does.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task _ForwardToSynchronousFailure() => _FailSynchronously();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task _FailSynchronously() => throw new FormatException("failed before any task existed");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void _BlockOn(bool suspend) => _ThrowAfterAsync(suspend).GetAwaiter().GetResult();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task _BlockOnTask(bool suspend)
    {
        _ThrowAfterAsync(suspend).GetAwaiter().GetResult();

        return Task.CompletedTask;
    }

    private static async Task _ThrowAfterAsync(bool suspend)
    {
        if (suspend)
        {
            await Task.Yield();
        }

        throw new TimeoutException("timed out");
    }

    #endregion
}
