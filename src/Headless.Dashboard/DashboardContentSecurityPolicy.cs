// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// The sources the host adds to the dashboard's Content Security Policy, for example the origin of a host-provided
/// image or an API a module calls.
/// </summary>
/// <remarks>
/// The shell's own policy stays strict: scripts load only through a per-response nonce with <c>'strict-dynamic'</c>,
/// so a script source added here does not load a parser-inserted script; module code loads through <c>import()</c>.
/// The dashboard's policy replaces any CSP header the host sets on the same response.
/// </remarks>
[PublicAPI]
public sealed class DashboardContentSecurityPolicy
{
    /// <summary>Gets or sets the sources added to <c>script-src</c>.</summary>
    public IList<string> ScriptSources { get; set; } = [];

    /// <summary>Gets or sets the sources added to <c>style-src</c>.</summary>
    public IList<string> StyleSources { get; set; } = [];

    /// <summary>Gets or sets the sources added to <c>img-src</c>.</summary>
    public IList<string> ImageSources { get; set; } = [];

    /// <summary>Gets or sets the sources added to <c>font-src</c>.</summary>
    public IList<string> FontSources { get; set; } = [];

    /// <summary>Gets or sets the sources added to <c>connect-src</c>.</summary>
    public IList<string> ConnectSources { get; set; } = [];
}
