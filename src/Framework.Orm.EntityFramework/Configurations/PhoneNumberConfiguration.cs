// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Framework.BuildingBlocks.Primitives;
using Framework.Primitives;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

public static class PhoneNumberConfiguration
{
    public static void HasComplexPhoneNumber<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, PhoneNumber?>> propertyExpression,
        bool isRequired = true,
        string codeColumnName = "PhoneCountryCode",
        string phoneColumnName = "PhoneNumber"
    )
        where TEntity : class
    {
        builder.ComplexProperty(
            propertyExpression,
            b =>
            {
                b.Property(x => x!.CountryCode)
                    .IsRequired(isRequired)
                    .HasColumnName(codeColumnName)
                    .HasMaxLength(PhoneNumberConstants.Codes.MaxLength);

                b.Property(x => x!.Number)
                    .IsRequired(isRequired)
                    .HasColumnName(phoneColumnName)
                    .HasMaxLength(PhoneNumberConstants.Numbers.MaxLength);
            }
        );
    }

    public static void HasComplexPhoneNumber<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        string propertyName,
        bool isRequired = true,
        string codeColumnName = "PhoneCountryCode",
        string phoneColumnName = "PhoneNumber"
    )
        where TEntity : class
    {
        builder.ComplexProperty(
            propertyName,
            b =>
            {
                b.Property(nameof(PhoneNumber.CountryCode))
                    .IsRequired(isRequired)
                    .HasColumnName(codeColumnName)
                    .HasMaxLength(PhoneNumberConstants.Codes.MaxLength);

                b.Property(nameof(PhoneNumber.Number))
                    .IsRequired(isRequired)
                    .HasColumnName(phoneColumnName)
                    .HasMaxLength(PhoneNumberConstants.Numbers.MaxLength);
            }
        );
    }

    public static void OwnsPhoneNumber<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, PhoneNumber?>> propertyExpression,
        bool isRequired = true,
        string codeColumnName = "PhoneCountryCode",
        string phoneColumnName = "PhoneNumber"
    )
        where TEntity : class
    {
        builder.OwnsOne(
            propertyExpression,
            b =>
            {
                b.Property(x => x.CountryCode)
                    .IsRequired(isRequired)
                    .HasColumnName(codeColumnName)
                    .HasMaxLength(PhoneNumberConstants.Codes.MaxLength);

                b.Property(x => x.Number)
                    .IsRequired(isRequired)
                    .HasColumnName(phoneColumnName)
                    .HasMaxLength(PhoneNumberConstants.Numbers.MaxLength);
            }
        );
    }

    public static void OwnsPhoneNumber(
        this EntityTypeBuilder builder,
        Type entityType,
        string propertyName,
        bool isRequired = true,
        string codeColumnName = "PhoneCountryCode",
        string phoneColumnName = "PhoneNumber"
    )
    {
        builder.OwnsOne(
            entityType,
            propertyName,
            b =>
            {
                b.Property(nameof(PhoneNumber.CountryCode))
                    .IsRequired(isRequired)
                    .HasColumnName(codeColumnName)
                    .HasMaxLength(PhoneNumberConstants.Codes.MaxLength);

                b.Property(nameof(PhoneNumber.Number))
                    .IsRequired(isRequired)
                    .HasColumnName(phoneColumnName)
                    .HasMaxLength(PhoneNumberConstants.Numbers.MaxLength);
            }
        );
    }
}
