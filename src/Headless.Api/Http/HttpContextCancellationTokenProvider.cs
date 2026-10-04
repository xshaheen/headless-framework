// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Microsoft.AspNetCore.Http;

namespace Headless.Api;

internal sealed class HttpContextCancellationTokenProvider(IHttpContextAccessor accessor) : ICancellationTokenProvider
{
    public CancellationToken Token => accessor.HttpContext?.RequestAborted ?? CancellationToken.None;
}
