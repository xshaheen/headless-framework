// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>
/// Configures the default values used by <see cref="IStringEncryptionService" />.
/// </summary>
/// <remarks>
/// The PBKDF2 key derived from <see cref="DefaultPassPhrase" /> and <see cref="DefaultSalt" /> is computed once
/// when the singleton service is constructed and cached for its lifetime. Changing these properties after
/// registration has no effect. Per-call overrides (the <c>passPhrase</c> / <c>salt</c> parameters on
/// <see cref="IStringEncryptionService.Encrypt" /> and <see cref="IStringEncryptionService.Decrypt" />) derive a
/// fresh key on every invocation and are not cached.
/// </remarks>
[PublicAPI]
public sealed class StringEncryptionOptions
{
    /// <summary>
    /// Gets or sets the symmetric key size in bits. Must be one of the legal AES key sizes: 128, 192, or 256.
    /// Defaults to 256.
    /// </summary>
    public int KeySize { get; set; } = 256;

    /// <summary>
    /// Gets or sets the number of PBKDF2 iterations used to derive the encryption key from the pass phrase.
    /// Must be greater than zero. Defaults to 600,000 (the OWASP-recommended minimum for PBKDF2-SHA256 as of 2023).
    /// </summary>
    public int Iterations { get; set; } = 600_000;

    /// <summary>
    /// Gets or sets the default pass phrase used to derive the AES encryption key via PBKDF2. Required; must not be
    /// empty.
    /// </summary>
    public required string DefaultPassPhrase { get; set; }

    /// <summary>
    /// Gets or sets the default salt used to derive the AES encryption key via PBKDF2. Required; must not be empty.
    /// Use a cryptographically random byte sequence of at least 16 bytes.
    /// </summary>
    public required byte[] DefaultSalt { get; set; }
}
