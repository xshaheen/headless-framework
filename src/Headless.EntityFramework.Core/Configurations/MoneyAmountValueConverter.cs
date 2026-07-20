// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MoneyAmount = Headless.Primitives.MoneyAmount;

namespace Headless.EntityFramework.Configurations;

/// <summary>EF Core value converter that stores <c>MoneyAmount</c> as its underlying <see cref="decimal"/> value.</summary>
[PublicAPI]
public sealed class MoneyAmountValueConverter : ValueConverter<MoneyAmount, decimal>
{
    /// <summary>Initializes the converter with default mapping hints.</summary>
    public MoneyAmountValueConverter()
        : base(v => v, v => v) { }

    /// <summary>Initializes the converter with custom mapping hints.</summary>
    /// <param name="mappingHints">Optional hints that influence how the provider maps the column.</param>
    public MoneyAmountValueConverter(ConverterMappingHints? mappingHints = null)
        : base(v => v, v => v, mappingHints) { }
}
