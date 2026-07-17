// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Headless.EntityFramework.Configurations;

[PublicAPI]
public static class MoneyConfiguration
{
    extension<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        public void HasComplexMoney(
            Expression<Func<TEntity, Money?>> propertyExpression,
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

        public void HasComplexMoney(
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
                    b.Property(nameof(Money.CurrencyCode))
                        .IsRequired(isRequired)
                        .HasColumnName(codeColumnName)
                        .HasMaxLength(codeMaxLength);

                    b.Property(nameof(Money.Amount))
                        .HasPrecision(32, 10)
                        .IsRequired(isRequired)
                        .HasColumnName(amountColumnName);
                }
            );
        }

        public void OwnsMoney(
            Expression<Func<TEntity, Money?>> propertyExpression,
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

        public void OwnsMoney(
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
                    b.Property(nameof(Money.CurrencyCode))
                        .IsRequired(isRequired)
                        .HasColumnName(codeColumnName)
                        .HasMaxLength(codeMaxLength);

                    b.Property(nameof(Money.Amount))
                        .HasPrecision(32, 10)
                        .IsRequired(isRequired)
                        .HasColumnName(amountColumnName);
                }
            );
        }
    }
}
