// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Primitives;

/// <summary>
/// API response envelope that wraps a single <typeparamref name="T"/> resource together with
/// optional hypermedia operations. Serializes as <c>{ "data": ..., "operations": [...] }</c>.
/// </summary>
/// <typeparam name="T">The type of the wrapped resource.</typeparam>
/// <param name="Data">The wrapped resource value.</param>
/// <param name="Operations">
/// Optional list of <see cref="OperationDescriptor"/> entries advertising state transitions
/// available on this resource (e.g., approve, cancel). <see langword="null"/> when no
/// operations apply.
/// </param>
public sealed record OperationsDataEnvelop<T>(T Data, IReadOnlyList<OperationDescriptor>? Operations = null);
