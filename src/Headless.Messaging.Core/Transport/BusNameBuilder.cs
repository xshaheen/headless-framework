// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Transport;

/// <summary>The naming rules one broker applies to the subscription a Bus consumer identity becomes.</summary>
internal sealed class BusNameRules
{
    private readonly Func<char, bool> _isAllowed;

    /// <summary>Describes a broker's name length limit and the characters it accepts.</summary>
    /// <param name="maxLength">
    /// The longest name the broker accepts. Built names are ASCII, so the limit holds in characters and in UTF-8 bytes.
    /// </param>
    /// <param name="isAllowed">Whether the broker accepts a printable ASCII character in a name.</param>
    /// <param name="alphanumericBoundaries">Whether a name must start and end with an ASCII letter or digit.</param>
    public BusNameRules(int maxLength, Func<char, bool> isAllowed, bool alphanumericBoundaries = false)
    {
        // A shortened name still needs room for one readable character, the separator, and the hash.
        MaxLength = Argument.IsGreaterThan(maxLength, BusNameBuilder.HashLength + 1);
        _isAllowed = Argument.IsNotNull(isAllowed);
        AlphanumericBoundaries = alphanumericBoundaries;
    }

    public int MaxLength { get; }

    public bool AlphanumericBoundaries { get; }

    // Non-ASCII, whitespace, and control characters are never kept, whatever the broker allows: they make names
    // unreadable in broker tooling, and keeping names ASCII means a byte limit and a character limit agree.
    public bool IsAllowed(char c) => char.IsAsciiLetterOrDigit(c) || (c is > ' ' and < '\x7f' && _isAllowed(c));
}

/// <summary>
/// Derives a broker subscription name from a Bus consumer identity, so every provider maps one identity to one name the
/// same way in every process.
/// </summary>
/// <remarks>
/// An identity the broker accepts as it is becomes the name unchanged. Otherwise the name keeps a readable prefix of the
/// identity, with rejected characters replaced by <c>-</c>, and ends with <c>-</c> plus a hash of the whole identity.
/// The hash is SHA-256 rather than <see cref="string.GetHashCode()"/>, which is randomized per process, and it is taken
/// from the original identity, so two identities that normalize to the same prefix still get different names.
/// </remarks>
internal static class BusNameBuilder
{
    /// <summary>Hex characters of the SHA-256 hash appended to a shortened or normalized name.</summary>
    public const int HashLength = 12;

    private const char _Replacement = '-';

    /// <summary>Returns the broker subscription name for <paramref name="identity"/> under <paramref name="rules"/>.</summary>
    /// <param name="identity">The consumer identity, or the text a provider derives a name from.</param>
    /// <param name="rules">The broker's naming rules.</param>
    /// <returns><paramref name="identity"/> when the broker accepts it; otherwise a readable prefix and a stable hash.</returns>
    public static string Build(string identity, BusNameRules rules)
    {
        Argument.IsNotNullOrWhiteSpace(identity);
        Argument.IsNotNull(rules);

        if (_IsValid(identity, rules))
        {
            return identity;
        }

        var hash = identity.ToSha256()[..HashLength];
        var readableLength = Math.Min(identity.Length, rules.MaxLength - HashLength - 1);

        var readable = string.Create(
            readableLength,
            (identity, rules),
            static (span, state) =>
            {
                for (var i = 0; i < span.Length; i++)
                {
                    var c = state.identity[i];
                    span[i] = state.rules.IsAllowed(c) ? c : _Replacement;
                }
            }
        );

        readable = rules.AlphanumericBoundaries ? _TrimToAlphanumeric(readable) : readable.TrimEnd(_Replacement);

        // The hash ends the name, so a name that must end with a letter or digit does.
        return readable.Length == 0 ? hash : $"{readable}{_Replacement}{hash}";
    }

    private static bool _IsValid(string name, BusNameRules rules)
    {
        if (name.Length > rules.MaxLength)
        {
            return false;
        }

        if (
            rules.AlphanumericBoundaries
            && (!char.IsAsciiLetterOrDigit(name[0]) || !char.IsAsciiLetterOrDigit(name[^1]))
        )
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!rules.IsAllowed(c))
            {
                return false;
            }
        }

        return true;
    }

    private static string _TrimToAlphanumeric(string value)
    {
        var start = 0;
        var end = value.Length;

        while (start < end && !char.IsAsciiLetterOrDigit(value[start]))
        {
            start++;
        }

        while (end > start && !char.IsAsciiLetterOrDigit(value[end - 1]))
        {
            end--;
        }

        return value[start..end];
    }
}
