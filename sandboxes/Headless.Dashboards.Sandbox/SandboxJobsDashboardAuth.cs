// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;

namespace Headless.Dashboards.Sandbox;

/// <summary>Applies <see cref="SandboxSettings.JobsDashboardAuth"/> to the Jobs dashboard builder.</summary>
internal static class SandboxJobsDashboardAuth
{
    public static void Configure(DashboardOptionsBuilder dashboard, SandboxSettings sandbox)
    {
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
                dashboard.WithHostAuthentication(host =>
                {
                    host.ViewPolicy = SandboxHostAuthentication.ViewPolicy;
                    host.OperatePolicy = SandboxHostAuthentication.OperatePolicy;
                });
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown Sandbox__JobsDashboardAuth '{sandbox.JobsDashboardAuth}'."
                );
        }
    }

    /// <summary>
    /// The Jobs secret, falling back to the Messaging password so one generated secret serves both. The sandbox host
    /// token behind the Headless dashboard's host mode uses it too.
    /// </summary>
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
                "The Headless dashboard's host sign-in and "
                    + $"Sandbox__JobsDashboardAuth={sandbox.JobsDashboardAuth} need Sandbox__JobsDashboardSecret or "
                    + "Sandbox__MessagingDashboardPassword."
            );
        }

        return secret;
    }
}
