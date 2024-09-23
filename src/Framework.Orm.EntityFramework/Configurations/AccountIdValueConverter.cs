// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Primitives;
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
