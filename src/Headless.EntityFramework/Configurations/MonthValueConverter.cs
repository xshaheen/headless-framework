// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Month = Headless.Primitives.Month;

namespace Headless.EntityFramework.Configurations;

/// <summary>EF Core value converter that stores <c>Month</c> as its underlying <see cref="int"/> value.</summary>
[PublicAPI]
public sealed class MonthValueConverter : ValueConverter<Month, int>
{
    /// <summary>Initializes the converter with default mapping hints.</summary>
    public MonthValueConverter()
        : base(v => v, v => v) { }

    /// <summary>Initializes the converter with custom mapping hints.</summary>
    /// <param name="mappingHints">Optional hints that influence how the provider maps the column.</param>
    public MonthValueConverter(ConverterMappingHints? mappingHints = null)
        : base(v => v, v => v, mappingHints) { }
}
