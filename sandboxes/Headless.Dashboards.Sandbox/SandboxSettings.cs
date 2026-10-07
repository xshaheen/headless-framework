// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboards.Sandbox;

/// <summary>Where the sandbox keeps Jobs state.</summary>
public enum SandboxStore
{
    /// <summary>In-process storage: starts in seconds and forgets everything on restart.</summary>
    Memory = 0,

    /// <summary>The PostgreSQL started by <c>sandboxes/compose.yaml</c>, so stored state survives a restart.</summary>
    Postgres = 1,
}

/// <summary>How the sandbox secures the Jobs dashboard.</summary>
public enum SandboxJobsAuth
{
    /// <summary>No auth.</summary>
    None = 0,

    /// <summary>Basic auth: user <see cref="SandboxSettings.JobsDashboardUser"/>, password <see cref="SandboxSettings.JobsDashboardSecret"/>.</summary>
    Basic = 1,

    /// <summary>API key auth: the key is <see cref="SandboxSettings.JobsDashboardSecret"/>.</summary>
    ApiKey = 2,

    /// <summary>Custom auth: the validator accepts <see cref="SandboxSettings.JobsDashboardSecret"/> as the credential.</summary>
    Custom = 3,

    /// <summary>
    /// Host auth through a fake sandbox scheme: <c>Bearer &lt;secret&gt;</c> signs in an operator the dashboard policy
    /// admits, and <c>Bearer viewer-&lt;secret&gt;</c> a viewer it refuses.
    /// </summary>
    Host = 4,
}

/// <summary>
/// The <c>Sandbox</c> configuration section. The project CLI sets it through <c>Sandbox__*</c> environment variables;
/// nothing here is a production default.
/// </summary>
public sealed class SandboxSettings
{
    /// <summary>Jobs and Messaging storage. The messaging transport stays in memory either way.</summary>
    public SandboxStore Store { get; set; } = SandboxStore.Memory;

    /// <summary>PostgreSQL connection string, required when <see cref="Store"/> is <see cref="SandboxStore.Postgres"/>.</summary>
    public string? PostgresConnectionString { get; set; }

    /// <summary>
    /// Messaging dashboard Basic-auth user name. Inbox and scheduled operations need a named operator, so the
    /// messaging dashboard uses Basic auth whenever <see cref="MessagingDashboardPassword"/> is set.
    /// </summary>
    public string MessagingDashboardUser { get; set; } = "sandbox";

    /// <summary>Messaging dashboard Basic-auth password; when empty, that dashboard runs without auth.</summary>
    public string? MessagingDashboardPassword { get; set; }

    /// <summary>
    /// Jobs dashboard auth mode. The default is <see cref="SandboxJobsAuth.None"/>; the other modes make the sign-in,
    /// session-timeout, and live-update journeys walkable under that mode.
    /// </summary>
    public SandboxJobsAuth JobsDashboardAuth { get; set; } = SandboxJobsAuth.None;

    /// <summary>Jobs dashboard Basic-auth user name.</summary>
    public string JobsDashboardUser { get; set; } = "sandbox";

    /// <summary>
    /// The Jobs dashboard password, API key, or custom credential, by <see cref="JobsDashboardAuth"/>. When empty, it
    /// falls back to <see cref="MessagingDashboardPassword"/>, so the one generated secret signs in to both.
    /// </summary>
    public string? JobsDashboardSecret { get; set; }

    /// <summary>Jobs dashboard session timeout in minutes; when unset, the dashboard default applies.</summary>
    public int? JobsDashboardSessionTimeoutMinutes { get; set; }
}
