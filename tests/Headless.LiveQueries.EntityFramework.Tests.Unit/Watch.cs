// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.LiveQueries;

namespace Tests;

/// <summary>
/// One live query a test watches. A run happens only inside <see cref="NextAsync" />, on the test's own flow, so a
/// test decides when the query touches the database and never races it.
/// </summary>
internal sealed class Watch<T> : IAsyncDisposable
{
    // Bounds a wait that only a defect makes endless; no assertion depends on how long anything takes.
    private static readonly TimeSpan _Patience = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource _stop;
    private readonly IAsyncEnumerator<LiveSnapshot<T>> _results;
    private Task<bool>? _pending;

    public Watch(
        ILiveQueries queries,
        Func<IServiceProvider, CancellationToken, ValueTask<T>> query,
        CancellationToken abortToken
    )
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(abortToken);
        _results = queries
            .WatchAsync(
                async (services, cancellationToken) =>
                {
                    Runs++;

                    return await query(services, cancellationToken);
                },
                _stop.Token
            )
            .GetAsyncEnumerator(_stop.Token);
    }

    /// <summary>How many times the query has started.</summary>
    public int Runs { get; private set; }

    /// <summary>Returns the next result that differs from the last one, running the query when it was signalled.</summary>
    public async Task<T> NextAsync()
    {
        _pending = _results.MoveNextAsync().AsTask();
        var moved = await _pending.WaitAsync(_Patience, _stop.Token);
        _pending = null;
        moved.Should().BeTrue();

        return _results.Current.Value;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();

        if (_pending is { } pending)
        {
            // A result the test stopped waiting for: let the enumerator finish before it is disposed.
            await ((Task)pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        await _results.DisposeAsync();
        _stop.Dispose();
    }
}
