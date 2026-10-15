// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Headless.Hosting;

/// <summary>
/// Applies, in order, the contributing package's defaults and the <c>Headless:HealthChecks:{name}</c> configuration
/// section to the options of the contributed check with that name, so a deployment can turn a check off or change its
/// failure status without a code change.
/// </summary>
/// <remarks>
/// It is registered ahead of every other configuration of <see cref="HeadlessHealthCheckOptions" />, so a
/// <c>ConfigureHeadlessHealthCheck</c> call in code wins over configuration, and configuration over a package default,
/// whichever is registered first. The keys are read one by one instead of through the reflection binder, which keeps
/// the package trimming-safe.
/// </remarks>
internal sealed class HeadlessHealthCheckConfiguration(
    HeadlessHealthCheckDefaults defaults,
    IConfiguration? configuration
) : IConfigureNamedOptions<HeadlessHealthCheckOptions>
{
    public const string SectionPrefix = "Headless:HealthChecks:";

    public void Configure(HeadlessHealthCheckOptions options)
    {
        // Unnamed options belong to no check.
    }

    public void Configure(string? name, HeadlessHealthCheckOptions options)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (defaults.TryGetValue(name, out var configureDefaults))
        {
            foreach (var configureDefault in configureDefaults)
            {
                configureDefault(options);
            }
        }

        if (configuration is null)
        {
            return;
        }

        var section = configuration.GetSection(SectionPrefix + name);

        if (section["Enabled"] is { } enabled)
        {
            options.Enabled = bool.TryParse(enabled, out var value)
                ? value
                : throw _Invalid(section, "Enabled", enabled);
        }

        if (section["FailureStatus"] is { } failureStatus)
        {
            options.FailureStatus = Enum.TryParse<HealthStatus>(failureStatus, ignoreCase: true, out var value)
                ? value
                : throw _Invalid(section, "FailureStatus", failureStatus);
        }

        if (section["Timeout"] is { } timeout)
        {
            options.Timeout = TimeSpan.TryParse(timeout, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw _Invalid(section, "Timeout", timeout);
        }

        if (section["TestCommand"] is { } testCommand)
        {
            options.TestCommand = testCommand;
        }

        foreach (var tag in section.GetSection("Tags").GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(tag.Value))
            {
                options.Tags.Add(tag.Value);
            }
        }
    }

    private static InvalidOperationException _Invalid(IConfigurationSection section, string key, string value) =>
        new($"Configuration value '{value}' at '{section.Path}:{key}' is not valid for a health check's {key}.");
}

/// <summary>The defaults each contributing package set for its checks, by check name, in registration order.</summary>
internal sealed class HeadlessHealthCheckDefaults()
    : Dictionary<string, List<Action<HeadlessHealthCheckOptions>>>(StringComparer.Ordinal);
