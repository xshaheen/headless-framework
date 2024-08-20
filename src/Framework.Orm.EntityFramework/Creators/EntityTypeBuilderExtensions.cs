using Framework.BuildingBlocks.Domains;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Framework.Orm.EntityFramework.Creators;

public static class EntityTypeBuilderExtensions
{
    public static void HasRequiredCreateAuditedGuidUser<TId, TEntity, TCreator>(this EntityTypeBuilder<TEntity> builder)
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

    public static void HasOptionalCreateAuditedGuidUser<TId, TEntity, TCreator>(this EntityTypeBuilder<TEntity> builder)
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

    public static void HasUpdateAuditGuidUser<TId, TEntity, TCreator>(this EntityTypeBuilder<TEntity> builder)
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

    public static void HasDeleteAuditGuidUser<TId, TEntity, TCreator>(this EntityTypeBuilder<TEntity> builder)
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
}
