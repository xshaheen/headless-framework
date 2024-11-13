// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Framework.Kernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Framework.Orm.EntityFramework.Configurations;

public static class CurrencyConfiguration
{
    public static void HasComplexCurrency<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, Currency?>> propertyExpression,
        string amountColumnName = "Amount",
        string codeColumnName = "Currency",
        int codeMaxLength = 3,
        bool isRequired = true
    )
        where TEntity : class
    {
        builder.ComplexProperty(
            propertyExpression,
            b =>
            {
                b.Property(x => x!.CurrencyCode)
                    .IsRequired(isRequired)
                    .HasColumnName(codeColumnName)
                    .HasMaxLength(codeMaxLength);

                b.Property(x => x!.Amount).HasPrecision(32, 10).IsRequired(isRequired).HasColumnName(amountColumnName);
            }
        );
    }

    public static void HasComplexCurrency<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        string propertyName,
        string amountColumnName = "Amount",
        string codeColumnName = "Currency",
        int codeMaxLength = 3,
        bool isRequired = true
    )
        where TEntity : class
    {
        builder.ComplexProperty(
            propertyName,
            b =>
            {
                b.Property(nameof(Currency.CurrencyCode))
                    .IsRequired(isRequired)
                    .HasColumnName(codeColumnName)
                    .HasMaxLength(codeMaxLength);

                b.Property(nameof(Currency.Amount))
                    .HasPrecision(32, 10)
                    .IsRequired(isRequired)
                    .HasColumnName(amountColumnName);
            }
        );
    }

    public static void OwnsCurrency<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, Currency?>> propertyExpression,
        string amountColumnName = "Amount",
        string codeColumnName = "Currency",
        int codeMaxLength = 3,
        bool isRequired = true
    )
        where TEntity : class
    {
        builder.OwnsOne(
            propertyExpression,
            b =>
            {
                b.Property(x => x!.CurrencyCode)
                    .IsRequired(isRequired)
                    .HasColumnName(codeColumnName)
                    .HasMaxLength(codeMaxLength);

                b.Property(x => x!.Amount).HasPrecision(32, 10).IsRequired(isRequired).HasColumnName(amountColumnName);
            }
        );
    }

    public static void OwnsCurrency<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Type entityType,
        string propertyName,
        string amountColumnName = "Amount",
        string codeColumnName = "Currency",
        int codeMaxLength = 3,
        bool isRequired = true
    )
        where TEntity : class
    {
        builder.OwnsOne(
            entityType,
            propertyName,
            b =>
            {
                b.Property(nameof(Currency.CurrencyCode))
                    .IsRequired(isRequired)
                    .HasColumnName(codeColumnName)
                    .HasMaxLength(codeMaxLength);

                b.Property(nameof(Currency.Amount))
                    .HasPrecision(32, 10)
                    .IsRequired(isRequired)
                    .HasColumnName(amountColumnName);
            }
        );
    }
}
