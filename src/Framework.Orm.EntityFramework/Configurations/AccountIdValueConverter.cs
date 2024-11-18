// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.Configurations;

/// <summary>ValueConverter for <see cref = "AccountId"/></summary>
public sealed class AccountIdValueConverter : ValueConverter<AccountId, string>
{
    public AccountIdValueConverter()
        : base(v => v, v => v) { }

    public AccountIdValueConverter(ConverterMappingHints? mappingHints = null)
        : base(v => v, v => v, mappingHints) { }
}
