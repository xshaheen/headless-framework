// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using AccountId = Headless.Primitives.AccountId;

namespace Headless.Orm.EntityFramework.Configurations;

/// <summary>ValueConverter for <see cref = "AccountId"/></summary>
public sealed class AccountIdValueConverter : PrimitiveValueConverter<AccountId, string>
{
    public AccountIdValueConverter()
        : base() { }

    public AccountIdValueConverter(ConverterMappingHints? mappingHints)
        : base(mappingHints) { }
}
