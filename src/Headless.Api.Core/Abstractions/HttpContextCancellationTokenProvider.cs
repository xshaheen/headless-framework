// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.Abstractions;

internal sealed class HttpContextCancellationTokenProvider(IHttpContextAccessor accessor) : ICancellationTokenProvider
{
    public CancellationToken Token => accessor.HttpContext?.RequestAborted ?? CancellationToken.None;
}
