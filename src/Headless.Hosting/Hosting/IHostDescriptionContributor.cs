// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;

namespace Headless.Hosting;

/// <summary>
/// Writes one feature's section of the host description: what the feature registered in this host, read from the same
/// frozen registry the host runs.
/// </summary>
/// <remarks>
/// <para>
/// A feature registers its contributor as a singleton <see cref="IHostDescriptionContributor" />, typically with
/// <c>TryAddEnumerable</c>. The host description is printed by <c>RunHeadlessAsync</c> when the host's first argument
/// is <c>describe</c>, after the startup validation a real start would run and without starting any hosted service.
/// </para>
/// <para>
/// The document is <c>{"schemaVersion":1,"sections":{...}}</c>, with one property per contributor named by
/// <see cref="SectionName" /> and sorted ordinally. Two contributors with the same section name fail the description.
/// </para>
/// <para>
/// Read only state that exists once the host is built: never start a hosted service, open a connection, or call a
/// broker. Write entries in a stable order and omit a setting that does not apply to an entry instead of writing
/// <see langword="null" />, so two descriptions of the same configuration diff cleanly. Never write connection strings,
/// broker addresses, credentials, or provider-specific transport configuration: the description is meant to be
/// printed and shared.
/// </para>
/// </remarks>
[PublicAPI]
public interface IHostDescriptionContributor
{
    /// <summary>Gets the name of the section this contributor writes, such as <c>messaging</c>.</summary>
    string SectionName { get; }

    /// <summary>Writes the section's value: exactly one complete JSON value, normally an object.</summary>
    /// <param name="writer">The writer, positioned after the section's property name.</param>
    void WriteSection(Utf8JsonWriter writer);
}
