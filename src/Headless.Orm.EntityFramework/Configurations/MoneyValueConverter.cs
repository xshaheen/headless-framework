// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Money = Headless.Primitives.Money;

namespace Headless.EntityFramework.Configurations;

/// <summary>EF Core value converter that stores <c>Money</c> as its underlying <see cref="decimal"/> value.</summary>
[PublicAPI]
public sealed class MoneyValueConverter : ValueConverter<Money, decimal>
{
    /// <summary>Initializes the converter with default mapping hints.</summary>
    public MoneyValueConverter()
        : base(v => v, v => v) { }

    /// <summary>Initializes the converter with custom mapping hints.</summary>
    /// <param name="mappingHints">Optional hints that influence how the provider maps the column.</param>
    public MoneyValueConverter(ConverterMappingHints? mappingHints = null)
        : base(v => v, v => v, mappingHints) { }
}
