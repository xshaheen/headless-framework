using System.Security.Claims;
using Framework.BuildingBlocks.Abstractions;
using Framework.BuildingBlocks.Constants;
using Framework.BuildingBlocks.Primitives;
using Microsoft.AspNetCore.Http;

namespace Framework.Api.Core.Abstractions;

public interface IRequestContext
{
    /// <summary>Get ClaimsPrincipal.</summary>
    /// <exception cref="InvalidOperationException">HttpContext not available.</exception>
    ClaimsPrincipal Principal { get; }

    /// <summary>Get Unique request identifier.</summary>
    string TraceIdentifier { get; }

    /// <summary>Get UserId.</summary>
    /// <exception cref="InvalidOperationException">HttpContext not available.</exception>
    UserId? UserId { get; }

    /// <summary>Get account type.</summary>
    /// <exception cref="InvalidOperationException">HttpContext not available.</exception>
    string? UserType { get; }

    /// <summary>Get AccountId.</summary>
    /// <exception cref="InvalidOperationException">HttpContext not available.</exception>
    AccountId? AccountId { get; }

    /// <summary>Get TenantId.</summary>
    /// <exception cref="InvalidOperationException">HttpContext not available.</exception>
    Guid? TenantId { get; }

    /// <summary>Get request CorrelatedId.</summary>
    /// <exception cref="InvalidOperationException">HttpContext not available.</exception>
    string? CorrelationId { get; }

    /// <summary>Get IpAddress.</summary>
    /// <exception cref="InvalidOperationException">HttpContext not available.</exception>
    string? IpAddress { get; }

    /// <summary>Get UserAgent.</summary>
    /// <exception cref="InvalidOperationException">HttpContext not available.</exception>
    string? UserAgent { get; }

    /// <summary>Get EndpointName.</summary>
    /// <exception cref="InvalidOperationException">HttpContext not available.</exception>
    string? EndpointName { get; }

    /// <summary>Current user roles.</summary>
    /// <exception cref="InvalidOperationException">HttpContext not available.</exception>
    IReadOnlyList<string> Roles { get; }

    /// <summary>Returns <see langword="true"/> if the HttpContext is available, otherwise false.</summary>
    /// <exception cref="InvalidOperationException">HttpContext not available.</exception>
    bool IsAvailable { get; }

    /// <summary>Get DateTimeOffset at the moment of the request.</summary>
    DateTimeOffset DateStarted { get; }
}

public sealed class HttpRequestContext(IHttpContextAccessor accessor, IClock clock) : IRequestContext
{
    private HttpContext HttpContext =>
        accessor.HttpContext ?? throw new InvalidOperationException("User context is not available");

    public ClaimsPrincipal Principal => HttpContext.User;

    public string TraceIdentifier => HttpContext.TraceIdentifier;

    public UserId? UserId => Principal.GetUserId();

    public AccountId? AccountId => Principal.GetAccountId();

    public string? UserType => Principal.GetUserType();

    public Guid? TenantId
    {
        get
        {
            var id = Principal.FindFirst(PlatformClaimTypes.TenantId)?.Value;

            return id is null
                ? null
                : Guid.TryParse(id, out var guid)
                    ? guid
                    : null;
        }
    }

    public string? CorrelationId => HttpContext.GetCorrelationId();

    public string? IpAddress => HttpContext.GetIpAddress();

    public string? UserAgent => HttpContext.GetUserAgent();

    public string? EndpointName => HttpContext.GetEndpoint()?.DisplayName;

    public IReadOnlyList<string> Roles => Principal.GetRoles();

    public bool IsAvailable => accessor.HttpContext is not null;

    public DateTimeOffset DateStarted { get; } = clock.Now;
}
