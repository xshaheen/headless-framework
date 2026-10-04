// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Api;

internal sealed class HttpContextRequestedApiVersion(IHttpContextAccessor accessor) : IRequestedApiVersion
{
    public string? Current =>
        accessor.HttpContext?.RequestedApiVersion?.ToString(format: null, CultureInfo.InvariantCulture);
}
