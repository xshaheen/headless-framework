// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using FluentValidation;

namespace Headless.Security;

/// <summary>Validates <see cref="LookupHasherOptions" />.</summary>
internal sealed class LookupHasherOptionsValidator : AbstractValidator<LookupHasherOptions>
{
    private static readonly HashAlgorithmName[] _AllowedAlgorithms =
    [
        HashAlgorithmName.SHA256,
        HashAlgorithmName.SHA384,
        HashAlgorithmName.SHA512,
    ];

    public LookupHasherOptionsValidator()
    {
        RuleFor(x => x.Iterations).GreaterThan(0);
        RuleFor(x => x.SizeInBytes).GreaterThanOrEqualTo(16);
        RuleFor(x => x.Algorithm)
            .Must(algorithm => _AllowedAlgorithms.Contains(algorithm))
            .WithMessage("Algorithm must be a SHA-2 family hash (SHA256, SHA384, or SHA512).");
    }
}
