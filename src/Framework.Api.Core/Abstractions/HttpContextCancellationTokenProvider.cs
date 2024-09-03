using Framework.Kernel.BuildingBlocks.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Framework.Api.Core.Abstractions;

public sealed class HttpContextCancellationTokenProvider(IHttpContextAccessor accessor) : ICancellationTokenProvider
{
    public CancellationToken Token => accessor.HttpContext?.RequestAborted ?? CancellationToken.None;
}
