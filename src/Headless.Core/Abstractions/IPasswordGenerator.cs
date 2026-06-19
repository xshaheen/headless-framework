// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Checks;

namespace Headless.Abstractions;

public interface IPasswordGenerator
{
    string GeneratePassword(
        int length,
        int requiredUniqueChars = 1,
        bool requireDigit = true,
        bool requireLowercase = true,
        bool requireNonAlphanumeric = true,
        bool requireUppercase = true,
        bool useDigitsInRemaining = true,
        bool useLowercaseInRemaining = false,
        bool useUppercaseInRemaining = false,
        bool useNonAlphanumericInRemaining = false
    );
}

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

    public string GeneratePassword(
        int length,
        int requiredUniqueChars = 1,
        bool requireDigit = true,
        bool requireLowercase = true,
        bool requireNonAlphanumeric = true,
        bool requireUppercase = true,
        bool useDigitsInRemaining = true,
        bool useLowercaseInRemaining = false,
        bool useUppercaseInRemaining = false,
        bool useNonAlphanumericInRemaining = false
    )
    {
        /* Validate password configuration */

        Argument.IsPositive(length);
        Argument.IsPositiveOrZero(requiredUniqueChars);

        var requiredCharsCount =
            (requireDigit ? 1 : 0)
            + (requireLowercase ? 1 : 0)
            + (requireUppercase ? 1 : 0)
            + (requireNonAlphanumeric ? 1 : 0);

        Ensure.True(
            length >= requiredCharsCount,
            "Invalid password configuration provided. The length must be greater than or equal to the number of required character sets."
        );

        Ensure.True(
            useDigitsInRemaining || useLowercaseInRemaining || useUppercaseInRemaining || useNonAlphanumericInRemaining,
            "Invalid password configuration provided. At least one character set must be used in remaining characters."
        );

        Ensure.True(
            requiredUniqueChars
                <= _NonAlphanumericChars.Length + _DigitsChars.Length + _LowercaseChars.Length + _UppercaseChars.Length,
            "Invalid password configuration provided. Required unique characters count is greater than the total available characters."
        );

        requiredUniqueChars = Math.Min(requiredUniqueChars, length);

        /* Generate password */

        var chars = new List<char>();

        // 1. Add required characters

        if (requireDigit)
        {
            chars.Add(_GetDigit());
        }

        if (requireLowercase)
        {
            chars.Add(_GetLowercase());
        }

        if (requireUppercase)
        {
            chars.Add(_GetUppercase());
        }

        if (requireNonAlphanumeric)
        {
            chars.Add(_GetNonAlphanumeric());
        }

        // 2. Add unique characters

        var remainingRequiredChars = requiredUniqueChars - chars.Count;

        if (remainingRequiredChars > 0)
        {
            var baseCharacters = _GetBaseCharacters(
                    useDigitsInRemaining,
                    useLowercaseInRemaining,
                    useUppercaseInRemaining,
                    useNonAlphanumericInRemaining
                )
                .Where(x => !chars.Contains(x))
                .ToList();

            for (var i = 0; i < remainingRequiredChars; i++)
            {
                // The enabled "remaining" sets can hold fewer distinct chars than requiredUniqueChars
                // (the up-front check bounds against the full pool); stop once they are exhausted
                // instead of indexing an empty list.
                if (baseCharacters.Count == 0)
                {
                    break;
                }

                var index = _GetIntInclusiveBetween(0, baseCharacters.Count - 1);
                var character = baseCharacters[index];
                baseCharacters.RemoveAt(index);
                chars.Add(character);
            }
        }

        // 3. Add remaining characters to reach the length

        var remainingLength = length - chars.Count;

        if (remainingLength > 0)
        {
            var baseCharacters = _GetBaseCharacters(
                    useDigitsInRemaining,
                    useLowercaseInRemaining,
                    useUppercaseInRemaining,
                    useNonAlphanumericInRemaining
                )
                .ToList();

            for (var i = 0; i < remainingLength; i++)
            {
                var index = _GetIntInclusiveBetween(0, baseCharacters.Count - 1);
                var character = baseCharacters[index];
                chars.Add(character);
            }
        }

        // 4. Shuffle characters
        var password = new StringBuilder(length);

        while (chars.Count > 0)
        {
            var index = _GetIntInclusiveBetween(0, chars.Count - 1);
            password.Append(chars[index]);
            chars.RemoveAt(index);
        }

        return password.ToString();
    }

    private static IEnumerable<char> _GetBaseCharacters(
        bool useDigitsInRemaining,
        bool useLowercaseInRemaining,
        bool useUppercaseInRemaining,
        bool useNonAlphanumericInRemaining
    )
    {
        if (useDigitsInRemaining)
        {
            foreach (var c in _DigitsChars)
            {
                yield return c;
            }
        }

        if (useLowercaseInRemaining)
        {
            foreach (var c in _LowercaseChars)
            {
                yield return c;
            }
        }

        if (useUppercaseInRemaining)
        {
            foreach (var c in _UppercaseChars)
            {
                yield return c;
            }
        }

        if (useNonAlphanumericInRemaining)
        {
            foreach (var c in _NonAlphanumericChars)
            {
                yield return c;
            }
        }
    }

    #region Helpers

    private static char _GetUppercase() => _GetCharInclusiveBetween(_AUpperCase, _ZUpperCase);

    private static char _GetNonAlphanumeric()
    {
        var index = _GetIntInclusiveBetween(0, _NonAlphanumericChars.Length - 1);

        return _NonAlphanumericChars[index];
    }

    private static char _GetLowercase() => _GetCharInclusiveBetween(_ALowercase, _ZLowercase);

    private static char _GetDigit() => _GetCharInclusiveBetween(_Zero, _Nine);

    private static char _GetCharInclusiveBetween(int min, int max) => (char)_GetIntInclusiveBetween(min, max);

    // Cryptographically secure: passwords are security-sensitive material, so character selection
    // and shuffling must not use the predictable Random.Shared PRNG.
    private static int _GetIntInclusiveBetween(int min, int max) => RandomNumberGenerator.GetInt32(min, max + 1);

    #endregion
}
