// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// The interceptors of one tracked context type. They hold no per-context state, so one set serves every instance
/// of the context, pooled or not.
/// </summary>
/// <typeparam name="TContext">The tracked context type.</typeparam>
internal sealed class LiveContextTracking<TContext>
    where TContext : DbContext
{
    public LiveContextTracking(LiveQueriesEntityFrameworkOptions options, LiveWriteTracker tracker)
    {
        var catalog = new LiveTableCatalog(options.MaxCachedCommands);

        Interceptors =
        [
            new LiveCommandInterceptor(catalog, tracker),
            new LiveTransactionInterceptor(tracker),
            new LiveSaveInterceptor(catalog, tracker),
        ];
    }

    public IReadOnlyList<IInterceptor> Interceptors { get; }
}
