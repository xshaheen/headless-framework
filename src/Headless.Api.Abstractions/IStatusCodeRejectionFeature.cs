// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Abstractions;

/// <summary>
/// Per-request feature through which the handler that fails a request supplies the rejection response
/// the status-codes rewriter (<c>UseStatusCodesRewriter()</c>) writes once the rest of the pipeline has
/// returned. It lets a feature shape its own rejection — a discriminator code, an overridden status, a
/// response that must stay byte-identical to another — without the rewriter knowing that feature.
/// </summary>
/// <remarks>
/// <para>
/// Store the feature under this interface type, not its concrete type: the rewriter reads
/// <c>Features.Get&lt;IStatusCodeRejectionFeature&gt;()</c>. Use
/// <see cref="Microsoft.AspNetCore.Http.HeadlessStatusCodeRejectionExtensions.TrySetStatusCodeRejection"/>,
/// which also applies the precedence rule below.
/// </para>
/// <para>
/// The request carries one rejection. The first handler that fails the request owns it: set a
/// rejection only when none is already present. ASP.NET Core keeps invoking authorization handlers after
/// one fails, so a later handler that overwrote the slot would replace the response the earlier failure
/// chose — including the tenant identifier mismatch rejection, which exists to stay indistinguishable
/// from an unknown tenant. Ownership is decided when the rejection is set, not when the response is
/// written: a rejection that later declines the final status still keeps a later handler's rejection out,
/// and the rewriter then applies its default handling.
/// </para>
/// </remarks>
[PublicAPI]
public interface IStatusCodeRejectionFeature
{
    /// <summary>
    /// Writes the rejection response for <paramref name="context"/>, or declines so the rewriter applies
    /// its default handling for the current status code.
    /// </summary>
    /// <param name="context">The current HTTP context. The response has not started.</param>
    /// <returns>
    /// <see langword="true"/> when the response was written; <see langword="false"/> to decline without
    /// touching the response. An implementation that accepts owns the whole response, so it clears any
    /// partial upstream headers and sets the status it needs.
    /// </returns>
    Task<bool> TryWriteResponseAsync(HttpContext context);
}
