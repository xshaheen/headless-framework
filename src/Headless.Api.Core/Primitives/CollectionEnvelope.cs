// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130, CA2225
// ReSharper disable once CheckNamespace
namespace Headless.Primitives;

/// <summary>
/// API response envelope that wraps a read-only collection of <typeparamref name="T"/> items.
/// Serializes as <c>{ "items": [...] }</c>. A <see langword="null"/> source coerces to an empty
/// collection via the implicit conversion operators.
/// </summary>
/// <typeparam name="T">The element type of the collection.</typeparam>
/// <param name="Items">The wrapped collection; never <see langword="null"/>.</param>
public sealed record CollectionEnvelope<T>(IReadOnlyCollection<T> Items)
{
    /// <summary>
    /// Implicitly converts a <typeparamref name="T"/> array to a <see cref="CollectionEnvelope{T}"/>.
    /// A <see langword="null"/> array becomes an empty envelope.
    /// </summary>
    public static implicit operator CollectionEnvelope<T>(T[]? operand) => new(operand ?? (IReadOnlyCollection<T>)[]);

    /// <summary>
    /// Implicitly converts a <see cref="HashSet{T}"/> to a <see cref="CollectionEnvelope{T}"/>.
    /// A <see langword="null"/> set becomes an empty envelope.
    /// </summary>
    public static implicit operator CollectionEnvelope<T>(HashSet<T>? operand) =>
        new(operand ?? (IReadOnlyCollection<T>)[]);

    /// <summary>
    /// Implicitly converts a <see cref="List{T}"/> to a <see cref="CollectionEnvelope{T}"/>.
    /// A <see langword="null"/> list becomes an empty envelope.
    /// </summary>
    public static implicit operator CollectionEnvelope<T>(List<T>? operand) =>
        new(operand ?? (IReadOnlyCollection<T>)[]);
}

/// <summary>Non-generic factory helpers for <see cref="CollectionEnvelope{T}"/>.</summary>
public static class CollectionEnvelope
{
    /// <summary>Wraps a <typeparamref name="T"/> array; a <see langword="null"/> array produces an empty envelope.</summary>
    public static CollectionEnvelope<T> FromArray<T>(T[]? operand) => operand;

    /// <summary>Wraps a <see cref="HashSet{T}"/>; a <see langword="null"/> set produces an empty envelope.</summary>
    public static CollectionEnvelope<T> FromHashSet<T>(HashSet<T>? operand) => operand;

    /// <summary>Wraps a <see cref="List{T}"/>; a <see langword="null"/> list produces an empty envelope.</summary>
    public static CollectionEnvelope<T> FromList<T>(List<T>? operand) => operand;

    /// <summary>
    /// Materializes <paramref name="operand"/> to an array and wraps it.
    /// A <see langword="null"/> enumerable produces an empty envelope.
    /// Enumerates <paramref name="operand"/> exactly once.
    /// </summary>
    public static CollectionEnvelope<T> FromEnumerable<T>(IEnumerable<T>? operand) => operand?.ToArray();
}
