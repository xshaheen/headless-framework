// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Primitives;

/// <summary>
/// API response envelope that pairs a resource identifier with a human-readable
/// <see cref="MessageDescriptor"/>. Combines <see cref="IIdEnvelop"/> and
/// <see cref="IMessageEnvelop"/> for responses that must convey both a new ID and a status message
/// (e.g., "Record created" + the new record's ID).
/// Serializes as <c>{ "id": "...", "message": { ... } }</c>.
/// </summary>
/// <param name="Id">The string representation of the resource identifier.</param>
/// <param name="Message">The human-readable status message.</param>
public sealed record IdMessageEnvelop(string Id, MessageDescriptor Message) : IIdEnvelop, IMessageEnvelop
{
    /// <summary>
    /// Initializes an <see cref="IdMessageEnvelop"/> from a <see cref="Guid"/> identifier.
    /// </summary>
    public IdMessageEnvelop(Guid id, MessageDescriptor message)
        : this(id.ToString(), message) { }

    /// <summary>
    /// Initializes an <see cref="IdMessageEnvelop"/> from a <see cref="long"/> identifier,
    /// formatted with <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    public IdMessageEnvelop(long id, MessageDescriptor message)
        : this(id.ToString(CultureInfo.InvariantCulture), message) { }

    /// <summary>
    /// Initializes an <see cref="IdMessageEnvelop"/> from an <see cref="int"/> identifier,
    /// formatted with <see cref="CultureInfo.InvariantCulture"/>.
    /// </summary>
    public IdMessageEnvelop(int id, MessageDescriptor message)
        : this(id.ToString(CultureInfo.InvariantCulture), message) { }
}
