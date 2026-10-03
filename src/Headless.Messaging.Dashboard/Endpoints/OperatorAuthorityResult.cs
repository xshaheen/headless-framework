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

internal sealed record OperatorAuthorityResult(
    OperatorAuthorityStatus Status,
    OperatorAuthorizationContext? Authorization
)
{
    [MemberNotNullWhen(true, nameof(Authorization))]
    public bool IsSuccess => Status == OperatorAuthorityStatus.Success && Authorization is not null;

    public static OperatorAuthorityResult Success(OperatorAuthorizationContext authorization) =>
        new(OperatorAuthorityStatus.Success, authorization);

    public static OperatorAuthorityResult Unauthenticated() =>
        new(OperatorAuthorityStatus.Unauthenticated, Authorization: null);

    public static OperatorAuthorityResult PlaceholderActor() =>
        new(OperatorAuthorityStatus.PlaceholderActor, Authorization: null);
}
