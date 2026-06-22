// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using FluentValidation;
using Headless.Abstractions;

namespace Headless;

/// <summary>Validates <see cref="StringHashOptions" />.</summary>
internal sealed class StringHashOptionsValidator : AbstractValidator<StringHashOptions>
{
    private static readonly HashAlgorithmName[] _AllowedAlgorithms =
    [
        HashAlgorithmName.SHA256,
        HashAlgorithmName.SHA384,
        HashAlgorithmName.SHA512,
    ];

    public StringHashOptionsValidator()
    {
        RuleFor(x => x.Iterations).GreaterThan(0);
        RuleFor(x => x.SizeInBytes).GreaterThanOrEqualTo(16);
        RuleFor(x => x.Algorithm)
            .Must(algorithm => _AllowedAlgorithms.Contains(algorithm))
            .WithMessage("Algorithm must be a SHA-2 family hash (SHA256, SHA384, or SHA512).");
    }
}
