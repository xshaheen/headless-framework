// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Framework.Api.Abstractions;

public interface IRequestedApiVersion
{
    string? Current { get; }
}

public sealed class HttpContextRequestedApiVersion(IHttpContextAccessor accessor) : IRequestedApiVersion
{
    public string? Current =>
        accessor.HttpContext?.GetRequestedApiVersion()?.ToString(format: null, CultureInfo.InvariantCulture);
}
