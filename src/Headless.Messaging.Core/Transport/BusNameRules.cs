// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Transport;

/// <summary>
/// The naming rules one broker applies to the subscription a Bus consumer identity becomes: its name length limit and
/// the characters it accepts.
/// </summary>
/// <param name="maxLength">
/// The longest name the broker accepts. Built names are ASCII, so the limit holds in characters and in UTF-8 bytes.
/// </param>
/// <param name="isAllowed">Whether the broker accepts a printable ASCII character in a name.</param>
/// <param name="alphanumericBoundaries">Whether a name must start and end with an ASCII letter or digit.</param>
internal sealed class BusNameRules(int maxLength, Func<char, bool> isAllowed, bool alphanumericBoundaries = false)
{
    private readonly Func<char, bool> _isAllowed = Argument.IsNotNull(isAllowed);

    // A shortened name still needs room for one readable character, the separator, and the hash.
    public int MaxLength { get; } = Argument.IsGreaterThan(maxLength, BusNameBuilder.HashLength + 1);

    public bool AlphanumericBoundaries { get; } = alphanumericBoundaries;

    // Non-ASCII, whitespace, and control characters are never kept, whatever the broker allows: they make names
    // unreadable in broker tooling, and keeping names ASCII means a byte limit and a character limit agree.
    public bool IsAllowed(char c) => char.IsAsciiLetterOrDigit(c) || (c is > ' ' and < '\x7f' && _isAllowed(c));
}
