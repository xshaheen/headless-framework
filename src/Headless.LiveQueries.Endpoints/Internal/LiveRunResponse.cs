// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Headless.LiveQueries.Internal;

/// <summary>What one run of the endpoint wrote to its synthetic response.</summary>
/// <param name="StatusCode">The response status.</param>
/// <param name="Headers">The response headers.</param>
/// <param name="Body">The response body.</param>
/// <param name="Denied">Whether the run was refused by authorization, so the handler did not execute.</param>
internal sealed record LiveRunResponse(
    int StatusCode,
    IHeaderDictionary Headers,
    ReadOnlyMemory<byte> Body,
    bool Denied
)
{
    public bool Succeeded => StatusCode is >= 200 and <= 299;

    /// <summary>
    /// Gets a value indicating whether the body is declared as JSON: <c>application/json</c> or a type with the
    /// <c>+json</c> suffix. A response with no content type, or with several, is not.
    /// </summary>
    public bool IsJson =>
        MediaTypeHeaderValue.TryParse(Headers.ContentType.ToString(), out var type)
        && (
            type.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || type.Suffix.Equals("json", StringComparison.OrdinalIgnoreCase)
        );
}
