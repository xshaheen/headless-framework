// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// A module's <c>manifest.json</c>, as the SDK's Vite plugin writes it beside the module's entry: everything static
/// the module declares, so the shell renders the sidebar, routes, and the palette without running module code.
/// </summary>
/// <remarks>
/// Unknown properties are ignored, so a newer plugin's additions do not break an older shell. The defaulted collections
/// have setters, not <see langword="init"/>: the source-generated reader assigns every <see langword="init"/> property, so a list missing
/// from the file would replace its empty default with <see langword="null"/>.
/// </remarks>
internal sealed record ModuleManifest
{
    /// <summary>The file name of the manifest at the root of a module's assets.</summary>
    public const string FileName = "manifest.json";

    public string? Id { get; init; }

    /// <summary>The dashboard SDK version the module was built against, such as <c>1.2.0</c>.</summary>
    public string? SdkVersion { get; init; }

    /// <summary>The entry chunk's path, relative to the module's assets.</summary>
    public string? Entry { get; init; }

    public IReadOnlyList<string> Css { get; set; } = [];

    /// <summary>The id of the product whose tab the module joins, one of <see cref="DashboardProducts.All"/>.</summary>
    public string? Product { get; init; }

    /// <summary>The module's sections in its product's sidebar, in display order.</summary>
    public IReadOnlyList<ModuleManifestSidebarSection> Sidebar { get; set; } = [];

    public IReadOnlyList<ModuleManifestRoute> Routes { get; set; } = [];

    /// <summary>Each object kind's client-side description, passed to the browser as the plugin wrote it.</summary>
    public IReadOnlyDictionary<string, JsonElement> ObjectKinds { get; set; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    public IReadOnlyList<ModuleManifestCommand> Commands { get; set; } = [];

    public IReadOnlyList<string> Events { get; set; } = [];
}

/// <summary>One section of a module's sidebar: an optional heading and its entries.</summary>
internal sealed record ModuleManifestSidebarSection
{
    /// <summary>The heading, or <see langword="null"/> for a section drawn without one.</summary>
    public string? Label { get; init; }

    public IReadOnlyList<ModuleManifestSidebarEntry> Entries { get; set; } = [];
}

/// <summary>One sidebar entry. Its id is a contribution id, so a host disables or orders it.</summary>
internal sealed record ModuleManifestSidebarEntry
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    /// <summary>The route the entry opens, relative to the module, with an optional query.</summary>
    public required string Route { get; init; }

    /// <summary>The icon key the entry leads with, or <see langword="null"/> for an entry with a state dot.</summary>
    public string? Icon { get; init; }

    /// <summary>The colour role of the entry's state dot, such as <c>fail</c>, or <see langword="null"/>.</summary>
    public string? State { get; init; }

    /// <summary>The key of the entry's count in the module's <see cref="IDashboardCountSource"/> answer.</summary>
    public string? CountKey { get; init; }

    /// <summary><c>view</c>, <c>operate</c>, or <see langword="null"/> for view.</summary>
    public string? Requires { get; init; }
}

internal sealed record ModuleManifestRoute
{
    public required string Path { get; init; }

    /// <summary>The route's chunk path, relative to the module's assets.</summary>
    public string? Chunk { get; init; }
}

/// <summary>A client command: navigation only, such as <c>Add time job</c>. Server commands are declared in .NET.</summary>
internal sealed record ModuleManifestCommand
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    public string? Requires { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true
)]
[JsonSerializable(typeof(ModuleManifest))]
internal sealed partial class ModuleManifestJsonContext : JsonSerializerContext;
