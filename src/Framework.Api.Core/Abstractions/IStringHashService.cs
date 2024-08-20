using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Framework.Api.Core.Abstractions;

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
public sealed class StringHashService(IOptionsSnapshot<StringHashSettings> options) : IStringHashService
{
    public string Create(string value, string salt)
    {
        var settings = options.Value;

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
