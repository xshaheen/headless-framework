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

/// <summary>
/// Default <see cref="IPasswordGenerator"/> implementation.
/// Uses <see cref="System.Security.Cryptography.RandomNumberGenerator"/> for all character selection and shuffling,
/// so the output is cryptographically secure.
/// </summary>
public sealed class PasswordGenerator : IPasswordGenerator
{
    private const int _Zero = '0';
    private const int _Nine = '9';
    private const int _ALowercase = 'a';
    private const int _ZLowercase = 'z';
    private const int _AUpperCase = 'A';
    private const int _ZUpperCase = 'Z';
    private const string _DigitsChars = "0123456789";
    private const string _LowercaseChars = "abcdefghijklmnopqrstuvwxyz";
    private const string _UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string _NonAlphanumericChars = "!@#$%^&*+=";

    /// <summary>
    /// Generates a cryptographically secure password that satisfies all constraints in <paramref name="options"/>.
    /// Required character sets are placed first, then distinct characters are drawn from the enabled
    /// "remaining" pools, and the result is Fisher-Yates shuffled before being returned.
    /// </summary>
    /// <param name="options">The policy that governs length, required character sets, and uniqueness requirements.</param>
    /// <returns>A newly generated password of exactly <see cref="GeneratePasswordOptions.Length"/> characters.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="GeneratePasswordOptions.Length"/> is not positive, or
    /// <see cref="GeneratePasswordOptions.RequiredUniqueChars"/> is negative.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The options are internally inconsistent: <see cref="GeneratePasswordOptions.Length"/> is less than the
    /// number of enabled required-character sets; no character set is enabled when remaining characters or
    /// additional unique characters must be drawn from the remaining fill pool; or
    /// <see cref="GeneratePasswordOptions.RequiredUniqueChars"/> exceeds the total number of distinct characters
    /// across all available sets.
    /// </exception>
    public string GeneratePassword(GeneratePasswordOptions options)
    {
        Argument.IsNotNull(options);

        /* Validate password configuration */

        var length = options.Length;
        var requiredUniqueChars = options.RequiredUniqueChars;

        Argument.IsPositive(length);
        Argument.IsPositiveOrZero(requiredUniqueChars);

        var requiredCharsCount =
            (options.RequireDigit ? 1 : 0)
            + (options.RequireLowercase ? 1 : 0)
            + (options.RequireUppercase ? 1 : 0)
            + (options.RequireNonAlphanumeric ? 1 : 0);

        Ensure.True(
            length >= requiredCharsCount,
            "Invalid password configuration provided. The length must be greater than or equal to the number of required character sets."
        );

        Ensure.True(
            requiredUniqueChars
                <= _NonAlphanumericChars.Length + _DigitsChars.Length + _LowercaseChars.Length + _UppercaseChars.Length,
            "Invalid password configuration provided. Required unique characters count is greater than the total available characters."
        );

        requiredUniqueChars = Math.Min(requiredUniqueChars, length);

        var hasRemainingCharacterSet =
            options.UseDigitsInRemaining
            || options.UseLowercaseInRemaining
            || options.UseUppercaseInRemaining
            || options.UseNonAlphanumericInRemaining;

        var needsRemainingCharacterSet = length > requiredCharsCount || requiredUniqueChars > requiredCharsCount;

        Ensure.True(
            !needsRemainingCharacterSet || hasRemainingCharacterSet,
            "Invalid password configuration provided. At least one character set must be used in remaining characters."
        );

        /* Generate password */

        var chars = new List<char>(length);

        // 1. Add required characters

        if (options.RequireDigit)
        {
            chars.Add(_GetDigit());
        }

        if (options.RequireLowercase)
        {
            chars.Add(_GetLowercase());
        }

        if (options.RequireUppercase)
        {
            chars.Add(_GetUppercase());
        }

        if (options.RequireNonAlphanumeric)
        {
            chars.Add(_GetNonAlphanumeric());
        }

        // 2. Add distinct characters drawn from the enabled "remaining" sets

        var remainingRequiredChars = requiredUniqueChars - chars.Count;

        if (remainingRequiredChars > 0)
        {
            var pool = _GetBaseCharacters(options).Where(x => !chars.Contains(x)).ToList();

            // The enabled "remaining" sets can hold fewer distinct chars than requiredUniqueChars;
            // draw until satisfied or the pool is exhausted. Swap-with-last keeps each removal O(1).
            for (var i = 0; i < remainingRequiredChars && pool.Count > 0; i++)
            {
                var index = _GetIntInclusiveBetween(0, pool.Count - 1);
                chars.Add(pool[index]);
                pool[index] = pool[^1];
                pool.RemoveAt(pool.Count - 1);
            }
        }

        // 3. Fill the remaining length from the enabled "remaining" sets (repetition allowed)

        var remainingLength = length - chars.Count;

        if (remainingLength > 0)
        {
            var pool = _GetBaseCharacters(options).ToList();

            for (var i = 0; i < remainingLength; i++)
            {
                chars.Add(pool[_GetIntInclusiveBetween(0, pool.Count - 1)]);
            }
        }

        // 4. Shuffle in place (Fisher-Yates) — O(n), no per-element list shifts

        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = _GetIntInclusiveBetween(0, i);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(CollectionsMarshal.AsSpan(chars));
    }

    private static IEnumerable<char> _GetBaseCharacters(GeneratePasswordOptions options)
    {
        if (options.UseDigitsInRemaining)
        {
            foreach (var c in _DigitsChars)
            {
                yield return c;
            }
        }

        if (options.UseLowercaseInRemaining)
        {
            foreach (var c in _LowercaseChars)
            {
                yield return c;
            }
        }

        if (options.UseUppercaseInRemaining)
        {
            foreach (var c in _UppercaseChars)
            {
                yield return c;
            }
        }

        if (options.UseNonAlphanumericInRemaining)
        {
            foreach (var c in _NonAlphanumericChars)
            {
                yield return c;
            }
        }
    }

    #region Helpers

    private static char _GetUppercase()
    {
        return _GetCharInclusiveBetween(_AUpperCase, _ZUpperCase);
    }

    private static char _GetNonAlphanumeric()
    {
        var index = _GetIntInclusiveBetween(0, _NonAlphanumericChars.Length - 1);

        return _NonAlphanumericChars[index];
    }

    private static char _GetLowercase()
    {
        return _GetCharInclusiveBetween(_ALowercase, _ZLowercase);
    }

    private static char _GetDigit()
    {
        return _GetCharInclusiveBetween(_Zero, _Nine);
    }

    private static char _GetCharInclusiveBetween(int min, int max)
    {
        return (char)_GetIntInclusiveBetween(min, max);
    }

    // Cryptographically secure: passwords are security-sensitive material, so character selection
    // and shuffling must not use the predictable Random.Shared PRNG.
    private static int _GetIntInclusiveBetween(int min, int max)
    {
        return RandomNumberGenerator.GetInt32(min, max + 1);
    }

    #endregion
}
