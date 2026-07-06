// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Primitives;

/// <summary>Marker contract for response envelopes that carry a human-readable status message.</summary>
public interface IMessageEnvelope
{
    /// <summary>The human-readable status message descriptor.</summary>
    MessageDescriptor Message { get; }
}

/// <summary>
/// API response envelope that carries a single <see cref="MessageDescriptor"/>.
/// Serializes as <c>{ "message": { ... } }</c>. Use for operations that produce no resource ID
/// but must communicate a user-facing outcome (e.g., "Email sent", "Password updated").
/// </summary>
/// <param name="Message">The human-readable status message.</param>
public sealed record MessageEnvelope(MessageDescriptor Message) : IMessageEnvelope
{
    /// <summary>Wraps a <see cref="MessageDescriptor"/> in a <see cref="MessageEnvelope"/>.</summary>
    public static MessageEnvelope FromMessageDescriptor(MessageDescriptor operand) => new(operand);

    /// <summary>Implicitly wraps a <see cref="MessageDescriptor"/> in a <see cref="MessageEnvelope"/>.</summary>
    public static implicit operator MessageEnvelope(MessageDescriptor operand) => new(operand);

    /// <summary>
    /// Wraps a plain string message. The string is implicitly converted to a
    /// <see cref="MessageDescriptor"/> before wrapping.
    /// </summary>
    public static MessageEnvelope FromString(string operand) => new(operand);

    /// <summary>
    /// Implicitly wraps a plain string message. The string is implicitly converted to a
    /// <see cref="MessageDescriptor"/> before wrapping.
    /// </summary>
    public static implicit operator MessageEnvelope(string operand) => new(operand);
}
