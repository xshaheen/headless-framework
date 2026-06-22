// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>
/// Describes a message with a machine-readable <paramref name="Code"/> and a human-readable
/// <paramref name="Description"/>.
/// </summary>
/// <param name="Code">A distinct code identifying the message.</param>
/// <param name="Description">A human-readable description of the message.</param>
[PublicAPI]
public sealed record MessageDescriptor(string Code, [LocalizationRequired] string Description)
{
    /// <summary>
    /// Creates a <see cref="MessageDescriptor"/> from a string, using the same value for both
    /// <see cref="Code"/> and <see cref="Description"/>.
    /// </summary>
    /// <param name="description">The text used as both the code and the description.</param>
    public static implicit operator MessageDescriptor(string description) => new(description, description);

    /// <summary>
    /// Creates a <see cref="MessageDescriptor"/> from a string, using the same value for both
    /// <see cref="Code"/> and <see cref="Description"/>. Named alternative to the implicit conversion operator.
    /// </summary>
    /// <param name="description">The text used as both the code and the description.</param>
    /// <returns>A <see cref="MessageDescriptor"/> whose code and description both equal <paramref name="description"/>.</returns>
    public static MessageDescriptor ToMessageDescriptor(string description) => description;
}
