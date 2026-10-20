// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace Tests;

/// <summary>Checks that a served document is valid OpenAPI, beyond what its shape-specific assertions cover.</summary>
internal static class OpenApiDocumentValidity
{
    public static void ShouldBeValidOpenApi(this JsonNode document, string because)
    {
        // The reader validates with the default rule set and reports every violation as an error.
        var result = OpenApiDocument.Parse(document.ToJsonString(), OpenApiConstants.Json);
        result.Diagnostic.Should().NotBeNull(because);
        result.Diagnostic!.Errors.Select(error => $"{error.Pointer}: {error.Message}").Should().BeEmpty(because);

        _UnresolvedReferences(document, document).Should().BeEmpty($"every $ref resolves in {because}");
        _UnknownSecuritySchemes(document).Should().BeEmpty($"every security requirement names a scheme in {because}");
        _DuplicateParameters(document).Should().BeEmpty($"no operation repeats a parameter in {because}");
    }

    private static IEnumerable<string> _UnresolvedReferences(JsonNode root, JsonNode? node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var (key, value) in jsonObject)
                {
                    if (
                        string.Equals(key, "$ref", StringComparison.Ordinal)
                        && value is JsonValue reference
                        && reference.GetValue<string>() is var pointer
                        && _Resolve(root, pointer) is null
                    )
                    {
                        yield return pointer;
                    }

                    foreach (var unresolved in _UnresolvedReferences(root, value))
                    {
                        yield return unresolved;
                    }
                }

                break;
            case JsonArray jsonArray:
                foreach (var item in jsonArray)
                {
                    foreach (var unresolved in _UnresolvedReferences(root, item))
                    {
                        yield return unresolved;
                    }
                }

                break;
        }
    }

    private static JsonNode? _Resolve(JsonNode root, string pointer)
    {
        if (!pointer.StartsWith("#/", StringComparison.Ordinal))
        {
            return null;
        }

        var node = root;

        foreach (var token in pointer[2..].Split('/'))
        {
            var name = token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);

            if (node is not JsonObject jsonObject || !jsonObject.TryGetPropertyValue(name, out node))
            {
                return null;
            }
        }

        return node;
    }

    private static IEnumerable<string> _UnknownSecuritySchemes(JsonNode document)
    {
        var schemes =
            document["components"]?["securitySchemes"]?.AsObject().Select(x => x.Key).ToHashSet(StringComparer.Ordinal)
            ?? [];
        var requirements = _Operations(document)
            .SelectMany(operation => operation.Operation["security"]?.AsArray() ?? [])
            .Concat(document["security"]?.AsArray() ?? []);

        return requirements
            .SelectMany(requirement => requirement!.AsObject().Select(x => x.Key))
            .Where(name => !schemes.Contains(name))
            .Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<string> _DuplicateParameters(JsonNode document)
    {
        foreach (var (path, method, operation) in _Operations(document))
        {
            var pathParameters = document["paths"]![path]!["parameters"]?.AsArray() ?? [];
            var duplicates = (operation["parameters"]?.AsArray() ?? [])
                .Concat(pathParameters)
                .GroupBy(
                    parameter => $"{(string?)parameter!["in"]}:{((string?)parameter["name"])?.ToLowerInvariant()}",
                    StringComparer.Ordinal
                )
                .Where(group => group.Skip(1).Any())
                .Select(group => $"{method.ToUpperInvariant()} {path} {group.Key}");

            foreach (var duplicate in duplicates)
            {
                yield return duplicate;
            }
        }
    }

    private static IEnumerable<(string Path, string Method, JsonNode Operation)> _Operations(JsonNode document)
    {
        string[] methods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

        foreach (var (path, item) in document["paths"]?.AsObject() ?? [])
        {
            foreach (var (method, operation) in item!.AsObject())
            {
                if (methods.Contains(method, StringComparer.Ordinal))
                {
                    yield return (path, method, operation!);
                }
            }
        }
    }
}
