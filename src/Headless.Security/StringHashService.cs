// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Abstractions;

#pragma warning disable CA5379
namespace Headless;

/// <inheritdoc />
public sealed class StringHashService(StringHashOptions options) : IStringHashService
{
    /// <inheritdoc />
    public string Create(string value, string? salt = null)
    {
        salt ??= options.DefaultSalt ?? string.Empty;

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
