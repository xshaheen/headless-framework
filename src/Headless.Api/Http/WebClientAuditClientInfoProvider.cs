// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;

namespace Headless.Api;

// Reads the same client info IRequestContext.WebClient exposes. It reads IWebClientInfoProvider directly because that
// is a singleton over IHttpContextAccessor, so the audit save pipeline can resolve it from any scope.
internal sealed class WebClientAuditClientInfoProvider(IWebClientInfoProvider webClient) : IAuditClientInfoProvider
{
    public string? IpAddress => webClient.IpAddress;

    public string? UserAgent => webClient.UserAgent;
}
