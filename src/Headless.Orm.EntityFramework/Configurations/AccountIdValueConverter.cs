// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using AccountId = Headless.Primitives.AccountId;

namespace Headless.EntityFramework.Configurations;

/// <summary>EF Core value converter that stores <c>AccountId</c> as its underlying <see cref="string"/> value.</summary>
public sealed class AccountIdValueConverter : ValueConverter<AccountId, string>
{
    /// <summary>Initializes the converter with default mapping hints.</summary>
    public AccountIdValueConverter()
        : base(v => v, v => new AccountId(v)) { }

    /// <summary>Initializes the converter with custom mapping hints.</summary>
    /// <param name="mappingHints">Optional hints that influence how the provider maps the column.</param>
    public AccountIdValueConverter(ConverterMappingHints? mappingHints)
        : base(v => v, v => new AccountId(v), mappingHints) { }
}
