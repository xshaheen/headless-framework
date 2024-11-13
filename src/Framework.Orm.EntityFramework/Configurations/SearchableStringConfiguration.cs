// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Framework.Kernel.Primitives;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

public static class SearchableStringConfiguration
{
    public static void HasComplexSearchableString<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, SearchableString?>> propertyExpression,
        string valueColumnName = "Value",
        string normalizedColumnName = "Normalized",
        int valueMaxLength = 1000,
        int normalizedMaxLength = 2000,
        bool isRequired = true
    )
        where TEntity : class
    {
        builder.ComplexProperty(
            propertyExpression,
            b =>
            {
                b.Property(x => x!.Value)
                    .IsRequired(isRequired)
                    .HasColumnName(valueColumnName)
                    .HasMaxLength(valueMaxLength);

                b.Property(x => x!.Normalized)
                    .IsRequired(isRequired)
                    .HasColumnName(normalizedColumnName)
                    .HasMaxLength(normalizedMaxLength);
            }
        );
    }

    public static void HasComplexSearchableString<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        string propertyName,
        string valueColumnName = "Value",
        string normalizedColumnName = "Normalized",
        int maxValueLength = 1000,
        int maxNormalizedLength = 2000,
        bool isRequired = true
    )
        where TEntity : class
    {
        builder.ComplexProperty(
            propertyName,
            b =>
            {
                b.Property(nameof(PhoneNumber.CountryCode))
                    .IsRequired(isRequired)
                    .HasColumnName(valueColumnName)
                    .HasMaxLength(maxValueLength);

                b.Property(nameof(PhoneNumber.Number))
                    .IsRequired(isRequired)
                    .HasColumnName(normalizedColumnName)
                    .HasMaxLength(maxNormalizedLength);
            }
        );
    }

    public static void OwnsSearchableString<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, SearchableString?>> propertyExpression,
        string valueColumnName = "Value",
        string normalizedColumnName = "Normalized",
        int maxValueLength = 1000,
        int maxNormalizedLength = 2000,
        bool isRequired = true
    )
        where TEntity : class
    {
        builder.OwnsOne(
            propertyExpression,
            b =>
            {
                b.Property(x => x.Value)
                    .IsRequired(isRequired)
                    .HasColumnName(valueColumnName)
                    .HasMaxLength(maxValueLength);

                b.Property(x => x.Normalized)
                    .IsRequired(isRequired)
                    .HasColumnName(normalizedColumnName)
                    .HasMaxLength(maxNormalizedLength);
            }
        );
    }

    public static void OwnsSearchableString(
        this EntityTypeBuilder builder,
        Type entityType,
        string propertyName,
        string valueColumnName = "Value",
        string normalizedColumnName = "Normalized",
        int maxValueLength = 1000,
        int maxNormalizedLength = 2000,
        bool isRequired = true
    )
    {
        builder.OwnsOne(
            entityType,
            propertyName,
            b =>
            {
                b.Property(nameof(SearchableString.Value))
                    .IsRequired(isRequired)
                    .HasColumnName(valueColumnName)
                    .HasMaxLength(maxValueLength);

                b.Property(nameof(SearchableString.Normalized))
                    .IsRequired(isRequired)
                    .HasColumnName(normalizedColumnName)
                    .HasMaxLength(maxNormalizedLength);
            }
        );
    }
}
