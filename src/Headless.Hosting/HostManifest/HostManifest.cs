// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Text.Json;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Hosting;

/// <summary>
/// Writes the host manifest document: the built-in <c>host</c> and <c>healthChecks</c> sections, then every registered
/// <see cref="IHostManifestSection" />.
/// </summary>
/// <remarks>
/// The <c>describe</c> command of <c>RunHeadlessAsync</c> and the HTTP manifest endpoint both write through this
/// type, so the two forms always serve the same document.
/// </remarks>
[PublicAPI]
public static class HostManifest
{
    /// <summary>The version of the document's shape; raise it when a change would break a reader of the old shape.</summary>
    public const int SchemaVersion = 1;

    /// <summary>
    /// Writes the manifest built from the built-in <c>host</c> and <c>healthChecks</c> sections and every
    /// <see cref="IHostManifestSection" /> registered in <paramref name="services" /> to <paramref name="destination" />
    /// as UTF-8 JSON.
    /// </summary>
    /// <param name="services">The built host's services, which hold the sections.</param>
    /// <param name="destination">The stream that receives the document.</param>
    /// <param name="cancellationToken">Abandons the copy to <paramref name="destination" />.</param>
    /// <remarks>
    /// The whole document is built in memory before the first byte reaches <paramref name="destination" />, so a
    /// failure leaves the stream untouched and the caller free to send an error instead.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services" /> or <paramref name="destination" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Two sections share a name (a registered section named <c>host</c> or <c>healthChecks</c> included), or a section
    /// failed; the message names the section and may carry the section's own error text, so do not show it to an
    /// untrusted caller.
    /// </exception>
    public static async Task WriteAsync(
        IServiceProvider services,
        Stream destination,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(destination);

        var document = _Write([
            new HostInfoManifestSection(services),
            new HealthChecksManifestSection(services),
            .. services.GetServices<IHostManifestSection>(),
        ]);

        await destination.WriteAsync(document, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the whole document into memory and returns its UTF-8 bytes.</summary>
    /// <remarks>
    /// The document is buffered so a section that throws leaves the caller nothing half-written to send.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Two sections share a name, or a section failed; the message names the section.
    /// </exception>
    private static byte[] _Write(IEnumerable<IHostManifestSection> sections)
    {
        var ordered = sections.OrderBy(section => section.Name, StringComparer.Ordinal).ToList();

        for (var i = 1; i < ordered.Count; i++)
        {
            if (string.Equals(ordered[i - 1].Name, ordered[i].Name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Two host manifest sections write the '{ordered[i].Name}' section: "
                        + $"{ordered[i - 1].GetType().FullName} and {ordered[i].GetType().FullName}."
                );
            }
        }

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteStartObject("sections");

            foreach (var section in ordered)
            {
                writer.WritePropertyName(section.Name);
                _WriteOneSection(writer, section);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void _WriteOneSection(Utf8JsonWriter writer, IHostManifestSection section)
    {
        try
        {
            section.Write(writer);
        }
#pragma warning disable CA1031 // False positive: the failure is rethrown, wrapped only to name the section that failed.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            throw new InvalidOperationException(
                $"The '{section.Name}' host manifest section failed: {exception.Message}",
                exception
            );
        }
    }
}
