// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using UserId = Headless.Primitives.UserId;

namespace Headless.Orm.EntityFramework.Configurations;

/// <summary>ValueConverter for <see cref = "UserId"/></summary>
public sealed class UserIdValueConverter : PrimitiveValueConverter<UserId, string>
{
    public UserIdValueConverter()
        : base() { }

    public UserIdValueConverter(ConverterMappingHints? mappingHints)
        : base(mappingHints) { }
}
