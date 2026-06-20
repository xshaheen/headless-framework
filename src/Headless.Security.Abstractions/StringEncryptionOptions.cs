// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Abstractions;

/// <summary>
/// Configures the default values used by <see cref="IStringEncryptionService" />.
/// </summary>
[PublicAPI]
public sealed class StringEncryptionOptions
{
    /// <summary>
    /// Gets or sets the symmetric key size in bits.
    /// </summary>
    public int KeySize { get; set; } = 256;

    /// <summary>
    /// Gets or sets the default pass phrase used to derive the encryption key.
    /// </summary>
    public required string DefaultPassPhrase { get; set; }

    /// <summary>
    /// Gets or sets the initialization vector bytes used for encryption and decryption.
    /// </summary>
    public required byte[] InitVectorBytes { get; set; }

    /// <summary>
    /// Gets or sets the default salt used to derive the encryption key.
    /// </summary>
    public required byte[] DefaultSalt { get; set; }
}

/// <summary>
/// Validates <see cref="StringEncryptionOptions" />.
/// </summary>
public sealed class StringEncryptionOptionsValidator : AbstractValidator<StringEncryptionOptions>
{
    public StringEncryptionOptionsValidator()
    {
        RuleFor(x => x.KeySize)
            .Must(keySize => keySize is 128 or 192 or 256)
            .WithMessage("KeySize must be 128, 192, or 256 bits (the legal AES key sizes).");
        RuleFor(x => x.DefaultPassPhrase).NotEmpty();
        // AES has a fixed 128-bit block, so the IV is always 16 bytes regardless of key size.
        RuleFor(x => x.InitVectorBytes)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(iv => iv.Length == 16)
            .WithMessage("InitVectorBytes must be exactly 16 bytes (the AES block size).");
        RuleFor(x => x.DefaultSalt).NotEmpty();
    }
}
