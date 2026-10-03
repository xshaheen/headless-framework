// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Headless.Checks;

namespace Headless.Abstractions;

/// <summary>
/// Generates cryptographically secure passwords according to a caller-supplied policy.
/// </summary>
public interface IPasswordGenerator
{
    /// <summary>
    /// Generates a password that satisfies the constraints defined by <paramref name="options"/>.
    /// </summary>
    /// <param name="options">The policy controlling length, required character sets, and uniqueness requirements.</param>
    /// <returns>A newly generated password string of the requested length.</returns>
    string GeneratePassword(GeneratePasswordOptions options);
}
