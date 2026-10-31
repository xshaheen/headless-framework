// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.Encryption;

/// <summary>
/// Stores an <see cref="EncryptedValue" /> as its frame bytes (<c>bytea</c> on PostgreSQL, <c>varbinary(max)</c> on
/// SQL Server). It never decrypts: the column holds ciphertext, and the entity holds the frame.
/// </summary>
/// <remarks>
/// Saving the default value throws, because it holds no frame and its empty bytes could not be read back. Map an
/// optional column as <see cref="Nullable{T}" /> of <see cref="EncryptedValue" /> instead; EF Core stores null for it
/// without calling the converter. Reading bytes that are not a frame throws <see cref="FormatException" />.
/// </remarks>
[PublicAPI]
public sealed class EncryptedValueConverter : ValueConverter<EncryptedValue, byte[]>
{
    /// <summary>Initializes the converter.</summary>
    public EncryptedValueConverter()
        : base(static value => _ToFrame(value), static frame => EncryptedValue.FromBytes(frame)) { }

    private static byte[] _ToFrame(EncryptedValue value)
    {
        return value.IsDefault
            ? throw new InvalidOperationException(
                "An EncryptedValue property holds the default value, which has no frame to store. Encrypt the value "
                    + "with IFieldEncryptor before saving, or map the property as EncryptedValue? for an optional column."
            )
            : value.ToArray();
    }
}
