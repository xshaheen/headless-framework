// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.EntityFramework.Configurations;

[PublicAPI]
public static class CurrencyConfiguration
{
    extension<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        public void HasComplexCurrency(
            Expression<Func<TEntity, Currency?>> propertyExpression,
            string amountColumnName = "Amount",
            string codeColumnName = "Currency",
            int codeMaxLength = 3,
            bool isRequired = true
        )
        {
            builder.ComplexProperty(
                propertyExpression,
                b =>
                {
                    b.Property(x => x.CurrencyCode)
                        .IsRequired(isRequired)
                        .HasColumnName(codeColumnName)
                        .HasMaxLength(codeMaxLength);

                    b.Property(x => x.Amount)
                        .HasPrecision(32, 10)
                        .IsRequired(isRequired)
                        .HasColumnName(amountColumnName);
                }
            );
        }

        public void HasComplexCurrency(
            string propertyName,
            string amountColumnName = "Amount",
            string codeColumnName = "Currency",
            int codeMaxLength = 3,
            bool isRequired = true
        )
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

        public void OwnsCurrency(
            Expression<Func<TEntity, Currency?>> propertyExpression,
            string amountColumnName = "Amount",
            string codeColumnName = "Currency",
            int codeMaxLength = 3,
            bool isRequired = true
        )
        {
            builder.OwnsOne(
                propertyExpression,
                b =>
                {
                    b.Property(x => x.CurrencyCode)
                        .IsRequired(isRequired)
                        .HasColumnName(codeColumnName)
                        .HasMaxLength(codeMaxLength);

                    b.Property(x => x.Amount)
                        .HasPrecision(32, 10)
                        .IsRequired(isRequired)
                        .HasColumnName(amountColumnName);
                }
            );
        }

        public void OwnsCurrency(
            Type entityType,
            string propertyName,
            string amountColumnName = "Amount",
            string codeColumnName = "Currency",
            int codeMaxLength = 3,
            bool isRequired = true
        )
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
}
