// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;
using Headless.Dashboard;
using Headless.Messaging.Dashboard;

namespace Tests;

/// <summary>
/// A dashboard configured for one of its own credentials, the <c>Authorization</c> header that passes it, and the actor
/// name the dashboard's scheme signs that caller in as.
/// </summary>
internal sealed record BuiltInCredential(
    MessagingDashboardOptionsBuilder Config,
    AuthenticationHeaderValue Header,
    string Actor
)
{
    public static BuiltInCredential For(AuthMode mode)
    {
        return mode switch
        {
            AuthMode.ApiKey => new(
                new MessagingDashboardOptionsBuilder().WithApiKey("secret"),
                new AuthenticationHeaderValue("Bearer", "secret"),
                "api-user"
            ),
            AuthMode.Basic => new(
                new MessagingDashboardOptionsBuilder().WithBasicAuth("ops", "pass"),
                new AuthenticationHeaderValue("Basic", "ops:pass".ToBase64()),
                "ops"
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Only API key and Basic are built in."),
        };
    }
}
