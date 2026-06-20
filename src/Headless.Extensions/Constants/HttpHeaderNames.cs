// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Constants;

/// <summary>
/// HTTP header names used by the framework. Includes standard IANA headers (e.g.
/// <see cref="Authorization"/>, <see cref="Location"/>, <see cref="ETag"/>) alongside conventional
/// <c>X-</c> headers and framework-specific custom headers; the wire name may differ from the
/// property name, so prefer these constants over hard-coded strings.
/// </summary>
[PublicAPI]
public static class HttpHeaderNames
{
    public const string Authorization = "Authorization";

    /// <summary>Custom API-key header (<c>X-Api-Key</c>) for key-based authentication.</summary>
    public const string ApiKey = "X-Api-Key";

    /// <summary>Requested API version (<c>Api-Version</c>) used by API versioning.</summary>
    public const string ApiVersion = "Api-Version";

    public const string UserAgent = "User-Agent";

    /// <summary>Request-correlation identifier (<c>CorrelationId</c>) for tracing a request across services and logs.</summary>
    public const string CorrelationId = "CorrelationId";

    /// <summary>Client/proxy chain forwarded for the originating client IP (<c>X-Forwarded-For</c>).</summary>
    public const string Forwards = "X-Forwarded-For";

    public const string XPoweredBy = "X-Powered-By";

    /// <summary>Custom locale / culture override header (<c>X-Locale</c>).</summary>
    public const string Locale = "X-Locale";

    /// <summary>Client-supplied idempotency key (<c>Idempotency-Key</c>) used to de-duplicate retried mutating requests.</summary>
    public const string IdempotencyKey = "Idempotency-Key";

    /// <summary>Response flag (<c>Idempotent-Replayed</c>) indicating the response was served from a cached idempotent result rather than freshly executed.</summary>
    public const string IdempotentReplayed = "Idempotent-Replayed";

    /// <summary>Calling client's version (<c>X-Client-Version</c>), e.g. for compatibility gating.</summary>
    public const string ClientVersion = "X-Client-Version";

    public const string Location = "Location";
    public const string ContentDisposition = "Content-Disposition";
    public const string ETag = "ETag";
    public const string ContentEncoding = "Content-Encoding";

    /// <summary>Rate-limit ceiling for the current window (<c>X-RateLimit-Limit</c>).</summary>
    public const string RateLimit = "X-RateLimit-Limit";

    /// <summary>Remaining requests in the current rate-limit window (<c>X-RateLimit-Remaining</c>).</summary>
    public const string RateLimitRemaining = "X-RateLimit-Remaining";

    /// <summary>Anti-forgery (CSRF) token header (<c>X-XSRF-TOKEN</c>).</summary>
    public const string Antiforgery = "X-XSRF-TOKEN";
}
