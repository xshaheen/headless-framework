// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Headless.EntityFramework;

/// <summary>
/// The tracked principals of one save snapshot, indexed by ownership key, so resolving the owner of an owned entry looks
/// its key up instead of scanning every tracked entry: a save of n owners with owned types otherwise costs n² key
/// comparisons.
/// </summary>
/// <remarks>
/// Each index is built on first use and holds the key values the snapshot's principals had then. The save-entry
/// processors stamp audit, tenant, and concurrency values, never keys, so those values hold for the snapshot's pass.
/// </remarks>
internal sealed class HeadlessOwnerIndex(IReadOnlyList<EntityEntry> tracked)
{
    private readonly Dictionary<(IForeignKey Ownership, bool Original), Dictionary<object[], EntityEntry?>> _indexes =
    [];

    /// <summary>The snapshot this index was built from.</summary>
    public IReadOnlyList<EntityEntry> Tracked { get; } = tracked;

    /// <summary>
    /// The one tracked principal whose ownership key equals the dependent's, read from original or current values;
    /// <see langword="null"/> when none or several match, or when either side's key is null or changed since it was
    /// loaded.
    /// </summary>
    public EntityEntry? FindPrincipal(EntityEntry dependent, IForeignKey ownership, bool original)
    {
        var properties = ownership.Properties;
        var values = original ? dependent.OriginalValues : dependent.CurrentValues;
        var loaded = dependent.State == EntityState.Added ? null : dependent.OriginalValues;
        var current = loaded is null ? null : dependent.CurrentValues;
        var key = new object[properties.Count];

        for (var i = 0; i < properties.Count; i++)
        {
            var comparer = ownership.PrincipalKey.Properties[i].GetKeyValueComparer();

            if (
                values[properties[i]] is not { } value
                || (loaded is not null && !comparer.Equals(loaded[properties[i]], current![properties[i]]))
            )
            {
                return null;
            }

            key[i] = value;
        }

        return _IndexFor(ownership, original).GetValueOrDefault(key);
    }

    private Dictionary<object[], EntityEntry?> _IndexFor(IForeignKey ownership, bool original)
    {
        if (_indexes.TryGetValue((ownership, original), out var index))
        {
            return index;
        }

        var keyProperties = ownership.PrincipalKey.Properties;
        index = new Dictionary<object[], EntityEntry?>(new KeyComparer(keyProperties));

        foreach (var candidate in Tracked)
        {
            if (
                ownership.PrincipalEntityType.IsAssignableFrom(candidate.Metadata)
                && _ReadKey(candidate, keyProperties, original) is { } key
                && !index.TryAdd(key, candidate)
            )
            {
                // A key two tracked principals share identifies neither.
                index[key] = null;
            }
        }

        _indexes.Add((ownership, original), index);

        return index;
    }

    private static object[]? _ReadKey(EntityEntry candidate, IReadOnlyList<IProperty> keyProperties, bool original)
    {
        var values = original ? candidate.OriginalValues : candidate.CurrentValues;
        var loaded = candidate.State == EntityState.Added ? null : candidate.OriginalValues;
        var current = loaded is null ? null : candidate.CurrentValues;
        var key = new object[keyProperties.Count];

        for (var i = 0; i < keyProperties.Count; i++)
        {
            var property = keyProperties[i];

            if (
                values[property] is not { } value
                || (loaded is not null && !property.GetKeyValueComparer().Equals(loaded[property], current![property]))
            )
            {
                return null;
            }

            key[i] = value;
        }

        return key;
    }

    private sealed class KeyComparer(IReadOnlyList<IProperty> keyProperties) : IEqualityComparer<object[]>
    {
        public bool Equals(object[]? x, object[]? y)
        {
            if (x is null || y is null)
            {
                return ReferenceEquals(x, y);
            }

            for (var i = 0; i < x.Length; i++)
            {
                if (!keyProperties[i].GetKeyValueComparer().Equals(x[i], y[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(object[] obj)
        {
            var hash = default(HashCode);

            for (var i = 0; i < obj.Length; i++)
            {
                hash.Add(keyProperties[i].GetKeyValueComparer().GetHashCode(obj[i]));
            }

            return hash.ToHashCode();
        }
    }
}
