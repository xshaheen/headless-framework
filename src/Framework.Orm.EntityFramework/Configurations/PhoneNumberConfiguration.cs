// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Models.Primitives;
using Framework.Kernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Framework.Orm.EntityFramework.Configurations;

public static class PhoneNumberConfiguration
{
    public static void OptionalPhoneNumber<TEntity>(
        this OwnedNavigationBuilder<TEntity, PhoneNumber> navigationBuilder,
        string prefix = "Phone"
    )
        where TEntity : class
    {
        navigationBuilder.Property(x => x.CountryCode).HasColumnName(prefix + nameof(PhoneNumber.CountryCode));

        navigationBuilder
            .Property(x => x.Number)
            .HasMaxLength(PhoneNumberConstants.Numbers.MaxLength)
            .HasColumnName(prefix + nameof(PhoneNumber.Number));
    }

    public static void RequiredPhoneNumber<TEntity>(
        this OwnedNavigationBuilder<TEntity, PhoneNumber> navigationBuilder,
        string prefix = "Phone"
    )
        where TEntity : class
    {
        navigationBuilder
            .Property(x => x.CountryCode)
            .IsRequired()
            .HasColumnName(prefix + nameof(PhoneNumber.CountryCode));

        navigationBuilder
            .Property(x => x.Number)
            .IsRequired()
            .HasMaxLength(PhoneNumberConstants.Numbers.MaxLength)
            .HasColumnName(prefix + nameof(PhoneNumber.Number));
    }
}
