// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

public static class TimeProviderExtensions
{
    public static async Task DelayUntilElapsedOrCancel(
        this TimeProvider timeProvider,
        TimeSpan delay,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await timeProvider.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException) { }
    }

    public static async Task Delay(
        this TimeProvider timeProvider,
        TimeSpan delay,
        CancellationToken cancellationToken = default
    )
    {
        await Task.Delay(delay, timeProvider, cancellationToken);
    }
}
