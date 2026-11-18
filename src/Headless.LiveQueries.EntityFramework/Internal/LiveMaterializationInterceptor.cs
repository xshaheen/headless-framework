// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Remembers, for each entity instance EF Core builds from a tracking query, the stored row it was built from. An update or
/// a delete of that instance is then known to reach a row with those values in the columns fixed at insert, which
/// the instance's own properties cannot prove: the application may have built it, or overwritten them while it was
/// detached.
/// </summary>
/// <remarks>
/// One instance serves every context. EF Core builds a set of internal services for each distinct singleton
/// interceptor, so an instance per host would multiply those sets.
/// </remarks>
internal sealed class LiveMaterializationInterceptor : IMaterializationInterceptor
{
    public static readonly LiveMaterializationInterceptor Instance = new();

    // Weak on the instance: the row is dropped with the entity it describes.
    private static readonly ConditionalWeakTable<object, LiveRow> _Read = [];

    private LiveMaterializationInterceptor() { }

    /// <summary>Returns the row <paramref name="entity" /> was read from, when EF Core read it and it is needed.</summary>
    public static LiveRow? Find(object entity) => _Read.TryGetValue(entity, out var row) ? row : null;

    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        // An instance the context does not track is saved only after the application attaches it, and nothing
        // says it was left untouched until then; it is matched on its key alone, like any attached instance.
        if (materializationData.QueryTrackingBehavior is not QueryTrackingBehavior.TrackAll)
        {
            return entity;
        }

        try
        {
            var layout = LiveRowLayout.For(materializationData.EntityType);

            // Asked first: passing the reader boxes the materialization data, for every entity of every query.
            if (
                layout.RemembersMaterialized && layout.ReadMaterialized(materializationData.GetPropertyValue) is { } row
            )
            {
                _Read.AddOrUpdate(entity, row);
            }
        }
#pragma warning disable CA1031, ERP022 // The fix would make it worse: tracking must not fail the application's query. Without a remembered row the instance is matched on its key alone, which re-runs more and misses nothing.
        catch (Exception)
        {
            _Read.Remove(entity);
        }
#pragma warning restore CA1031, ERP022

        return entity;
    }
}
