// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using FluentValidation;

namespace Headless.Abstractions;

/// <summary>
/// Configures the default values used by <see cref="IStringHashService" />.
/// </summary>
[PublicAPI]
public sealed class StringHashOptions
{
    /// <summary>
    /// Gets or sets the number of PBKDF2 iterations.
    /// </summary>
    public int Iterations { get; set; } = 600_000;

    /// <summary>
    /// Gets or sets the output hash size in bytes.
    /// </summary>
    public int Size { get; set; } = 128;

    /// <summary>
    /// Gets or sets the PBKDF2 hash algorithm.
    /// </summary>
    public HashAlgorithmName Algorithm { get; set; } = HashAlgorithmName.SHA256;

    /// <summary>
    /// Gets or sets the optional default salt used when <see cref="IStringHashService.Create(string, string?)" /> is
    /// called without a salt.
    /// </summary>
    public string? DefaultSalt { get; set; }
}

/// <summary>
/// Validates <see cref="StringHashOptions" />.
/// </summary>
public sealed class StringHashOptionsValidator : AbstractValidator<StringHashOptions>
{
    public StringHashOptionsValidator()
    {
        RuleFor(x => x.Iterations).GreaterThan(0);
        RuleFor(x => x.Size).GreaterThan(0);
        RuleFor(x => x.Algorithm).NotEqual((HashAlgorithmName)default);
    }
}
