// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.Abstractions;

public sealed class HttpContextRequestedApiVersion(IHttpContextAccessor accessor) : IRequestedApiVersion
{
    public string? Current =>
        accessor.HttpContext?.GetRequestedApiVersion()?.ToString(format: null, CultureInfo.InvariantCulture);
}
