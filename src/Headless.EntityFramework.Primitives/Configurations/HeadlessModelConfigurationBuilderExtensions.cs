// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.Primitives;
using AccountId = Headless.Primitives.AccountId;
using File = Headless.Primitives.File;
using MoneyAmount = Headless.Primitives.MoneyAmount;
using Month = Headless.Primitives.Month;
using UserId = Headless.Primitives.UserId;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Extension methods for registering Headless building-block primitive type converter mappings on a
/// <see cref="ModelConfigurationBuilder"/>.
/// </summary>
public static class HeadlessModelConfigurationBuilderExtensions
{
    /// <summary>
    /// Registers default column precision rules and value converters for the Headless primitive types
    /// (<c>MoneyAmount</c>, <c>Month</c>, <c>UserId</c>, <c>AccountId</c>, <c>Locales</c>,
    /// <c>ExtraProperties</c>, <c>File</c>, <c>Image</c>) and configures <see cref="decimal"/> precision
    /// and <see cref="Enum"/> string storage globally.
    /// </summary>
    public static void AddBuildingBlocksPrimitivesConvertersMappings(this ModelConfigurationBuilder b)
    {
        b.Properties<decimal?>().HavePrecision(32, 10);
        b.Properties<decimal>().HavePrecision(32, 10);
        b.Properties<Enum>().HaveMaxLength(DomainConstants.EnumMaxLength).HaveConversion<string>();
        b.Properties<Month>().HaveConversion<MonthValueConverter>();
        b.Properties<MoneyAmount>().HaveConversion<MoneyAmountValueConverter>().HavePrecision(32, 10);
        b.Properties<UserId>().HaveConversion<UserIdValueConverter>();
        b.Properties<AccountId>().HaveConversion<AccountIdValueConverter>();
        // File and Image carry mutable members (Container, Metadata), so they need a deep comparer for an in-place
        // change to be saved.
        b.Properties<File>().HaveConversion<JsonValueConverter<File>, JsonValueComparer<File>>();
        b.Properties<Image>().HaveConversion<JsonValueConverter<Image>, JsonValueComparer<Image>>();
        b.Properties<Locales>().HaveConversion<LocalesValueConverter, LocalesValueComparer>();
        b.Properties<ExtraProperties>().HaveConversion<ExtraPropertiesValueConverter, ExtraPropertiesValueComparer>();
    }
}
