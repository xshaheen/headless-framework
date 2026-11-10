// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Headless.Jobs;

/// <summary>
/// The Jobs entries a node publishes in its Coordination node metadata, which registration fixes for the node's
/// lifetime, as a node's run filter is.
/// </summary>
internal static class JobsNodeMetadata
{
    /// <summary>
    /// Membership metadata key carrying a JSON array of the job functions this node claims and runs, ordered
    /// ordinally: the registered functions its run filter allows, or none when it claims no work. A function no live
    /// node lists is one nothing will run.
    /// </summary>
    internal const string ClaimedFunctionsKey = "headless.jobs.claimed-functions";

    /// <summary>Formats <paramref name="functions"/> as the value of <see cref="ClaimedFunctionsKey"/>.</summary>
    public static string FormatClaimedFunctions(IEnumerable<string> functions)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();

            foreach (var function in functions)
            {
                writer.WriteStringValue(function);
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Reads the functions a node's metadata says it claims.</summary>
    /// <param name="metadata">The node's metadata.</param>
    /// <param name="functions">The functions, or empty when the node publishes none or the value is malformed.</param>
    /// <returns>
    /// <see langword="true"/> when the node publishes the key with a well-formed value; <see langword="false"/> for a
    /// node that is not a Jobs node, or runs a build that does not publish it.
    /// </returns>
    public static bool TryReadClaimedFunctions(
        IReadOnlyDictionary<string, string> metadata,
        out IReadOnlyList<string> functions
    )
    {
        functions = [];

        if (!metadata.TryGetValue(ClaimedFunctionsKey, out var value))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(value);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var read = new List<string>(document.RootElement.GetArrayLength());

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                read.Add(element.GetString()!);
            }

            functions = read;

            return true;
        }
        catch (JsonException)
        {
            // Another node's metadata is input this node does not control; a malformed value reads as unpublished.
            return false;
        }
    }

    /// <summary>
    /// Membership metadata key carrying the node's host max concurrency, an invariant-culture integer, so a dashboard
    /// served by another node can show how many runs this one may run at once.
    /// </summary>
    internal const string MaxConcurrencyKey = "headless.jobs.max-concurrency";

    /// <summary>Reads the host max concurrency a node's metadata publishes.</summary>
    /// <param name="metadata">The node's metadata.</param>
    /// <returns>The max concurrency, or <see langword="null"/> when the node publishes none or the value is malformed.</returns>
    public static int? ReadMaxConcurrency(IReadOnlyDictionary<string, string> metadata)
    {
        return
            metadata.TryGetValue(MaxConcurrencyKey, out var value)
            && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var maxConcurrency)
            ? maxConcurrency
            : null;
    }
}
