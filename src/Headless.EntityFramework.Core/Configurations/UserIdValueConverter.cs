// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using UserId = Headless.Primitives.UserId;

namespace Headless.EntityFramework.Configurations;

/// <summary>EF Core value converter that stores <c>UserId</c> as its underlying <see cref="string"/> value.</summary>
public sealed class UserIdValueConverter : ValueConverter<UserId, string>
{
    /// <summary>Initializes the converter with default mapping hints.</summary>
    public UserIdValueConverter()
        : base(v => v, v => new UserId(v)) { }

    /// <summary>Initializes the converter with custom mapping hints.</summary>
    /// <param name="mappingHints">Optional hints that influence how the provider maps the column.</param>
    public UserIdValueConverter(ConverterMappingHints? mappingHints)
        : base(v => v, v => new UserId(v), mappingHints) { }
}
