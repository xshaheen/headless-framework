// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Encryption;

/// <summary>A data key encrypted by a key-encryption key, as it is stored.</summary>
[PublicAPI]
public sealed class WrappedDataKey
{
    /// <summary>
    /// Gets the id of the key-encryption key that wrapped the data key, including its version where the key service
    /// versions keys, so unwrapping uses that exact version. At most <see cref="DataKeyFieldLimits.KekIdMaxLength" />
    /// characters.
    /// </summary>
    public required string KekId { get; init; }

    /// <summary>Gets the wrapped data key, in the provider's own format.</summary>
    public required byte[] Ciphertext { get; init; }
}
