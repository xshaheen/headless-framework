// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Text.Json;
using Headless.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace Headless.Api;

/// <summary>
/// Masks the text of a problem-details body written for an exception, so a response carries the shapes the app's text
/// redactors mask in its logs.
/// </summary>
/// <remarks>
/// ASP.NET Core's developer exception page answers a JSON client with the exception's message as <c>detail</c> and its
/// full <see cref="Exception.ToString"/>, the request headers, path, and route values under the <c>exception</c>
/// extension; an app's own writer can put exception text in any member. So every string of an exception's body is
/// masked: the title, the detail, and each string inside an extension.
/// </remarks>
internal static class ProblemDetailsExceptionText
{
    public static void Mask(ProblemDetails problemDetails, ILogTextMasker masker)
    {
        if (problemDetails.Title is { } title)
        {
            problemDetails.Title = masker.Mask(title);
        }

        if (problemDetails.Detail is { } detail)
        {
            problemDetails.Detail = masker.Mask(detail);
        }

        if (problemDetails.Extensions.Count == 0)
        {
            return;
        }

        // Copied first: the loop replaces values in the dictionary it reads.
        foreach (var (key, value) in problemDetails.Extensions.ToArray())
        {
            switch (value)
            {
                case string text:
                    problemDetails.Extensions[key] = masker.Mask(text);
                    break;
                case JsonElement element when _HasText(element):
                    problemDetails.Extensions[key] = _Mask(element, masker);
                    break;
            }
        }
    }

    private static bool _HasText(JsonElement element) =>
        element.ValueKind is JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array;

    private static JsonElement _Mask(JsonElement element, ILogTextMasker masker)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            _Write(writer, element, masker);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);

        return document.RootElement.Clone();
    }

    private static void _Write(Utf8JsonWriter writer, JsonElement element, ILogTextMasker masker)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    _Write(writer, property.Value, masker);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();

                foreach (var item in element.EnumerateArray())
                {
                    _Write(writer, item, masker);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(masker.Mask(element.GetString()!));
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
