// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Data.Sqlite;

namespace Headless.Dashboards.Sandbox;

/// <summary>Where the sandbox keeps Jobs state.</summary>
public enum SandboxStore
{
    /// <summary>In-process storage: starts in seconds and forgets everything on restart.</summary>
    Memory = 0,

    /// <summary>The PostgreSQL started by <c>sandboxes/compose.yaml</c>, so stored state survives a restart.</summary>
    Postgres = 1,
}

/// <summary>How a person signs in to the sandbox's Headless dashboard.</summary>
public enum SandboxAuth
{
    /// <summary>
    /// The dashboard's own sign-in page with the fixed user name and password <c>admin</c>/<c>admin</c>. One operator,
    /// so every action is allowed; the quickest way to drive the dashboard on loopback.
    /// </summary>
    Basic = 0,

    /// <summary>
    /// The stand-in for an application's own sign-in (<see cref="SandboxHostAuthentication"/>): a secret signs in an
    /// operator and <c>viewer-</c> plus the secret a viewer, so the view-only states can be walked.
    /// </summary>
    Host = 1,
}

/// <summary>
/// The <c>Sandbox</c> configuration section. The project CLI sets it through <c>Sandbox__*</c> environment variables;
/// nothing here is a production default.
/// </summary>
public sealed class SandboxSettings
{
    /// <summary>Jobs and Messaging storage. The messaging transport stays in memory either way.</summary>
    public SandboxStore Store { get; set; } = SandboxStore.Memory;

    /// <summary>How the Headless dashboard signs people in; <see cref="SandboxAuth.Basic"/> unless set.</summary>
    public SandboxAuth Auth { get; set; } = SandboxAuth.Basic;

    /// <summary>PostgreSQL connection string, required when <see cref="Store"/> is <see cref="SandboxStore.Postgres"/>.</summary>
    public string? PostgresConnectionString { get; set; }

    /// <summary>
    /// The SQLite file the sequence counters live in, whatever <see cref="Store"/> is: no in-memory sequences provider
    /// exists, and one loopback process makes a file enough. <c>make up</c> puts it in the checkout's sandbox folder;
    /// without it the file sits in the system temporary folder.
    /// </summary>
    public string? SequencesDatabasePath { get; set; }

    /// <summary>The SQLite connection string of <see cref="SequencesDatabasePath"/>, or of its fallback.</summary>
    public string SequencesConnectionString()
    {
        var path = string.IsNullOrEmpty(SequencesDatabasePath)
            ? Path.Combine(Path.GetTempPath(), "headless-dashboards-sandbox", "sequences.db")
            : SequencesDatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        return new SqliteConnectionStringBuilder { DataSource = path }.ToString();
    }

    /// <summary>
    /// The secret <c>make up</c> generates into <c>sandbox.env</c>; the host sign-in falls back to it when
    /// <see cref="HostSecret"/> is empty.
    /// </summary>
    public string? MessagingDashboardPassword { get; set; }

    /// <summary>
    /// The secret behind the sandbox host sign-in of the Headless dashboard. When empty, it falls back to
    /// <see cref="MessagingDashboardPassword"/>, the secret <c>make up</c> generates.
    /// </summary>
    public string? HostSecret { get; set; }

    /// <summary>The host sign-in secret: <see cref="HostSecret"/>, else <see cref="MessagingDashboardPassword"/>.</summary>
    /// <exception cref="InvalidOperationException">Neither is set.</exception>
    public string ResolveHostSecret()
    {
        var secret = string.IsNullOrEmpty(HostSecret) ? MessagingDashboardPassword : HostSecret;

        return string.IsNullOrEmpty(secret)
            ? throw new InvalidOperationException(
                "The Headless dashboard's host sign-in needs Sandbox__HostSecret or Sandbox__MessagingDashboardPassword."
            )
            : secret;
    }
}
