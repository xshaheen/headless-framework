// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Framework.Api.Abstractions;

public sealed class HttpContextCancellationTokenProvider(IHttpContextAccessor accessor) : ICancellationTokenProvider
{
    public CancellationToken Token => accessor.HttpContext?.RequestAborted ?? CancellationToken.None;
}
