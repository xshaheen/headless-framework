// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.LiveQueries.Internal;

internal sealed class LiveQueryInvalidator(LiveQueryHub hub) : ILiveQueryInvalidator
{
    public ValueTask InvalidateAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(keys);

        string[] changed = [.. keys];

        foreach (var key in changed)
        {
            Argument.IsNotNullOrEmpty(key, paramName: nameof(keys));
        }

        // A run that announces a key it read is stale the moment it returns, so the hub would run it again, and
        // that run would announce again. Refused for any key, because the query reads whatever it reads next time.
        if (changed.Length > 0 && LiveQueryRecorder.Current is not null)
        {
            throw LiveReadOnlyRule.Announced(changed);
        }

        cancellationToken.ThrowIfCancellationRequested();
        hub.Invalidate(new LiveWriteSet(changed, []));

        return ValueTask.CompletedTask;
    }
}
