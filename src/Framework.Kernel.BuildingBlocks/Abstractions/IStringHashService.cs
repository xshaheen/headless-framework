// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using FluentValidation;

namespace Framework.Kernel.BuildingBlocks.Abstractions;

public interface IStringHashService
{
    string Create(string value, string salt);
}

#region Options

public sealed class StringHashSettings
{
    public int Iterations { get; init; } = 1000;

    public int Size { get; init; } = 128;

    public HashAlgorithmName Algorithm { get; init; } = HashAlgorithmName.SHA256;
}

public sealed class StringHashSettingsValidator : AbstractValidator<StringHashSettings>
{
    public StringHashSettingsValidator()
    {
        RuleFor(x => x.Iterations).GreaterThan(0);
        RuleFor(x => x.Size).GreaterThan(0);
        RuleFor(x => x.Algorithm).NotEqual((HashAlgorithmName)default);
    }
}

#endregion

#region Implementation

#pragma warning disable CA5379 // CA5379: Ensure key derivation function algorithm is sufficiently strong
public sealed class StringHashService(StringHashSettings settings) : IStringHashService
{
    public string Create(string value, string salt)
    {
        using var algorithm = new Rfc2898DeriveBytes(
            value,
            Encoding.UTF8.GetBytes(salt),
            settings.Iterations,
            settings.Algorithm
        );

        return Convert.ToBase64String(algorithm.GetBytes(settings.Size));
    }
}

#endregion
