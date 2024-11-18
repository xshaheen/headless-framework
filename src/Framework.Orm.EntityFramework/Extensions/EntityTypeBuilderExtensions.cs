// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domains;
using Framework.Orm.EntityFramework.Configurations;
using Framework.Primitives;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

public static class EntityTypeBuilderExtensions
{
    public static void ConfigureFrameworkConvention(this EntityTypeBuilder b)
    {
        b.TryConfigureConcurrencyStamp();
        b.TryConfigureExtraProperties();
        b.TryConfigureDeleteAudit();
        b.TryConfigureCreateAudit();
        b.TryConfigureUpdateAudit();
        b.TryConfigureSuspendAudit();
        b.TryConfigureDeleteAudit();
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
        const string createdByIdName = nameof(ICreateAudit<string>.CreatedById);
        const string createdByName = nameof(ICreateAudit<string, object>.CreatedBy);

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
        b.As<EntityTypeBuilder>().TryConfigureCreateAudit();
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
        const string updatedByIdName = nameof(IUpdateAudit<string>.UpdatedById);
        const string updatedByName = nameof(IUpdateAudit<string, object>.UpdatedBy);

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
        b.As<EntityTypeBuilder>().TryConfigureUpdateAudit();
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
        const string deletedByIdName = nameof(IDeleteAudit<string>.DeletedById);
        const string restoredByIdName = nameof(IDeleteAudit<string>.RestoredById);
        const string deletedByName = nameof(IDeleteAudit<string, object>.DeletedBy);
        const string restoredByName = nameof(IDeleteAudit<string, object>.RestoredBy);

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
        b.As<EntityTypeBuilder>().TryConfigureDeleteAudit();
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
        const string suspendedByIdName = nameof(ISuspendAudit<string>.SuspendedById);
        const string restoredByIdName = nameof(ISuspendAudit<string>.RestoredById);
        const string suspendedByName = nameof(ISuspendAudit<string, object>.SuspendedBy);
        const string restoredByName = nameof(ISuspendAudit<string, object>.RestoredBy);

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

    public static void ConfigureSuspendAudit<T>(this EntityTypeBuilder<T> b)
        where T : class, ISuspendAudit
    {
        b.As<EntityTypeBuilder>().TryConfigureSuspendAudit();
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
        b.As<EntityTypeBuilder>().TryConfigureConcurrencyStamp();
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
        b.As<EntityTypeBuilder>().TryConfigureExtraProperties();
    }

    #endregion
}
