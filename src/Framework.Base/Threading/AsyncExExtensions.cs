// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Nito.AsyncEx;

[PublicAPI]
public static class AsyncExExtensions
{
    [DebuggerStepThrough]
    public static async Task WaitAsync(this AsyncAutoResetEvent resetEvent, TimeSpan timeout)
    {
        using var timeoutCancellationTokenSource = timeout.ToCancellationTokenSource();
        await resetEvent.WaitAsync(timeoutCancellationTokenSource.Token).AnyContext();
    }

    [DebuggerStepThrough]
    public static async Task SafeWaitAsync(this AsyncAutoResetEvent resetEvent, TimeSpan timeout)
    {
        try
        {
            await resetEvent.WaitAsync(timeout);
        }
        catch (OperationCanceledException) { }
    }

    [DebuggerStepThrough]
    public static async Task SafeWaitAsync(this AsyncAutoResetEvent resetEvent, CancellationToken cancellationToken)
    {
        try
        {
            await resetEvent.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) { }
    }

    [DebuggerStepThrough]
    public static async Task WaitAsync(this AsyncManualResetEvent resetEvent, TimeSpan timeout)
    {
        using var cts = timeout.ToCancellationTokenSource();
        await resetEvent.WaitAsync(cts.Token).AnyContext();
    }

    [DebuggerStepThrough]
    public static async Task SafeWaitAsync(this AsyncManualResetEvent resetEvent, TimeSpan timeout)
    {
        try
        {
            await resetEvent.WaitAsync(timeout);
        }
        catch (OperationCanceledException) { }
    }

    [DebuggerStepThrough]
    public static async Task SafeWaitAsync(this AsyncManualResetEvent resetEvent, CancellationToken cancellationToken)
    {
        try
        {
            await resetEvent.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) { }
    }

    [DebuggerStepThrough]
    public static async Task WaitAsync(this AsyncCountdownEvent countdownEvent, TimeSpan timeout)
    {
        _ = await Task.WhenAny(countdownEvent.WaitAsync(), Task.Delay(timeout)).ConfigureAwait(false);
    }

    [DebuggerStepThrough]
    public static async Task SafeWaitAsync(this AsyncCountdownEvent resetEvent, TimeSpan timeout)
    {
        try
        {
            await resetEvent.WaitAsync(timeout);
        }
        catch (OperationCanceledException) { }
    }

    [DebuggerStepThrough]
    public static async Task SafeWaitAsync(this AsyncCountdownEvent resetEvent, CancellationToken cancellationToken)
    {
        try
        {
            await resetEvent.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) { }
    }
}
