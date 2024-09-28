// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Domains;
using Framework.Kernel.Primitives;
using Framework.Orm.EntityFramework.Configurations;
using Microsoft.EntityFrameworkCore;
using File = Framework.Kernel.Primitives.File;

namespace Framework.Orm.EntityFramework.Extensions;

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
        b.Properties<File>().HaveConversion<FileValueConverter>();
        b.Properties<Image>().HaveConversion<ImageValueConverter>();
        b.Properties<Locale>().HaveConversion<LocaleValueConverter, LocaleValueComparer>();
        b.Properties<ExtraProperties>().HaveConversion<ExtraPropertiesValueConverter, ExtraPropertiesValueComparer>();
    }
}
