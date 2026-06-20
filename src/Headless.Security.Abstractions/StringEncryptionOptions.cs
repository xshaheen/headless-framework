// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>
/// Configures the default values used by <see cref="IStringEncryptionService" />.
/// </summary>
[PublicAPI]
public sealed class StringEncryptionOptions
{
    /// <summary>
    /// Gets or sets the symmetric key size in bits. Must be one of the legal AES key sizes: 128, 192, or 256.
    /// </summary>
    public int KeySize { get; set; } = 256;

    /// <summary>
    /// Gets or sets the number of PBKDF2 iterations used to derive the encryption key from the pass phrase.
    /// </summary>
    public int Iterations { get; set; } = 600_000;

    /// <summary>
    /// Gets or sets the default pass phrase used to derive the encryption key.
    /// </summary>
    public required string DefaultPassPhrase { get; set; }

    /// <summary>
    /// Gets or sets the default salt used to derive the encryption key.
    /// </summary>
    public required byte[] DefaultSalt { get; set; }
}
