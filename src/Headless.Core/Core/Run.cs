// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Core;

public static class Run
{
    public static Task DelayedAsync(
        TimeSpan delay,
        Func<CancellationToken, Task> action,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(delay);
        Argument.IsNotNull(action);

        timeProvider ??= TimeProvider.System;

        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            async () =>
            {
                if (delay.Ticks > 0)
                {
                    await timeProvider.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();

                await action(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken
        );
    }
}
