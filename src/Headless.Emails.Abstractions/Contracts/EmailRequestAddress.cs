// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails;

/// <summary>
/// An email address optionally paired with a display name.
/// </summary>
/// <param name="EmailAddress">The RFC 5321 email address (e.g. <c>user@example.com</c>).</param>
/// <param name="DisplayName">
/// The human-readable name shown alongside the address (e.g. <c>Alice</c>).
/// When <see langword="null"/>, only the bare address is used.
/// </param>
[PublicAPI]
public sealed record EmailRequestAddress(string EmailAddress, string? DisplayName = null)
{
    /// <summary>
    /// Implicitly converts a plain email address string to an <see cref="EmailRequestAddress"/>
    /// with no display name.
    /// </summary>
    public static implicit operator EmailRequestAddress(string operand) => new(operand);

    /// <summary>
    /// Converts a plain email address string to an <see cref="EmailRequestAddress"/>
    /// with no display name.
    /// </summary>
    public static EmailRequestAddress FromString(string operand)
    {
        return operand;
    }

    /// <summary>
    /// Returns <c>"DisplayName &lt;EmailAddress&gt;"</c> when a display name is set;
    /// otherwise returns the bare <see cref="EmailAddress"/>.
    /// </summary>
    public override string ToString()
    {
        return DisplayName is null ? EmailAddress : $"{DisplayName} <{EmailAddress}>";
    }
}
