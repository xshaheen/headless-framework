// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Extensions.NetStandardFixture;

/// <summary>An async frame compiled for netstandard2.0, so it always runs as a compiler-generated state machine.</summary>
public static class NetStandardAsyncFrame
{
    /// <summary>Awaits <paramref name="inner"/>, suspending first when <paramref name="suspend"/> is set.</summary>
    /// <param name="inner">The awaited call whose failure passes through this frame.</param>
    /// <param name="suspend">Whether this frame yields before it awaits <paramref name="inner"/>.</param>
    /// <returns>The task of <paramref name="inner"/>, awaited inside this frame.</returns>
    public static async Task RunAsync(Func<Task> inner, bool suspend)
    {
        if (suspend)
        {
            await Task.Yield();
        }

        await inner().ConfigureAwait(false);
    }
}
