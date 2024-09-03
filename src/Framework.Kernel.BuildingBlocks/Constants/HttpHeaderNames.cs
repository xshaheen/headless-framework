#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

public static class HttpHeaderNames
{
    public const string Authorization = "Authorization";
    public const string ApiKey = "X-Api-Key";
    public const string ApiVersion = "Api-Version";
    public const string UserAgent = "User-Agent";
    public const string CorrelationId = "CorrelationId";
    public const string Forwards = "X-Forwarded-For";
    public const string XPoweredBy = "X-Powered-By";
    public const string Locale = "X-Locale";
    public const string ClientVersion = "X-Client-Version";
    public const string Location = "Location";
    public const string ContentDisposition = "Content-Disposition";
    public const string ETag = "ETag";
    public const string ContentEncoding = "Content-Encoding";
    public const string RateLimit = "X-RateLimit-Limit";
    public const string RateLimitRemaining = "X-RateLimit-Remaining";
    public const string Antiforgery = "X-XSRF-TOKEN";
}
