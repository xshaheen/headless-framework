// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Primitives;

/// <summary>
/// API response envelope that wraps a read-only collection of <typeparamref name="T"/> items together
/// with optional hypermedia operations. Serializes as
/// <c>{ "items": [...], "operations": [...] }</c>.
/// </summary>
/// <typeparam name="T">The element type of the collection.</typeparam>
/// <param name="Items">The wrapped collection of items.</param>
/// <param name="Operations">
/// Optional list of <see cref="OperationDescriptor"/> entries advertising state transitions
/// available on this collection (e.g., bulk-delete, export). <see langword="null"/> when no
/// operations apply.
/// </param>
public sealed record OperationsCollectionEnvelop<T>(
    IReadOnlyCollection<T> Items,
    IReadOnlyList<OperationDescriptor>? Operations = null
);
