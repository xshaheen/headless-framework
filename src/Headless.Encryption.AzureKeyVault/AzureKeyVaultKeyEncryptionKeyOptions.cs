// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Encryption.AzureKeyVault;

/// <summary>Chooses the Key Vault key that wraps data keys.</summary>
[PublicAPI]
public sealed class AzureKeyVaultKeyEncryptionKeyOptions
{
    /// <summary>
    /// Gets or sets the name of an RSA key (2048 or 3072 bits) in the vault the registered <c>KeyClient</c> reaches,
    /// with the wrapKey and unwrapKey operations. New data keys are wrapped with the key's current version; keys
    /// wrapped earlier are unwrapped with the exact version that wrapped them. Required.
    /// </summary>
    public string KeyName { get; set; } = string.Empty;
}

internal sealed class AzureKeyVaultKeyEncryptionKeyOptionsValidator
    : AbstractValidator<AzureKeyVaultKeyEncryptionKeyOptions>
{
    public AzureKeyVaultKeyEncryptionKeyOptionsValidator()
    {
        // Key Vault object names: 1 to 127 characters, letters, digits, and dashes.
        RuleFor(x => x.KeyName)
            .NotEmpty()
            .MaximumLength(127)
            .Matches("^[0-9A-Za-z-]+$")
            .WithMessage("KeyName must be a Key Vault key name: letters, digits, and dashes.");
    }
}
