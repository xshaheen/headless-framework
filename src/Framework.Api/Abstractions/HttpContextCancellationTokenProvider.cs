// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Framework.Api.Abstractions;

public sealed class HttpContextCancellationTokenProvider(IHttpContextAccessor accessor) : ICancellationTokenProvider
{
    public CancellationToken Token => accessor.HttpContext?.RequestAborted ?? CancellationToken.None;
}
