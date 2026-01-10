// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domain;
using Framework.Orm.EntityFramework.Configurations;
using Framework.Primitives;
using File = Framework.Primitives.File;

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
