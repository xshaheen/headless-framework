// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Primitives;

/// <summary>
/// Describes a single hypermedia operation (HATEOAS link) that a client may invoke on a resource.
/// Included in <see cref="OperationsDataEnvelope{T}"/> and <see cref="OperationsCollectionEnvelope{T}"/>
/// to advertise available transitions alongside the resource data.
/// </summary>
/// <param name="Code">
/// A stable machine-readable identifier for this operation (e.g., <c>"approve"</c>,
/// <c>"cancel"</c>). Clients branch on this value — do not parse <see cref="Href"/> or
/// <see cref="Method"/> to determine intent.
/// </param>
/// <param name="Href">The URL the client should call to perform this operation.</param>
/// <param name="Method">The HTTP method to use (e.g., <c>"POST"</c>, <c>"DELETE"</c>).</param>
/// <param name="IdempotentKey">
/// A per-operation idempotency key the client should forward in the <c>Idempotency-Key</c>
/// request header to make the call safely retryable.
/// </param>
[PublicAPI]
public sealed record OperationDescriptor(string Code, string Href, string Method, string IdempotentKey);
