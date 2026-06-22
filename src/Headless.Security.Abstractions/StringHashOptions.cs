// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;

namespace Headless.Abstractions;

/// <summary>
/// Configures the default values used by <see cref="IStringHashService" />.
/// </summary>
[PublicAPI]
public sealed class StringHashOptions
{
    /// <summary>
    /// Gets or sets the number of PBKDF2 iterations. Must be greater than zero. Defaults to 600,000.
    /// </summary>
    public int Iterations { get; set; } = 600_000;

    /// <summary>
    /// Gets or sets the output hash size in bytes. Must be at least 16. Defaults to 32 (256 bits).
    /// </summary>
    public int SizeInBytes { get; set; } = 32;

    /// <summary>
    /// Gets or sets the PBKDF2 hash algorithm. Must be a SHA-2 family algorithm (SHA256, SHA384, or SHA512).
    /// Defaults to <see cref="HashAlgorithmName.SHA256" />.
    /// </summary>
    public HashAlgorithmName Algorithm { get; set; } = HashAlgorithmName.SHA256;

    /// <summary>
    /// Gets or sets the optional default salt applied when <see cref="IStringHashService.Create(string, string?)" />
    /// is called without an explicit salt. When <see langword="null" />, an empty salt is used, producing a globally
    /// deterministic (unsalted) hash.
    /// </summary>
    public string? DefaultSalt { get; set; }
}
