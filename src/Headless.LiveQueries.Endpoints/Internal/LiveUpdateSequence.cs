// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries.Internal;

/// <summary>
/// The updates of one stream as a transport receives them: the first result, which the stream's owner already
/// fetched, then the rest. The owner keeps the lifetime of the underlying subscription, because a transport may
/// leave while a step is still in flight and the subscription must not be disposed under it.
/// </summary>
/// <param name="source">The subscription, already moved to its first update.</param>
internal sealed class LiveUpdateSequence(IAsyncEnumerator<LiveUpdate> source) : IAsyncEnumerable<LiveUpdate>
{
    private readonly IAsyncEnumerator<LiveUpdate> _source = source;
    private Task<bool>? _pending;
    private bool _firstTaken;

    public IAsyncEnumerator<LiveUpdate> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return new Enumerator(this);
    }

    /// <summary>
    /// Ends the subscription. Call it after cancelling the subscription's token, so a step still in flight ends.
    /// </summary>
    public async ValueTask CompleteAsync()
    {
        if (_pending is { } pending)
        {
            // Its outcome was the transport's to observe; here it only has to be over before the dispose.
            await ((Task)pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        await _source.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class Enumerator(LiveUpdateSequence sequence) : IAsyncEnumerator<LiveUpdate>
    {
        public LiveUpdate Current => sequence._source.Current;

        // Not an async method on purpose: the step starts on the caller's execution flow, which is the request's.
        public ValueTask<bool> MoveNextAsync()
        {
            if (!sequence._firstTaken)
            {
                sequence._firstTaken = true;

                return new(result: true);
            }

            sequence._pending = sequence._source.MoveNextAsync().AsTask();

            return new(sequence._pending);
        }

        // Nothing to release: the stream's owner ends the subscription through CompleteAsync.
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
