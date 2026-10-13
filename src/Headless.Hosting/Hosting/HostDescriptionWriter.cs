// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Text.Json;

namespace Headless.Hosting;

/// <summary>Writes the host description document from every registered <see cref="IHostDescriptionContributor" />.</summary>
internal static class HostDescriptionWriter
{
    /// <summary>The version of the document's shape; raise it when a change would break a reader of the old shape.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Writes the whole document into memory and returns its UTF-8 bytes.</summary>
    /// <remarks>
    /// The document is buffered so a contributor that throws leaves the caller nothing half-written to send.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Two contributors claim the same section, or a contributor failed; the message names the section.
    /// </exception>
    public static byte[] Write(IEnumerable<IHostDescriptionContributor> contributors)
    {
        var ordered = contributors.OrderBy(contributor => contributor.SectionName, StringComparer.Ordinal).ToList();

        for (var i = 1; i < ordered.Count; i++)
        {
            if (string.Equals(ordered[i - 1].SectionName, ordered[i].SectionName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Two host description contributors write the '{ordered[i].SectionName}' section: "
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

            foreach (var contributor in ordered)
            {
                writer.WritePropertyName(contributor.SectionName);
                _WriteSection(writer, contributor);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void _WriteSection(Utf8JsonWriter writer, IHostDescriptionContributor contributor)
    {
        try
        {
            contributor.WriteSection(writer);
        }
#pragma warning disable CA1031 // False positive: the failure is rethrown, wrapped only to name the section that failed.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            throw new InvalidOperationException(
                $"The '{contributor.SectionName}' host description section failed: {exception.Message}",
                exception
            );
        }
    }
}
