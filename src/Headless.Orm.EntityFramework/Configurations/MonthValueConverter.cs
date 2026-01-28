// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Month = Headless.Primitives.Month;

namespace Headless.Orm.EntityFramework.Configurations;

/// <summary>ValueConverter for <see cref = "Month"/></summary>
[PublicAPI]
public sealed class MonthValueConverter : ValueConverter<Month, int>
{
    public MonthValueConverter()
        : base(v => v, v => v) { }

    public MonthValueConverter(ConverterMappingHints? mappingHints = null)
        : base(v => v, v => v, mappingHints) { }
}
