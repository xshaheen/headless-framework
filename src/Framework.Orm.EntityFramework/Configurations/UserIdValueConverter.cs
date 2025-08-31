// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.Configurations;

// TODO: Consider use generic type converter for IPrimitive types
/// <summary>ValueConverter for <see cref = "UserId"/></summary>
public sealed class UserIdValueConverter : ValueConverter<UserId, string>
{
    public UserIdValueConverter()
        : base(v => v, v => v) { }

    public UserIdValueConverter(ConverterMappingHints? mappingHints = null)
        : base(v => v, v => v, mappingHints) { }
}
