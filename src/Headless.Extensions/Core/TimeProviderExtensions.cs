// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System;

[PublicAPI]
public static class TimeProviderExtensions
{
    extension(TimeProvider timeProvider)
    {
        public async Task DelayUntilElapsedOrCancel(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            try
            {
                await timeProvider.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException) { }
        }

        public Task DelayedAsync(
            TimeSpan delay,
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken = default
        )
        {
            return Task.DelayedAsync(delay, action, timeProvider, cancellationToken);
        }
    }
}
