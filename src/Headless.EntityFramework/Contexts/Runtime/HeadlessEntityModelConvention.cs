// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Domain;
using Headless.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.EntityFramework;

/// <summary>
/// Applies the Headless entity mapping: the audit, concurrency-stamp, and extra-properties columns, the audit actor
/// references, client-side Guid keys, <see cref="DateTime"/> normalization, and the soft-delete filter.
/// </summary>
/// <remarks>
/// As a convention it reaches every entity type, including one a context first introduces with
/// <c>modelBuilder.Entity&lt;T&gt;()</c> after <c>base.OnModelCreating</c>. Everything is set through the convention
/// builders, so a facet the consumer configured explicitly keeps the consumer's value, whether that configuration came
/// before or after the base call. Column facets, keys, converters, and filters are applied when the model is finalized,
/// after all configuration has run, so a data annotation also keeps its value.
/// </remarks>
internal sealed class HeadlessEntityModelConvention : IEntityTypeAddedConvention, IModelFinalizingConvention
{
    private static readonly NormalizeDateTimeValueConverter _DateTimeConverter = new();
    private static readonly NullableNormalizeDateTimeValueConverter _NullableDateTimeConverter = new();

    // Relationships are mapped as each entity type enters the model, not at finalization: relationship discovery has
    // stopped reacting by then, so it would still report the navigations it found ambiguous and fail validation.
    public void ProcessEntityTypeAdded(
        IConventionEntityTypeBuilder entityTypeBuilder,
        IConventionContext<IConventionEntityTypeBuilder> context
    )
    {
        var type = entityTypeBuilder.Metadata;
        if (type.IsOwned() || !type.ClrType.IsAssignableTo<IEntity>())
        {
            return;
        }

        if (_Implements(type.ClrType, typeof(ICreateAudit<,>)))
        {
            _ActorReference(entityTypeBuilder, nameof(ICreateAudit<,>.CreatedBy), nameof(ICreateAudit<>.CreatedById));
        }

        if (_Implements(type.ClrType, typeof(IUpdateAudit<,>)))
        {
            _ActorReference(entityTypeBuilder, nameof(IUpdateAudit<,>.UpdatedBy), nameof(IUpdateAudit<>.UpdatedById));
        }

        if (_Implements(type.ClrType, typeof(IDeleteAudit<,>)))
        {
            _ActorReference(entityTypeBuilder, nameof(IDeleteAudit<,>.DeletedBy), nameof(IDeleteAudit<>.DeletedById));
            _ActorReference(entityTypeBuilder, nameof(IDeleteAudit<,>.RestoredBy), nameof(IDeleteAudit<>.RestoredById));
        }
    }

    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context
    )
    {
        foreach (var type in modelBuilder.Metadata.GetEntityTypes().ToArray())
        {
            if (type.IsOwned())
            {
                continue;
            }

            if (type.ClrType.IsAssignableTo<IEntity>())
            {
                _ConfigureConcurrencyStamp(type.Builder);
                _ConfigureExtraProperties(type.Builder);
                _ConfigureDeleteAudit(type.Builder);
                _ConfigureCreateAudit(type.Builder);
                _ConfigureUpdateAudit(type.Builder);
                _ConfigureKeyGeneration(type);

                if (type.BaseType is null && type.ClrType.IsAssignableTo<IDeleteAudit>())
                {
                    _ConfigureNotDeletedFilter(type);
                }
            }

            _ConfigureDateTimeConverters(type);
        }
    }

    private static void _ConfigureConcurrencyStamp(IConventionEntityTypeBuilder builder)
    {
        if (!builder.Metadata.ClrType.IsAssignableTo<IHasConcurrencyStamp>())
        {
            return;
        }

        const string name = nameof(IHasConcurrencyStamp.ConcurrencyStamp);
        var property = _Property(builder, name);
        property?.IsConcurrencyToken(concurrencyToken: true);
        property?.HasMaxLength(DomainConstants.ConcurrencyStampMaxLength);
        property?.HasColumnName(name);
    }

    private static void _ConfigureExtraProperties(IConventionEntityTypeBuilder builder)
    {
        if (!builder.Metadata.ClrType.IsAssignableTo<IHasExtraProperties>())
        {
            return;
        }

        const string name = nameof(IHasExtraProperties.ExtraProperties);
        var property = _Property(builder, name);
        property?.HasColumnName(name);
        property?.HasConversion(new ExtraPropertiesValueConverter());
        property?.HasValueComparer(new ExtraPropertiesValueComparer());
    }

    private static void _ConfigureDeleteAudit(IConventionEntityTypeBuilder builder)
    {
        var clrType = builder.Metadata.ClrType;
        if (!clrType.IsAssignableTo<IDeleteAudit>())
        {
            return;
        }

        var isDeleted = _Column(builder, nameof(IDeleteAudit.IsDeleted), required: true);
        isDeleted?.HasDefaultValue(value: false);
        _Column(builder, nameof(IDeleteAudit.DeletedAt), required: false);
        _Column(builder, nameof(IDeleteAudit.RestoredAt), required: false);

        if (_Implements(clrType, typeof(IDeleteAudit<>)) || _Implements(clrType, typeof(IDeleteAudit<,>)))
        {
            _ActorId(builder, nameof(IDeleteAudit<>.DeletedById), required: false);
            _ActorId(builder, nameof(IDeleteAudit<>.RestoredById), required: false);
        }
    }

    private static void _ConfigureCreateAudit(IConventionEntityTypeBuilder builder)
    {
        var clrType = builder.Metadata.ClrType;
        if (!clrType.IsAssignableTo<ICreateAudit>())
        {
            return;
        }

        _Column(builder, nameof(ICreateAudit.CreatedAt), required: true);

        if (_Implements(clrType, typeof(ICreateAudit<>)) || _Implements(clrType, typeof(ICreateAudit<,>)))
        {
            _ActorId(builder, nameof(ICreateAudit<>.CreatedById), required: false);
        }
    }

    private static void _ConfigureUpdateAudit(IConventionEntityTypeBuilder builder)
    {
        var clrType = builder.Metadata.ClrType;
        if (!clrType.IsAssignableTo<IUpdateAudit>())
        {
            return;
        }

        _Column(builder, nameof(IUpdateAudit.UpdatedAt), required: false);

        if (_Implements(clrType, typeof(IUpdateAudit<>)) || _Implements(clrType, typeof(IUpdateAudit<,>)))
        {
            _ActorId(builder, nameof(IUpdateAudit<>.UpdatedById), required: false);
        }
    }

    // Guid keys get the framework's client-side generator and no store generation. Value generation the consumer
    // configured, including [DatabaseGenerated(Identity|Computed)], outranks this convention and keeps its generator.
    private static void _ConfigureKeyGeneration(IConventionEntityType type)
    {
        var id = type.FindDeclaredProperty(nameof(IEntity<>.Id));
        if (id is null)
        {
            return;
        }

        if (type.ClrType.IsAssignableTo(typeof(IEntity<Guid>)))
        {
            if (id.Builder.ValueGenerated(ValueGenerated.Never) is { } idBuilder)
            {
                idBuilder.HasValueGenerator(static (_, _) => new HeadlessGuidIdValueGenerator());
            }
        }
        else if (type.ClrType.IsAssignableTo(typeof(IEntity<string>)))
        {
            // The caller or the opt-in GenerateCompactGuidForStringPrimaryKeys selector supplies a string key, so
            // EF Core must not infer store generation for it.
            id.Builder.ValueGenerated(ValueGenerated.Never);
        }
    }

    private static void _ConfigureNotDeletedFilter(IConventionEntityType type)
    {
        // The filter names the property, not its column: a consumer may map IsDeleted to any column name.
        var parameter = Expression.Parameter(type.ClrType, "entity");
        var isDeleted = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            [typeof(bool)],
            parameter,
            Expression.Constant(nameof(IDeleteAudit.IsDeleted))
        );

        type.Builder.HasQueryFilter(
            HeadlessQueryFilters.NotDeletedFilter,
            Expression.Lambda(Expression.Not(isDeleted), parameter)
        );
    }

    // Declared properties only: a DateTime declared on a TPH-derived type is not among the root's properties, and
    // visiting inherited ones again would configure them twice.
    private static void _ConfigureDateTimeConverters(IConventionEntityType type)
    {
        foreach (var property in type.GetDeclaredProperties())
        {
            if (property.PropertyInfo is not { CanWrite: true } propertyInfo)
            {
                continue;
            }

            ValueConverter? converter =
                propertyInfo.PropertyType == typeof(DateTime) ? _DateTimeConverter
                : propertyInfo.PropertyType == typeof(DateTime?) ? _NullableDateTimeConverter
                : null;

            if (converter is not null)
            {
                property.Builder.HasConversion(converter);
            }
        }
    }

    // A property the consumer ignored explicitly stays ignored: the convention builder returns null for it.
    private static IConventionPropertyBuilder? _Property(IConventionEntityTypeBuilder builder, string name)
    {
        if (builder.Metadata.FindProperty(name) is { } property)
        {
            return property.Builder;
        }

        return builder.Metadata.ClrType.GetProperty(name) is { } member ? builder.Property(member) : null;
    }

    private static IConventionPropertyBuilder? _Column(IConventionEntityTypeBuilder builder, string name, bool required)
    {
        var property = _Property(builder, name);
        property?.IsRequired(required);
        property?.HasColumnName(name);

        return property;
    }

    private static void _ActorId(IConventionEntityTypeBuilder builder, string name, bool required)
    {
        _Column(builder, name, required)?.HasMaxLength(DomainConstants.IdMaxLength);
    }

    // Each actor navigation is a one-way optional reference through its own id property. Mapped at data-annotation
    // rank, like a [ForeignKey] declared by the audit interface: relationship discovery cannot pair it with an inverse
    // collection on the actor type, and fluent configuration of the navigation still wins.
    private static void _ActorReference(
        IConventionEntityTypeBuilder builder,
        string navigationName,
        string foreignKeyName
    )
    {
        if (builder.Metadata.ClrType.GetProperty(navigationName) is not { } navigation)
        {
            return;
        }

        var target = builder.ModelBuilder.Entity(navigation.PropertyType, fromDataAnnotation: true)?.Metadata;
        var relationship = target is null
            ? null
            : builder.HasRelationship(
                target,
                navigation,
                inverseNavigation: null,
                setTargetAsPrincipal: true,
                fromDataAnnotation: true
            );

        if (relationship is null)
        {
            return;
        }

        relationship = relationship.HasForeignKey([foreignKeyName], fromDataAnnotation: true) ?? relationship;
        relationship.IsRequired(required: false, fromDataAnnotation: true);
        relationship.OnDelete(DeleteBehavior.Restrict, fromDataAnnotation: true);
    }

    private static bool _Implements(Type type, Type genericInterface)
    {
        return type.GetInterfaces().Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == genericInterface);
    }
}
