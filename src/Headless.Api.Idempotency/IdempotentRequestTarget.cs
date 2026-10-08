// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Api.Idempotency;

/// <summary>
/// The earlier request a lookup reads back: the method, path, query, and idempotency key it was sent with.
/// </summary>
/// <param name="Method">The earlier request's HTTP method, such as <c>POST</c>.</param>
/// <param name="Path">The earlier request's path, such as <c>/transactions</c>.</param>
/// <param name="Key">The idempotency key the earlier request carried.</param>
[PublicAPI]
public sealed record IdempotentRequestTarget(string Method, PathString Path, string Key)
{
    /// <summary>Gets the earlier request's query string, when it had one; part of the record's address.</summary>
    public QueryString Query { get; init; }
}
