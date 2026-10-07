// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Jobs;

namespace Headless.Dashboards.Sandbox;

/// <summary>Applies <see cref="SandboxSettings.JobsDashboardAuth"/> to the Jobs dashboard builder.</summary>
internal static class SandboxJobsDashboardAuth
{
    public static void Configure(DashboardOptionsBuilder dashboard, SandboxSettings sandbox)
    {
        if (sandbox.JobsDashboardSessionTimeoutMinutes is { } minutes)
        {
            dashboard.WithSessionTimeout(minutes);
        }

        if (sandbox.JobsDashboardAuth == SandboxJobsAuth.None)
        {
            dashboard.WithNoAuth();
            return;
        }

        var secret = Secret(sandbox);

        switch (sandbox.JobsDashboardAuth)
        {
            case SandboxJobsAuth.Basic:
                dashboard.WithBasicAuth(sandbox.JobsDashboardUser, secret);
                break;
            case SandboxJobsAuth.ApiKey:
                dashboard.WithApiKey(secret);
                break;
            case SandboxJobsAuth.Host:
                dashboard.WithHostAuthentication(SandboxHostAuthentication.Policy);
                break;
            case SandboxJobsAuth.Custom:
                var expected = Encoding.UTF8.GetBytes(secret);
                dashboard.WithCustomAuth(
                    (credential, _) =>
                        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(credential), expected)
                );
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown Sandbox__JobsDashboardAuth '{sandbox.JobsDashboardAuth}'."
                );
        }
    }

    /// <summary>The Jobs secret, falling back to the Messaging password so one generated secret serves both.</summary>
    public static string Secret(SandboxSettings sandbox)
    {
        var secret = sandbox.JobsDashboardSecret;
        if (string.IsNullOrEmpty(secret))
        {
            secret = sandbox.MessagingDashboardPassword;
        }

        if (string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException(
                $"Sandbox__JobsDashboardAuth={sandbox.JobsDashboardAuth} needs Sandbox__JobsDashboardSecret or "
                    + "Sandbox__MessagingDashboardPassword."
            );
        }

        return secret;
    }
}
