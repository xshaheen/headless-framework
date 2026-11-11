// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Which properties of an entity type a saved row can be matched on, and whether rows of the type can be matched at
/// all. Only a value that is certain to equal the stored one is used: a primary-key property, or a property EF Core
/// refuses to update, of a type whose equality is the same in memory and in every database.
/// </summary>
internal sealed class LiveRowLayout
{
    // Process-wide and keyed weakly on the entity type, so every context of a model shares one layout and a model
    // that is collected takes its layouts with it.
    private static readonly ConditionalWeakTable<IEntityType, LiveRowLayout> _Layouts = [];

    private readonly IProperty[] _properties;
    private readonly int _keyCount;

    // Whether the properties above hold the whole primary key, so two rows that agree on them are one row.
    private readonly bool _identifiesRow;

    private LiveRowLayout(IEntityType entityType)
    {
        EntityType = entityType;
        Key = _FindOwnTable(entityType);

        var key = entityType.FindPrimaryKey()?.Properties ?? [];
        IProperty[] trustedKey = [.. key.Where(_IsTrusted)];

        _properties = Key is null
            ? []
            : [.. trustedKey, .. entityType.GetProperties().Where(x => !x.IsPrimaryKey() && _IsTrusted(x))];
        _keyCount = Key is null ? 0 : trustedKey.Length;
        _identifiesRow = _keyCount > 0 && _keyCount == key.Count;
    }

    public IEntityType EntityType { get; }

    /// <summary>The dependency key of the type's table, or <see langword="null" /> when rows cannot be matched.</summary>
    public string? Key { get; }

    /// <summary>Whether a filter on this type can narrow a query and a saved row of it can be matched.</summary>
    public bool IsMatchable => _properties.Length > 0;

    public static LiveRowLayout For(IEntityType entityType)
    {
        return _Layouts.GetValue(entityType, static type => new LiveRowLayout(type));
    }

    /// <summary>Returns the position of <paramref name="property" /> in a row, or -1 when rows are not matched on it.</summary>
    public int IndexOf(IProperty property) => Array.IndexOf(_properties, property);

    /// <summary>Reads a row EF Core has just inserted, whose every value is the stored one.</summary>
    public LiveRow ReadInserted(EntityEntry entry)
    {
        var values = new object?[_properties.Length];

        for (var index = 0; index < values.Length; index++)
        {
            values[index] = entry.Property(_properties[index]).CurrentValue;
        }

        return new LiveRow(EntityType, values);
    }

    /// <summary>
    /// Reads the row an update or a delete reaches. The key is always the stored row's, because the statement finds
    /// the row by it. The other values are known only when EF Core read this very instance from the database, as
    /// this row; an instance the application built or changed while detached may say anything.
    /// </summary>
    public LiveRow ReadStored(EntityEntry entry)
    {
        var values = new object?[_properties.Length];

        for (var index = 0; index < _keyCount; index++)
        {
            values[index] = entry.Property(_properties[index]).CurrentValue;
        }

        if (_keyCount == values.Length)
        {
            return new LiveRow(EntityType, values);
        }

        if (_identifiesRow && LiveMaterializationInterceptor.Find(entry.Entity) is { } read && _IsSameRow(read, values))
        {
            return read;
        }

        Array.Fill(values, LiveRow.Unknown, _keyCount, values.Length - _keyCount);

        return new LiveRow(EntityType, values);
    }

    /// <summary>Gets a value indicating whether <see cref="ReadMaterialized" /> returns a row for this type.</summary>
    public bool RemembersMaterialized => _keyCount != _properties.Length && _identifiesRow;

    /// <summary>
    /// Reads the row an instance was materialized from, or returns <see langword="null" /> when it would never be
    /// used: the type has no column fixed at insert, or part of its key is of a type rows are not matched on, so a
    /// later save could not tell whether the instance still names the row it was read from.
    /// </summary>
    public LiveRow? ReadMaterialized(Func<IProperty, object?> read)
    {
        if (!RemembersMaterialized)
        {
            return null;
        }

        var values = new object?[_properties.Length];

        for (var index = 0; index < values.Length; index++)
        {
            values[index] = read(_properties[index]);
        }

        return new LiveRow(EntityType, values);
    }

    private bool _IsSameRow(LiveRow read, object?[] key)
    {
        if (!ReferenceEquals(read.EntityType, EntityType))
        {
            return false;
        }

        for (var index = 0; index < _keyCount; index++)
        {
            if (!Equals(read[index], key[index]))
            {
                return false;
            }
        }

        return true;
    }

    // A row can be matched only when its table holds rows of this one entity type and a query over the type reads
    // nothing but that table: no hierarchy, no second table, no table shared with another type, and no navigation
    // EF Core loads with every query, which would put a second row into each result.
    private static string? _FindOwnTable(IEntityType entityType)
    {
        if (
            entityType.BaseType is not null
            || entityType.GetDirectlyDerivedTypes().Any()
            || entityType.HasSharedClrType
            || entityType.IsOwned()
            || entityType.GetNavigations().Any(x => x.IsEagerLoaded || x.TargetEntityType.IsOwned())
            || entityType.GetSkipNavigations().Any(x => x.IsEagerLoaded)
        )
        {
            return null;
        }

        return
            entityType.GetTableMappings().ToArray() is [var mapping]
            && mapping.Table.EntityTypeMappings.Take(2).Count() == 1
            ? LiveTableKey.Create(mapping.Table.Name, mapping.Table.Schema)
            : null;
    }

    private static bool _IsTrusted(IProperty property)
    {
        return (property.IsPrimaryKey() || property.GetAfterSaveBehavior() is PropertySaveBehavior.Throw)
            && !property.ValueGenerated.HasFlag(ValueGenerated.OnUpdate)
            // A converter may store another value than the one the entity holds, and a query sends the stored one.
            && property.GetValueConverter() is null
            && LiveRowValue.IsComparable(property.ClrType);
    }
}
