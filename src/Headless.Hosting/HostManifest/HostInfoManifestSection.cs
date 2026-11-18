// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Hosting;

/// <summary>
/// Writes the <c>host</c> section every host manifest carries: which application and build this is, so two manifests
/// can be told apart and matched to a deployment, and the isolation prefix its shared-store names start with, when it
/// has one.
/// </summary>
/// <remarks>
/// The versions are informational versions, which carry the source revision when the build stamps one. Nothing here
/// names a machine or an address: the application name and environment come from the host environment, not the
/// process.
/// </remarks>
internal sealed class HostInfoManifestSection(IServiceProvider services) : IHostManifestSection
{
    public string Name => "host";

    public void Write(Utf8JsonWriter writer)
    {
        var environment = services.GetService<IHostEnvironment>();

        writer.WriteStartObject();

        if (environment is not null)
        {
            writer.WriteString("applicationName", environment.ApplicationName);
            writer.WriteString("environment", environment.EnvironmentName);
        }

        if (_Version(Assembly.GetEntryAssembly()) is { } applicationVersion)
        {
            writer.WriteString("applicationVersion", applicationVersion);
        }

        if (_Version(typeof(HostInfoManifestSection).Assembly) is { } headlessVersion)
        {
            writer.WriteString("headlessVersion", headlessVersion);
        }

        writer.WriteString("runtime", RuntimeInformation.FrameworkDescription);

        if (services.GetService<IOptions<HeadlessPrefixOptions>>()?.Value.Prefix is { Length: > 0 } prefix)
        {
            writer.WriteString("prefix", prefix);
        }

        writer.WriteEndObject();
    }

    private static string? _Version(Assembly? assembly) =>
        assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly?.GetName().Version?.ToString();
}
