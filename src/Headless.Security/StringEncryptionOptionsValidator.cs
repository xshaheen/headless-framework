// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Security;

/// <summary>Validates <see cref="StringEncryptionOptions" />.</summary>
internal sealed class StringEncryptionOptionsValidator : AbstractValidator<StringEncryptionOptions>
{
    public StringEncryptionOptionsValidator()
    {
        RuleFor(x => x.KeySize)
            .Must(keySize => keySize is 128 or 192 or 256)
            .WithMessage("KeySize must be 128, 192, or 256 bits (the legal AES key sizes).");
        RuleFor(x => x.Iterations).GreaterThan(0);
        RuleFor(x => x.DefaultPassPhrase).NotEmpty();
        RuleFor(x => x.DefaultSalt).NotEmpty();
    }
}
