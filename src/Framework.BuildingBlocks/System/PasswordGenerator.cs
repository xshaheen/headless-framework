// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Framework.BuildingBlocks.System;

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

        if (
            !useDigitsInRemaining
            && !useLowercaseInRemaining
            && !useUppercaseInRemaining
            && !useNonAlphanumericInRemaining
        )
        {
            throw new InvalidOperationException(
                "Invalid password configuration provided. At least one character set must be used in remaining characters."
            );
        }

        if (
            requiredUniqueChars
            > _NonAlphanumericChars.Length + _DigitsChars.Length + _LowercaseChars.Length + _UppercaseChars.Length
        )
        {
            throw new InvalidOperationException(
                "Invalid password configuration provided. Required unique characters count is greater than the total available characters."
            );
        }

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

#pragma warning disable CA5394 // Do not use insecure randomness
    private static int _GetIntInclusiveBetween(int min, int max) => Random.Shared.Next(min, max + 1);
#pragma warning restore CA5394

    #endregion
}
