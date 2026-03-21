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
        RuleFor(x => x.KeySize).GreaterThan(0);
        RuleFor(x => x.DefaultPassPhrase).NotEmpty();
        RuleFor(x => x.InitVectorBytes)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must((settings, iv) => iv.Length == settings.KeySize / 16);
        RuleFor(x => x.DefaultSalt).NotEmpty();
    }
}
