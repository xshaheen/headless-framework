// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Primitives;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.Configurations;

/// <summary>ValueConverter for <see cref = "UserId"/></summary>
public sealed class UserIdValueConverter : ValueConverter<UserId, string>
{
    public UserIdValueConverter()
        : base(v => v, v => v) { }

    public UserIdValueConverter(ConverterMappingHints? mappingHints = null)
        : base(v => v, v => v, mappingHints) { }
}
