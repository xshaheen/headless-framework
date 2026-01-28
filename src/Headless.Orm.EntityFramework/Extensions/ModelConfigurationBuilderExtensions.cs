// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Orm.EntityFramework.Configurations;
using Headless.Primitives;
using AccountId = Headless.Primitives.AccountId;
using File = Headless.Primitives.File;
using Money = Headless.Primitives.Money;
using Month = Headless.Primitives.Month;
using UserId = Headless.Primitives.UserId;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

public static class ModelConfigurationBuilderExtensions
{
    public static void AddBuildingBlocksPrimitivesConvertersMappings(this ModelConfigurationBuilder b)
    {
        b.Properties<decimal?>().HavePrecision(32, 10);
        b.Properties<decimal>().HavePrecision(32, 10);
        b.Properties<Enum>().HaveMaxLength(DomainConstants.EnumMaxLength).HaveConversion<string>();
        b.Properties<Month>().HaveConversion<MonthValueConverter>();
        b.Properties<Money>().HaveConversion<MoneyValueConverter>().HavePrecision(32, 10);
        b.Properties<UserId>().HaveConversion<UserIdValueConverter>();
        b.Properties<AccountId>().HaveConversion<AccountIdValueConverter>();
        b.Properties<File>().HaveConversion<JsonValueConverter<File>>();
        b.Properties<Image>().HaveConversion<JsonValueConverter<Image>>();
        b.Properties<Locales>().HaveConversion<LocalesValueConverter, LocalesValueComparer>();
        b.Properties<ExtraProperties>().HaveConversion<ExtraPropertiesValueConverter, ExtraPropertiesValueComparer>();
    }
}
