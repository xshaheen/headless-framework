// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.LiveQueries.Internal;

/// <summary>Runs a query delegate as a live query for .NET callers.</summary>
internal sealed class LiveQueryWatcher(LiveQueryHub hub, IServiceScopeFactory scopes) : ILiveQueries
{
    public IAsyncEnumerable<LiveSnapshot<T>> WatchAsync<T>(
        Func<IServiceProvider, CancellationToken, ValueTask<T>> query,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);

        return _WatchAsync(query, cancellationToken);
    }

    private async IAsyncEnumerable<LiveSnapshot<T>> _WatchAsync<T>(
        Func<IServiceProvider, CancellationToken, ValueTask<T>> query,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        // The hub compares and forwards bytes only. The typed value of the run it just reported is kept here, so
        // the caller receives the object the query returned rather than a deserialized copy.
        var latest = new StrongBox<T>();
        var updates = hub.WatchAsync(runToken => _RunAsync(query, latest, runToken), cancellationToken);

        await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return new LiveSnapshot<T>
            {
                Value = latest.Value!,
                Version = update.Version,
                IsInitial = update.Kind is LiveUpdateKind.Initial,
            };
        }
    }

    private async ValueTask<LiveRunResult> _RunAsync<T>(
        Func<IServiceProvider, CancellationToken, ValueTask<T>> query,
        StrongBox<T> latest,
        CancellationToken cancellationToken
    )
    {
        var recorder = LiveQueryRecorder.Begin();
        var scope = scopes.CreateAsyncScope();

        try
        {
            await using (scope.ConfigureAwait(false))
            {
                var value = await query(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
                latest.Value = value;

                return new LiveRunResult(
                    JsonSerializer.SerializeToUtf8Bytes(value),
                    Failed: false,
                    recorder.Snapshot()
                );
            }
        }
        finally
        {
            // After the snapshot: a task the query left running must not stay inside a live query.
            recorder.End();
        }
    }
}
