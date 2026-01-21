// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.Configurations;

/// <summary>ValueConverter for <see cref = "UserId"/></summary>
public sealed class UserIdValueConverter : PrimitiveValueConverter<UserId, string>
{
    public UserIdValueConverter()
        : base() { }

    public UserIdValueConverter(ConverterMappingHints? mappingHints)
        : base(mappingHints) { }
}
