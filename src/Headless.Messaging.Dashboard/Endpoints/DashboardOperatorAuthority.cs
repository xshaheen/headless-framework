// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Headless.Dashboard.Authentication;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Microsoft.AspNetCore.Http;

namespace Headless.Messaging.Dashboard;

internal enum OperatorAuthorityStatus
{
    Success,
    Unauthenticated,
    PlaceholderActor,
}

internal sealed record OperatorAuthorityResult(
    OperatorAuthorityStatus Status,
    OperatorAuthorizationContext? Authorization
)
{
    [MemberNotNullWhen(true, nameof(Authorization))]
    public bool IsSuccess => Status == OperatorAuthorityStatus.Success && Authorization is not null;

    public static OperatorAuthorityResult Success(OperatorAuthorizationContext authorization) =>
        new(OperatorAuthorityStatus.Success, authorization);

    public static OperatorAuthorityResult Unauthenticated() => new(OperatorAuthorityStatus.Unauthenticated, null);

    public static OperatorAuthorityResult PlaceholderActor() => new(OperatorAuthorityStatus.PlaceholderActor, null);
}

public sealed record OperatorActorRequiredProblem(string Code, string Remedy, string Message);

internal static class DashboardOperatorAuthority
{
    public const string OperatorActorRequiredCode = "g:operator_actor_required";

    public const string OperatorActorRequiredRemedy =
        "Operator actions require an authenticated principal with a stable actor name. Configure Basic or Host authentication with a name or sub claim.";

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter<StatusName>(allowIntegerValues: false),
            new JsonStringEnumConverter<MessageLane>(allowIntegerValues: false),
            new JsonStringEnumConverter<MessagingOperationType>(allowIntegerValues: false),
            new JsonStringEnumConverter<InboxOperationOutcome>(allowIntegerValues: false),
        },
    };

    public static readonly OperatorAuthorizationContext HostAuthorizationContext = new(
        new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "headless-host")], "Host"))
    );

    public static OperatorAuthorityResult Resolve(HttpContext httpContext)
    {
        if (httpContext.User.Identity is not { IsAuthenticated: true } identity)
        {
            return OperatorAuthorityResult.Unauthenticated();
        }

        string? candidateActor = null;

        if (!string.IsNullOrWhiteSpace(identity.Name))
        {
            candidateActor = identity.Name;
        }
        else if (identity is ClaimsIdentity claimsIdentity)
        {
            candidateActor =
                claimsIdentity
                    .FindFirst(claim =>
                        claim.Type == ClaimTypes.NameIdentifier && !string.IsNullOrWhiteSpace(claim.Value)
                    )
                    ?.Value
                ?? claimsIdentity
                    .FindFirst(claim => claim.Type == "sub" && !string.IsNullOrWhiteSpace(claim.Value))
                    ?.Value;

            if (string.IsNullOrWhiteSpace(candidateActor))
            {
                candidateActor = httpContext.Items[AuthMiddleware.AuthenticatedKey] is true
                    ? httpContext.Items[AuthMiddleware.UsernameKey] as string
                    : null;
            }
        }

        if (
            string.IsNullOrWhiteSpace(candidateActor)
            || string.Equals(candidateActor, "anonymous", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidateActor, "host-user", StringComparison.OrdinalIgnoreCase)
        )
        {
            return OperatorAuthorityResult.PlaceholderActor();
        }

        var principal = new ClaimsPrincipal(httpContext.User.Identities.Select(static id => id.Clone()));
        var auditIdentity = (ClaimsIdentity)principal.Identity!;
        foreach (var claim in auditIdentity.FindAll(auditIdentity.NameClaimType).ToArray())
        {
            auditIdentity.RemoveClaim(claim);
        }
        auditIdentity.AddClaim(new Claim(auditIdentity.NameClaimType, candidateActor));

        return OperatorAuthorityResult.Success(new OperatorAuthorizationContext(principal));
    }

    public static IResult CreateForbiddenResult() =>
        Results.Json(
            new OperatorActorRequiredProblem(
                OperatorActorRequiredCode,
                OperatorActorRequiredRemedy,
                OperatorActorRequiredRemedy
            ),
            JsonOptions,
            statusCode: StatusCodes.Status403Forbidden
        );
}
