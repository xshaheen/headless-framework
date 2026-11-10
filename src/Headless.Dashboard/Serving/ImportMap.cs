// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// Renders the shell document's import map: the bare specifiers module code imports (React, React DOM, the React
/// compiler runtime, and each SDK subpath) mapped to the shell's own builds of them, so every module shares one copy.
/// </summary>
/// <remarks>
/// The map is rendered per request because its URLs are absolute paths under the request's <c>PathBase</c>; a hash
/// of it in the policy would change with every prefix, so the map carries the response's nonce instead.
/// </remarks>
internal static class ImportMap
{
    /// <summary>Returns the map's JSON, escaped for an inline <c>&lt;script type="importmap"&gt;</c> block.</summary>
    /// <param name="imports">Each specifier's file, relative to the shell's build output, from <c>shell.json</c>.</param>
    /// <param name="prefix">The dashboard's absolute path for this request.</param>
    public static string Render(IReadOnlyDictionary<string, string> imports, string prefix)
    {
        var resolved = new Dictionary<string, string>(imports.Count, StringComparer.Ordinal);

        foreach (var (specifier, path) in imports)
        {
            resolved[specifier] = prefix + "/" + path;
        }

        return JsonSerializer.Serialize(new ImportMapDocument { Imports = resolved }, ShellJson.ImportMapInfo);
    }
}

/// <summary>The import map's JSON shape.</summary>
internal sealed record ImportMapDocument
{
    public required IReadOnlyDictionary<string, string> Imports { get; init; }
}
