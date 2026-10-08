// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Idempotency;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.Idempotency;

/// <summary>
/// Reads back the outcome of an earlier idempotent request, for a client that lost the original response: a payment
/// terminal that timed out, a mobile app that went offline mid-request.
/// </summary>
/// <remarks>
/// <para>
/// The lookup addresses the same record the middleware stored, from the earlier request's method, path, query, and
/// key plus the current caller: the authenticated user and that user's tenant claim. A lookup by another user or
/// tenant finds nothing, exactly as a replay would.
/// </para>
/// <para>
/// It uses the application-level <see cref="IdempotencyOptions" />. A <see cref="IdempotencyOptions.KeyDeriver" />
/// is called with the lookup request's <see cref="HttpContext" />, so a deriver that reads the request path addresses
/// the lookup's path, not the earlier request's; such an application keeps its own reconciliation read through
/// <see cref="IIdempotentOperations.GetResultAsync" />.
/// </para>
/// <para>
/// Reading never admits an attempt: a key with no record stays absent, and a retry of the earlier request with the
/// same key and body still runs it.
/// </para>
/// </remarks>
[PublicAPI]
public interface IIdempotencyLookup
{
    /// <summary>Reads whether the earlier request is absent, still pending, or completed.</summary>
    /// <param name="context">The current request; its user and tenant claim select the record.</param>
    /// <param name="target">The earlier request.</param>
    /// <param name="cancellationToken">Token used to cancel the store call.</param>
    /// <returns>
    /// <see cref="IdempotencyPeekStatus.Absent" /> when no record exists, its retention elapsed, or the caller has no
    /// identity while <see cref="IdempotencyOptions.RequireUserIdentity" /> is on; otherwise
    /// <see cref="IdempotencyPeekStatus.Pending" /> or <see cref="IdempotencyPeekStatus.Completed" />.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="target" /> has a blank or malformed key, or a blank method.</exception>
    ValueTask<IdempotencyPeekStatus> GetStatusAsync(
        HttpContext context,
        IdempotentRequestTarget target,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Writes the earlier request's stored response to the current response (status, allowlisted headers, body, and
    /// <c>Idempotent-Replayed: true</c>) when that request completed.
    /// </summary>
    /// <param name="context">The current request; its user and tenant claim select the record.</param>
    /// <param name="target">The earlier request.</param>
    /// <param name="cancellationToken">Token used to cancel the store call and the response write.</param>
    /// <returns>
    /// <see langword="true" /> when the stored response was written; <see langword="false" /> when there is none to
    /// write (absent, pending, or past retention), leaving the response untouched for the caller to answer.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="target" /> has a blank or malformed key, or a blank method.</exception>
    /// <exception cref="InvalidOperationException">The current response has already started.</exception>
    ValueTask<bool> TryReplayAsync(
        HttpContext context,
        IdempotentRequestTarget target,
        CancellationToken cancellationToken = default
    );
}
