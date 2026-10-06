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

/// <summary>
/// The <c>Sandbox</c> configuration section. The project CLI sets it through <c>Sandbox__*</c> environment variables;
/// nothing here is a production default.
/// </summary>
public sealed class SandboxSettings
{
    /// <summary>Jobs storage. Messaging always runs in memory.</summary>
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
}
