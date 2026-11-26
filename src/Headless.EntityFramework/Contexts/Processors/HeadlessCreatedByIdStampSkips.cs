// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Headless.EntityFramework;

/// <summary>Entities marked by <see cref="HeadlessEntityEntryExtensions.SkipCreatedByIdStamp{TEntry}"/>.</summary>
internal static class HeadlessCreatedByIdStampSkips
{
    // Keyed weakly by the entity instance: the mark lives exactly as long as the entity, with no cleanup step for a
    // save that fails, retries, or never happens.
    private static readonly ConditionalWeakTable<object, object> _Marked = [];
    private static readonly object _Mark = new();

    public static void Add(object entity) => _Marked.AddOrUpdate(entity, _Mark);

    public static bool Contains(object entity) => _Marked.TryGetValue(entity, out _);
}
