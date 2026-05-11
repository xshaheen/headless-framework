// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using UserId = Headless.Primitives.UserId;

namespace Headless.EntityFramework.Configurations;

/// <summary>ValueConverter for <see cref = "UserId"/></summary>
public sealed class UserIdValueConverter : ValueConverter<UserId, string>
{
    public UserIdValueConverter()
        : base(v => v, v => new UserId(v)) { }

    public UserIdValueConverter(ConverterMappingHints? mappingHints)
        : base(v => v, v => new UserId(v), mappingHints) { }
}
