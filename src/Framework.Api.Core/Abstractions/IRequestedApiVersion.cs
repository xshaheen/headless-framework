using Microsoft.AspNetCore.Http;

namespace Framework.Api.Core.Abstractions;

public interface IRequestedApiVersion
{
    public string? Current { get; }
}

public sealed class HttpContextRequestedApiVersion(IHttpContextAccessor accessor) : IRequestedApiVersion
{
    public string? Current => accessor.HttpContext?.GetRequestedApiVersion()?.ToString();
}
