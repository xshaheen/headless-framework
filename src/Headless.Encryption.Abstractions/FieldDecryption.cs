// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;

namespace Headless.Encryption;

/// <summary>The result of <see cref="IFieldEncryptor.DecryptAsync" />.</summary>
[PublicAPI]
public sealed class FieldDecryption
{
    /// <summary>Gets the decrypted bytes. The caller owns the array and may clear it after use.</summary>
    public required byte[] Plaintext { get; init; }

    /// <summary>
    /// Gets whether the frame's data key is still the active key of its scope. <see langword="false" /> after a key
    /// rotation: a re-encryption job encrypts the plaintext again and stores the new frame.
    /// </summary>
    public required bool KeyIsCurrent { get; init; }

    /// <summary>Returns <see cref="Plaintext" /> decoded as UTF-8.</summary>
    /// <returns>The plaintext as text.</returns>
    public string GetString() => Encoding.UTF8.GetString(Plaintext);
}
