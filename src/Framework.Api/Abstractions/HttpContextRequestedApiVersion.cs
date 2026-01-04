// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Framework.Api.Abstractions;

public sealed class HttpContextRequestedApiVersion(IHttpContextAccessor accessor) : IRequestedApiVersion
{
    public string? Current =>
        accessor.HttpContext?.GetRequestedApiVersion()?.ToString(format: null, CultureInfo.InvariantCulture);
}
