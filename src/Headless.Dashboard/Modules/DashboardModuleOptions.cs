// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// The access settings every dashboard module carries: the host policies that let a user view and operate that
/// module. Each module package derives its own options class from this one.
/// </summary>
/// <remarks>
/// <para>
/// Derive from this class to add a module's own settings, and register the derived options with the module
/// package's usual configuration overloads before calling
/// <see cref="HeadlessDashboardSetupBuilder.AddModule{TModule, TOptions}"/>.
/// </para>
/// <para>
/// The policies apply under host authentication and override the shell's: an empty <see cref="ViewPolicy"/> uses the
/// shell's view policy, and an empty <see cref="OperatePolicy"/> uses this module's <see cref="ViewPolicy"/> when it
/// is set, else the shell's operate policy. Basic and API-key authentication share one credential for every module
/// and ignore both.
/// </para>
/// </remarks>
[PublicAPI]
public class DashboardModuleOptions
{
    /// <summary>
    /// Gets or sets the host authorization policy a user must pass to see this module and read from it. When
    /// <see langword="null"/> or empty, the shell's view policy applies.
    /// </summary>
    public string? ViewPolicy { get; set; }

    /// <summary>
    /// Gets or sets the host authorization policy a user must pass to run this module's commands and other
    /// state-changing requests. When <see langword="null"/> or empty, <see cref="ViewPolicy"/> applies when it is set,
    /// else the shell's operate policy.
    /// </summary>
    public string? OperatePolicy { get; set; }
}
