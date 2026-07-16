// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel.DataAnnotations.Schema;
using Headless.Domain;
using Headless.EntityFramework.Contexts;
using Headless.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

[PublicAPI]
public static class MutableEntityTypeExtensions
{
    /// <summary>
    /// Makes the application-side generator the single source of an <see cref="IEntity{TId}.Id"/> of type
    /// <see cref="Guid"/>: maps the key <c>ValueGenerated.Never</c> (no store identity column, no provider coupling)
    /// and attaches the framework value generator that produces the key at add time via <c>IGuidGenerator</c>.
    /// String keys are marked Never only (the caller or the opt-in <c>GenerateCompactGuidForStringPrimaryKeys</c>
    /// selector supplies the value). Numeric keys are left to the consumer's EF/database/provider configuration.
    /// No-op for other key types, an unmapped <c>Id</c>, or a key that opts into store generation via
    /// <c>[DatabaseGenerated]</c>.
    /// </summary>
    /// <remarks>
    /// A Headless Guid key must never be database-generated: a random store-generated Guid clusters poorly, and any
    /// store-generated key forces a database round-trip to learn the id and couples it to one provider. Letting EF
    /// Core's value generator stamp it client-side instead makes the id known before <c>SaveChanges</c> (usable for
    /// foreign keys, outbox, and domain events in the same unit of work) and provider-portable. EF Core runs the
    /// generator on the transition into the <c>Added</c> state (whether via <c>Add</c>, a direct state change, or
    /// attach-then-promote), so the key is always set before it reaches the save pipeline. Keys outside
    /// <see cref="Guid"/> / <see cref="string"/> are left to their own configuration.
    /// </remarks>
    public static void ConfigureHeadlessValueGenerated(this IMutableEntityType entityType)
    {
        var idProperty = entityType.FindProperty(nameof(IEntity<>.Id));

        if (idProperty is null || _OptsIntoStoreGeneration(idProperty))
        {
            return;
        }

        var clrType = entityType.ClrType;

        if (clrType.IsAssignableTo(typeof(IEntity<Guid>)))
        {
            idProperty.ValueGenerated = ValueGenerated.Never;
            idProperty.SetValueGeneratorFactory(static (_, _) => new HeadlessGuidIdValueGenerator());
        }
        else if (clrType.IsAssignableTo(typeof(IEntity<string>)))
        {
            // String keys are marked Never so EF Core does not infer store generation; the value is supplied by
            // the caller or the opt-in GenerateCompactGuidForStringPrimaryKeys selector — not a built-in generator.
            idProperty.ValueGenerated = ValueGenerated.Never;
        }
    }

    // A key explicitly mapped to store generation via [DatabaseGenerated(Identity|Computed)] is the consumer
    // opting out of framework key ownership; leave EF Core's own configuration untouched. [DatabaseGenerated(None)]
    // (or no attribute) means the client supplies the key, which is exactly what the framework generators do.
    private static bool _OptsIntoStoreGeneration(IMutableProperty idProperty)
    {
        return idProperty.PropertyInfo?.GetFirstOrDefaultAttribute<DatabaseGeneratedAttribute>()
            is { DatabaseGeneratedOption: not DatabaseGeneratedOption.None };
    }
}
