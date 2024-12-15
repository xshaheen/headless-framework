// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.Configurations;

/// <summary>ValueConverter for <see cref = "Month"/></summary>
public sealed class MonthValueConverter : ValueConverter<Month, int>
{
    public MonthValueConverter()
        : base(v => v, v => v) { }

    public MonthValueConverter(ConverterMappingHints? mappingHints = null)
        : base(v => v, v => v, mappingHints) { }
}
