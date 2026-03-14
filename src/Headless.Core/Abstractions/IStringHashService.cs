// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using FluentValidation;

namespace Headless.Abstractions;

public interface IStringHashService
{
    string Create(string value, string salt);
}

#region Options

public sealed class StringHashOptions
{
    /// <summary>
    /// PBKDF2 iteration count. Default is 600,000 per OWASP 2023 recommendation for SHA-256.
    /// </summary>
    public int Iterations { get; init; } = 600_000;

    public int Size { get; init; } = 128;

    public HashAlgorithmName Algorithm { get; init; } = HashAlgorithmName.SHA256;
}

public sealed class StringHashOptionsValidator : AbstractValidator<StringHashOptions>
{
    public StringHashOptionsValidator()
    {
        RuleFor(x => x.Iterations).GreaterThan(0);
        RuleFor(x => x.Size).GreaterThan(0);
        RuleFor(x => x.Algorithm).NotEqual((HashAlgorithmName)default);
    }
}

#endregion

#region Implementation

#pragma warning disable CA5379 // CA5379: Ensure key derivation function algorithm is sufficiently strong
public sealed class StringHashService(StringHashOptions options) : IStringHashService
{
    public string Create(string value, string salt)
    {
        var bytes = Rfc2898DeriveBytes.Pbkdf2(
            value,
            Encoding.UTF8.GetBytes(salt),
            options.Iterations,
            options.Algorithm,
            options.Size
        );

        return Convert.ToBase64String(bytes);
    }
}

#endregion
