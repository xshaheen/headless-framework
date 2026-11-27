// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Headless.EntityFramework;

/// <summary>Entities marked by the <see cref="HeadlessEntityEntryExtensions"/> <c>Skip…Stamp</c> methods.</summary>
internal static class HeadlessActorStampSkips
{
    // Keyed weakly by the entity instance: a mark lives at most as long as the entity, with no cleanup step for a
    // context that is disposed or a save that never happens.
    private static readonly ConditionalWeakTable<object, StrongBox<int>> _Marked = [];

    public static void Add(object entity, HeadlessActorStamps stamps)
    {
        var box = _Marked.GetValue(entity, static _ => new StrongBox<int>());
        Interlocked.Or(ref box.Value, (int)stamps);
    }

    /// <summary>Whether <paramref name="stamp"/> is marked, leaving the mark in place.</summary>
    public static bool Contains(object entity, HeadlessActorStamps stamp)
    {
        return _Marked.TryGetValue(entity, out var box) && (Volatile.Read(ref box.Value) & (int)stamp) != 0;
    }

    /// <summary>
    /// Whether <paramref name="stamp"/> is marked, removing the mark: an update, delete, or restore mark covers only
    /// the next save that stamps it, whether that save then commits or fails.
    /// </summary>
    public static bool TryConsume(object entity, HeadlessActorStamps stamp)
    {
        if (!_Marked.TryGetValue(entity, out var box))
        {
            return false;
        }

        return (Interlocked.And(ref box.Value, ~(int)stamp) & (int)stamp) != 0;
    }
}
