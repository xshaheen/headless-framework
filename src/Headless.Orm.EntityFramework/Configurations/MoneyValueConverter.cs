// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Money = Headless.Primitives.Money;

namespace Headless.Orm.EntityFramework.Configurations;

/// <summary>ValueConverter for <see cref = "Money"/></summary>
[PublicAPI]
public sealed class MoneyValueConverter : ValueConverter<Money, decimal>
{
    public MoneyValueConverter()
        : base(v => v, v => v) { }

    public MoneyValueConverter(ConverterMappingHints? mappingHints = null)
        : base(v => v, v => v, mappingHints) { }
}
