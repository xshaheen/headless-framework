// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Domain;
using Headless.Linq;
using Headless.Orm.EntityFramework.Configurations;
using Headless.Primitives;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

public static class EntityTypeBuilderExtensions
{
    /// <summary>
    /// It allows you to add a query filter to an entity type, combining it with
    /// any existing query filter if any using a logical AND operation
    /// it differs from <see cref="EntityTypeBuilder{TEntity}.HasQueryFilter(Expression{Func{TEntity, bool}})"/>
    /// because it merges the new filter with the existing one instead of replacing it.
    /// </summary>
    public static EntityTypeBuilder<TEntity> AndHasQueryFilter<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, bool>> filter
    )
        where TEntity : class
    {
        // INTERNAL EF CORE API USAGE
        // -----------------------------------------------------------------------------
        // Required: Access to CoreAnnotationNames.QueryFilter to retrieve existing query
        //   filter from entity metadata. EF Core's public API HasQueryFilter() replaces
        //   filters rather than combining them. We need the annotation name constant to
        //   retrieve the existing filter for AND-combination.
        // Tested with: EF Core 8.x, 9.x, 10.x
        // On EF Core upgrade: Verify CoreAnnotationNames.QueryFilter constant exists
        //   and FindAnnotation() returns the filter expression with this key.
        // Alternative: None available in public API as of EF Core 10.0
        // -----------------------------------------------------------------------------
#pragma warning disable EF1001 // Is an internal API
        var queryFilterAnnotation = builder.Metadata.FindAnnotation(CoreAnnotationNames.QueryFilter);
#pragma warning restore EF1001

        if (queryFilterAnnotation is { Value: Expression<Func<TEntity, bool>> existingFilter })
        {
            filter = filter.And(existingFilter);
        }

        return builder.HasQueryFilter(filter);
    }

    /// <inheritdoc cref="AndHasQueryFilter{TEntity}(EntityTypeBuilder{TEntity},Expression{Func{TEntity,bool}})"/>
    public static void AndHasQueryFilter<TEntity>(
        this EntityTypeBuilder builder,
        Expression<Func<TEntity, bool>> filter
    )
        where TEntity : class
    {
        // INTERNAL EF CORE API USAGE
        // -----------------------------------------------------------------------------
        // Required: Access to CoreAnnotationNames.QueryFilter to retrieve existing query
        //   filter from entity metadata. See documentation in generic overload above.
        // Tested with: EF Core 8.x, 9.x, 10.x
        // On EF Core upgrade: Verify CoreAnnotationNames.QueryFilter constant exists
        // Alternative: None available in public API as of EF Core 10.0
        // -----------------------------------------------------------------------------
#pragma warning disable EF1001 // Is an internal API
        var queryFilterAnnotation = builder.Metadata.FindAnnotation(CoreAnnotationNames.QueryFilter);
#pragma warning restore EF1001

        if (queryFilterAnnotation is { Value: Expression<Func<TEntity, bool>> existingFilter })
        {
            filter = filter.And(existingFilter);
        }

        builder.HasQueryFilter(filter);
    }

    /// <summary>
    /// Configures all headless conventions for the given entity type builder.
    /// Applies concurrency stamp, extra properties, delete audit, create audit,
    /// update audit, and suspend audit configurations.
    /// </summary>
    public static void ConfigureHeadlessConvention(this EntityTypeBuilder builder)
    {
        builder.TryConfigureConcurrencyStamp();
        builder.TryConfigureExtraProperties();
        builder.TryConfigureDeleteAudit();
        builder.TryConfigureCreateAudit();
        builder.TryConfigureUpdateAudit();
        builder.TryConfigureSuspendAudit();
    }

    #region Configure ICreateAudit

    public static void ConfigureRequiredCreateAudit<TId, TEntity, TCreator>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, ICreateAudit<TId, TCreator>
        where TCreator : class
    {
        builder.Property(x => x.CreatedById).IsRequired();

        builder
            .HasOne(x => x.CreatedBy)
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }

    public static void ConfigureOptionalCreateAudit<TId, TEntity, TCreator>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, ICreateAudit<TId?, TCreator?>
        where TCreator : class
    {
        builder.Property(x => x.CreatedById).IsRequired(false);

        builder
            .HasOne(x => x.CreatedBy)
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public static void TryConfigureCreateAudit(this EntityTypeBuilder builder, bool isRequired = false)
    {
        if (!builder.Metadata.ClrType.IsAssignableTo<ICreateAudit>())
        {
            return;
        }

        const string dateCreatedName = nameof(ICreateAudit.DateCreated);
        const string createdByIdName = nameof(ICreateAudit<>.CreatedById);
        const string createdByName = nameof(ICreateAudit<,>.CreatedBy);

        builder.Property(dateCreatedName).IsRequired().HasColumnName(dateCreatedName);

        if (
            builder
                .Metadata.ClrType.GetInterfaces()
                .Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(ICreateAudit<,>))
        )
        {
            builder
                .Property(createdByIdName)
                .IsRequired(isRequired)
                .HasColumnName(createdByIdName)
                .HasMaxLength(DomainConstants.IdMaxLength);

            builder
                .HasOne(createdByName)
                .WithMany()
                .HasForeignKey(createdByIdName)
                .IsRequired(isRequired)
                .OnDelete(DeleteBehavior.Restrict);
        }
        else if (
            builder
                .Metadata.ClrType.GetInterfaces()
                .Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(ICreateAudit<>))
        )
        {
            builder
                .Property(createdByIdName)
                .IsRequired(isRequired)
                .HasColumnName(createdByIdName)
                .HasMaxLength(DomainConstants.IdMaxLength);
        }
    }

    public static void ConfigureCreateAudit<TEntity>(this EntityTypeBuilder<TEntity> b)
        where TEntity : class, ICreateAudit
    {
        b.Cast<EntityTypeBuilder>().TryConfigureCreateAudit();
    }

    #endregion

    #region Configure IUpdateAudit

    public static void TryConfigureUpdateAudit(this EntityTypeBuilder builder)
    {
        if (!builder.Metadata.ClrType.IsAssignableTo<IUpdateAudit>())
        {
            return;
        }

        const string dateUpdatedName = nameof(IUpdateAudit.DateUpdated);
        const string updatedByIdName = nameof(IUpdateAudit<>.UpdatedById);
        const string updatedByName = nameof(IUpdateAudit<,>.UpdatedBy);

        builder.Property(dateUpdatedName).IsRequired(false).HasColumnName(dateUpdatedName);

        if (
            builder
                .Metadata.ClrType.GetInterfaces()
                .Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IUpdateAudit<,>))
        )
        {
            builder
                .Property(updatedByIdName)
                .IsRequired(false)
                .HasColumnName(updatedByIdName)
                .HasMaxLength(DomainConstants.IdMaxLength);

            builder
                .HasOne(updatedByName)
                .WithMany()
                .HasForeignKey(updatedByIdName)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        }
        else if (
            builder
                .Metadata.ClrType.GetInterfaces()
                .Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IUpdateAudit<>))
        )
        {
            builder
                .Property(updatedByIdName)
                .IsRequired(false)
                .HasColumnName(updatedByIdName)
                .HasMaxLength(DomainConstants.IdMaxLength);
        }
    }

    public static void ConfigureUpdateAudit<TEntity>(this EntityTypeBuilder<TEntity> b)
        where TEntity : class, IDeleteAudit
    {
        b.Cast<EntityTypeBuilder>().TryConfigureUpdateAudit();
    }

    public static void ConfigureUpdateAudit<TId, TEntity, TCreator>(this EntityTypeBuilder<TEntity> builder)
        where TId : struct, IEquatable<TId>
        where TEntity : class, IUpdateAudit<TId, TCreator>
        where TCreator : class
    {
        builder.Property(x => x.UpdatedById).IsRequired(false);
        builder.Property(x => x.DateUpdated).IsRequired(false);

        builder
            .HasOne(x => x.UpdatedBy)
            .WithMany()
            .HasForeignKey(x => x.UpdatedById)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }

    #endregion

    #region Configure IDeletedAudit

    public static void TryConfigureDeleteAudit(this EntityTypeBuilder builder)
    {
        if (!builder.Metadata.ClrType.IsAssignableTo<IDeleteAudit>())
        {
            return;
        }

        const string isDeletedName = nameof(IDeleteAudit.IsDeleted);
        const string dateDeletedName = nameof(IDeleteAudit.DateDeleted);
        const string dateRestoredName = nameof(IDeleteAudit.DateRestored);
        const string deletedByIdName = nameof(IDeleteAudit<>.DeletedById);
        const string restoredByIdName = nameof(IDeleteAudit<>.RestoredById);
        const string deletedByName = nameof(IDeleteAudit<,>.DeletedBy);
        const string restoredByName = nameof(IDeleteAudit<,>.RestoredBy);

        builder.Property(isDeletedName).IsRequired().HasDefaultValue(value: false).HasColumnName(isDeletedName);
        builder.Property(dateDeletedName).IsRequired(false).HasColumnName(dateDeletedName);
        builder.Property(dateRestoredName).IsRequired(false).HasColumnName(dateRestoredName);

        if (
            builder
                .Metadata.ClrType.GetInterfaces()
                .Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IDeleteAudit<,>))
        )
        {
            builder
                .Property(deletedByIdName)
                .IsRequired(false)
                .HasColumnName(deletedByIdName)
                .HasMaxLength(DomainConstants.IdMaxLength);

            builder
                .Property(restoredByIdName)
                .IsRequired(false)
                .HasColumnName(restoredByIdName)
                .HasMaxLength(DomainConstants.IdMaxLength);

            builder
                .HasOne(deletedByName)
                .WithMany()
                .HasForeignKey(deletedByIdName)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasOne(restoredByName)
                .WithMany()
                .HasForeignKey(restoredByIdName)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        }
        else if (
            builder
                .Metadata.ClrType.GetInterfaces()
                .Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IDeleteAudit<>))
        )
        {
            builder
                .Property(deletedByIdName)
                .IsRequired(false)
                .HasColumnName(deletedByIdName)
                .HasMaxLength(DomainConstants.IdMaxLength);

            builder
                .Property(restoredByIdName)
                .IsRequired(false)
                .HasColumnName(restoredByIdName)
                .HasMaxLength(DomainConstants.IdMaxLength);
        }
    }

    public static void ConfigureDeleteAudit<TEntity>(this EntityTypeBuilder<TEntity> b)
        where TEntity : class, IDeleteAudit
    {
        b.Cast<EntityTypeBuilder>().TryConfigureDeleteAudit();
    }

    public static void ConfigureDeleteAudit<TId, TEntity, TCreator>(this EntityTypeBuilder<TEntity> builder)
        where TId : struct, IEquatable<TId>
        where TEntity : class, IDeleteAudit<TId, TCreator>
        where TCreator : class
    {
        builder.Property(x => x.DeletedById).IsRequired(false);
        builder.Property(x => x.DateDeleted).IsRequired(false);

        builder
            .HasOne(x => x.DeletedBy)
            .WithMany()
            .HasForeignKey(x => x.DeletedById)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }

    #endregion

    #region Configure ISuspendAudit

    public static void TryConfigureSuspendAudit(this EntityTypeBuilder builder)
    {
        if (!builder.Metadata.ClrType.IsAssignableTo<ISuspendAudit>())
        {
            return;
        }

        const string isSuspended = nameof(ISuspendAudit.IsSuspended);
        const string dateSuspendedName = nameof(ISuspendAudit.DateSuspended);
        const string dateRestoredName = nameof(ISuspendAudit.DateRestored);
        const string suspendedByIdName = nameof(ISuspendAudit<>.SuspendedById);
        const string restoredByIdName = nameof(ISuspendAudit<>.RestoredById);
        const string suspendedByName = nameof(ISuspendAudit<,>.SuspendedBy);
        const string restoredByName = nameof(ISuspendAudit<,>.RestoredBy);

        builder.Property(isSuspended).IsRequired().HasDefaultValue(value: false).HasColumnName(isSuspended);
        builder.Property(dateSuspendedName).IsRequired(false).HasColumnName(dateSuspendedName);
        builder.Property(dateRestoredName).IsRequired(false).HasColumnName(dateRestoredName);

        if (
            builder
                .Metadata.ClrType.GetInterfaces()
                .Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(ISuspendAudit<,>))
        )
        {
            builder
                .Property(suspendedByIdName)
                .IsRequired(false)
                .HasColumnName(suspendedByIdName)
                .HasMaxLength(DomainConstants.IdMaxLength);

            builder
                .Property(restoredByIdName)
                .IsRequired(false)
                .HasColumnName(restoredByIdName)
                .HasMaxLength(DomainConstants.IdMaxLength);

            builder
                .HasOne(suspendedByName)
                .WithMany()
                .HasForeignKey(suspendedByIdName)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasOne(restoredByName)
                .WithMany()
                .HasForeignKey(restoredByIdName)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        }
        else if (
            builder
                .Metadata.ClrType.GetInterfaces()
                .Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(ISuspendAudit<>))
        )
        {
            builder
                .Property(suspendedByIdName)
                .IsRequired(false)
                .HasColumnName(suspendedByIdName)
                .HasMaxLength(DomainConstants.IdMaxLength);

            builder
                .Property(restoredByIdName)
                .IsRequired(false)
                .HasColumnName(restoredByIdName)
                .HasMaxLength(DomainConstants.IdMaxLength);
        }
    }

    public static void ConfigureSuspendAudit<T>(this EntityTypeBuilder<T> builder)
        where T : class, ISuspendAudit
    {
        builder.Cast<EntityTypeBuilder>().TryConfigureSuspendAudit();
    }

    #endregion

    #region Configure IHasConcurrencyStamp

    public static void TryConfigureConcurrencyStamp(this EntityTypeBuilder b)
    {
        if (b.Metadata.ClrType.IsAssignableTo<IHasConcurrencyStamp>())
        {
            b.Property(nameof(IHasConcurrencyStamp.ConcurrencyStamp))
                .IsConcurrencyToken()
                .HasMaxLength(DomainConstants.ConcurrencyStampMaxLength)
                .HasColumnName(nameof(IHasConcurrencyStamp.ConcurrencyStamp));
        }
    }

    public static void ConfigureConcurrencyStamp<T>(this EntityTypeBuilder<T> b)
        where T : class, IHasConcurrencyStamp
    {
        b.Cast<EntityTypeBuilder>().TryConfigureConcurrencyStamp();
    }

    #endregion

    #region Configure IHasExtraProperties

    public static void TryConfigureExtraProperties(this EntityTypeBuilder b)
    {
        if (b.Metadata.ClrType.IsAssignableTo<IHasExtraProperties>())
        {
            b.Property<ExtraProperties>(nameof(IHasExtraProperties.ExtraProperties))
                .HasColumnName(nameof(IHasExtraProperties.ExtraProperties))
                .HasConversion(new ExtraPropertiesValueConverter())
                .Metadata.SetValueComparer(new ExtraPropertiesValueComparer());
        }
    }

    public static void ConfigureExtraProperties<T>(this EntityTypeBuilder<T> b)
        where T : class, IHasExtraProperties
    {
        b.Cast<EntityTypeBuilder>().TryConfigureExtraProperties();
    }

    #endregion
}
