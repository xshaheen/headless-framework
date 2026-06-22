// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings.Values;

/// <summary>Well-known provider name constants used to address specific setting value providers.</summary>
public static class SettingValueProviderNames
{
    /// <summary>Provider that supplies the static default value declared on the <see cref="Headless.Settings.Models.SettingDefinition"/>.</summary>
    public const string DefaultValue = "DefaultValue";

    /// <summary>Provider that reads values from the application's <c>IConfiguration</c> (e.g. appsettings.json, env vars).</summary>
    public const string Configuration = "Configuration";

    /// <summary>Provider that stores and retrieves settings at the global (application-wide) scope.</summary>
    public const string Global = "Global";

    /// <summary>Provider that stores and retrieves settings scoped to a specific tenant.</summary>
    public const string Tenant = "Tenant";

    /// <summary>Provider that stores and retrieves settings scoped to a specific user.</summary>
    public const string User = "User";
}
