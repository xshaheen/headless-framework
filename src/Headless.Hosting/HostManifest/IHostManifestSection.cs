// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;

namespace Headless.Hosting;

/// <summary>
/// Writes one feature's section of the host manifest: what the feature registered in this host, read from the same
/// frozen registry the host runs.
/// </summary>
/// <remarks>
/// <para>
/// A feature registers its section as a singleton <see cref="IHostManifestSection" />, typically with
/// <c>TryAddEnumerable</c>. The host manifest is printed by <c>RunHeadlessAsync</c> when the host's first argument
/// is <c>describe</c>, after the startup validation a real start would run and without starting any hosted service.
/// <c>MapHeadlessHostManifest</c> in <c>Headless.Api.ServiceDefaults</c> serves the same document over HTTP, behind
/// authorization, and <see cref="HostManifest" /> writes it for both, so the two forms never differ.
/// </para>
/// <para>
/// The document is <c>{"schemaVersion":1,"sections":{...}}</c>, with one property per section named by
/// <see cref="Name" /> and sorted ordinally. Two sections with the same section name fail the manifest.
/// </para>
/// <para>
/// Read only state that exists once the host is built: never start a hosted service, open a connection, or call a
/// broker. Write entries in a stable order and omit a setting that does not apply to an entry instead of writing
/// <see langword="null" />, so two manifests of the same configuration diff cleanly. Never write connection strings,
/// broker addresses, credentials, or provider-specific transport configuration: the manifest is meant to be
/// printed and shared.
/// </para>
/// </remarks>
[PublicAPI]
public interface IHostManifestSection
{
    /// <summary>Gets the name of the section this section writes, such as <c>messaging</c>.</summary>
    string Name { get; }

    /// <summary>Writes the section's value: exactly one complete JSON value, normally an object.</summary>
    /// <param name="writer">The writer, positioned after the section's property name.</param>
    void Write(Utf8JsonWriter writer);
}
