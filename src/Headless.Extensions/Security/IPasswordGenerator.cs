// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Security;

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

/// <summary>Options controlling how <see cref="IPasswordGenerator.GeneratePassword"/> builds a password.</summary>
/// <param name="Length">Total character count of the generated password. Must be positive and at least as large as the number of enabled required-character sets.</param>
[PublicAPI]
public sealed record GeneratePasswordOptions(int Length)
{
    /// <summary>
    /// Target number of distinct characters to include, drawn from the enabled "remaining" sets.
    /// Best-effort: bounded by the distinct characters those sets provide and by <see cref="Length"/>.
    /// </summary>
    public int RequiredUniqueChars { get; init; } = 1;

    /// <summary>Require at least one digit (0-9).</summary>
    public bool RequireDigit { get; init; } = true;

    /// <summary>Require at least one lowercase letter (a-z).</summary>
    public bool RequireLowercase { get; init; } = true;

    /// <summary>Require at least one uppercase letter (A-Z).</summary>
    public bool RequireUppercase { get; init; } = true;

    /// <summary>Require at least one non-alphanumeric character.</summary>
    public bool RequireNonAlphanumeric { get; init; } = true;

    /// <summary>Include digits in the pool that fills the remaining length.</summary>
    public bool UseDigitsInRemaining { get; init; } = true;

    /// <summary>Include lowercase letters in the pool that fills the remaining length.</summary>
    public bool UseLowercaseInRemaining { get; init; }

    /// <summary>Include uppercase letters in the pool that fills the remaining length.</summary>
    public bool UseUppercaseInRemaining { get; init; }

    /// <summary>Include non-alphanumeric characters in the pool that fills the remaining length.</summary>
    public bool UseNonAlphanumericInRemaining { get; init; }
}
