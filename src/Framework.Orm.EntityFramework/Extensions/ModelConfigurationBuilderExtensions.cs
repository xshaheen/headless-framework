// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Domains;
using Framework.Kernel.Primitives;
using Framework.Orm.EntityFramework.Configurations;
using Microsoft.EntityFrameworkCore;
using File = Framework.Kernel.Primitives.File;

namespace Framework.Orm.EntityFramework.Extensions;

public static class ModelConfigurationBuilderExtensions
{
    public static void AddBuildingBlocksPrimitivesConvertersMappings(
        this ModelConfigurationBuilder configurationBuilder
    )
    {
        configurationBuilder.Properties<decimal?>().HavePrecision(32, 10);
        configurationBuilder.Properties<decimal>().HavePrecision(32, 10);
        configurationBuilder.Properties<Enum>().HaveMaxLength(DomainConstants.EnumMaxLength).HaveConversion<string>();

        configurationBuilder.Properties<UserId>().HaveConversion<UserIdValueConverter>();
        configurationBuilder.Properties<AccountId>().HaveConversion<AccountIdValueConverter>();
        configurationBuilder.Properties<Month>().HaveConversion<MonthValueConverter>();
        configurationBuilder.Properties<Money>().HaveConversion<MoneyValueConverter>().HavePrecision(32, 10);
        configurationBuilder.Properties<File>().HaveConversion<FileValueConverter>();
        configurationBuilder.Properties<Image>().HaveConversion<ImageValueConverter>();
        configurationBuilder.Properties<Locale>().HaveConversion<LocaleValueConverter, LocaleValueComparer>();
        configurationBuilder
            .Properties<ExtraProperties>()
            .HaveConversion<ExtraPropertiesValueConverter, ExtraPropertiesValueComparer>();
    }
}
