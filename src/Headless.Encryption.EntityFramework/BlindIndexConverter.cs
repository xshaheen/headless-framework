// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.Encryption;

/// <summary>
/// Stores a <see cref="BlindIndex" /> as text with <see cref="BlindIndex.ToBase64Url" />, for a lookup column that must
/// stay text. One input under one key version always gives the same text, so an equality query on the column works.
/// </summary>
/// <remarks>
/// Saving the default value throws, because it holds no hash and its empty text could not be read back. Map an
/// optional column as <see cref="Nullable{T}" /> of <see cref="BlindIndex" /> instead; EF Core stores null for it
/// without calling the converter. Reading text that is not a blind index throws <see cref="FormatException" />.
/// </remarks>
[PublicAPI]
public sealed class BlindIndexConverter : ValueConverter<BlindIndex, string>
{
    /// <summary>Initializes the converter.</summary>
    public BlindIndexConverter()
        : base(static index => _ToText(index), static text => BlindIndex.Parse(text)) { }

    private static string _ToText(BlindIndex index)
    {
        return index.Hash is null
            ? throw new InvalidOperationException(
                "A BlindIndex property holds the default value, which has no hash to store. Compute the index with "
                    + "IBlindIndexer before saving, or map the property as BlindIndex? for an optional column."
            )
            : index.ToBase64Url();
    }
}
