// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.Configurations;

/// <summary>ValueConverter for <see cref = "AccountId"/></summary>
public sealed class AccountIdValueConverter : PrimitiveValueConverter<AccountId, string>
{
    public AccountIdValueConverter()
        : base() { }

    public AccountIdValueConverter(ConverterMappingHints? mappingHints)
        : base(mappingHints) { }
}
