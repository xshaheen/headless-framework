// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using System.Text.Json.Serialization.Metadata;

namespace Headless.Dashboard;

/// <summary>
/// The page state the shell document inlines as a <c>&lt;script type="application/json"&gt;</c> block, so the first
/// render knows the session, the modules, and every URL without an API round trip.
/// </summary>
/// <remarks>
/// Every URL is an absolute path built from the request's <c>PathBase</c> plus the base path, because the document
/// has no <c>&lt;base&gt;</c> element. Only modules the session may view are listed, and server commands are left
/// out: the shell reads those per object from the command endpoints.
/// </remarks>
internal sealed record BootstrapState
{
    /// <summary>The router's basename: the dashboard's absolute path, with no trailing slash.</summary>
    public required string BasePath { get; init; }

    public required string ApiBase { get; init; }

    public required string StreamUrl { get; init; }

    /// <summary>The dashboard SDK version the shell serves, such as <c>1.0.0</c>.</summary>
    public required string SdkVersion { get; init; }

    /// <summary><c>none</c>, <c>basic</c>, <c>apiKey</c>, or <c>host</c>: which sign-in the sign-in page offers.</summary>
    public required string AuthMode { get; init; }

    public required BootstrapSession Session { get; init; }

    public string? TraceLinkTemplate { get; init; }

    public required IReadOnlyList<BootstrapModule> Modules { get; init; }

    /// <summary>Builds the state for one document response.</summary>
    /// <param name="prefix">The dashboard's absolute path for this request: <c>PathBase</c> plus the base path.</param>
    /// <param name="manifest">The merged manifest.</param>
    /// <param name="session">The request's session.</param>
    /// <param name="options">The shell's settings.</param>
    /// <param name="assets">The served build files, read for each module URL's content version.</param>
    public static BootstrapState Create(
        string prefix,
        MergedManifest manifest,
        DashboardSessionSnapshot session,
        HeadlessDashboardOptions options,
        DashboardAssetCatalog assets
    )
    {
        var modules = new List<BootstrapModule>(manifest.Modules.Count);

        foreach (var module in manifest.Modules)
        {
            if (!session.MayView(module.Id))
            {
                continue;
            }

            modules.Add(_CreateModule(prefix, module, session.MayOperate(module.Id), assets.FindModule(module.Id)));
        }

        return new BootstrapState
        {
            BasePath = prefix,
            ApiBase = prefix + "/api",
            StreamUrl = prefix + "/api/stream",
            SdkVersion = manifest.ShellSdkVersion.ToString(3),
            AuthMode = options.AuthMode switch
            {
                Dashboard.AuthMode.Basic => "basic",
                Dashboard.AuthMode.ApiKey => "apiKey",
                Dashboard.AuthMode.Host => "host",
                _ => "none",
            },
            Session = new BootstrapSession { Authenticated = session.Authenticated, Name = session.Name },
            TraceLinkTemplate = string.IsNullOrEmpty(options.TraceLinkTemplate) ? null : options.TraceLinkTemplate,
            Modules = modules,
        };
    }

    private static BootstrapModule _CreateModule(
        string prefix,
        MergedModule module,
        bool canOperate,
        EmbeddedAssetSet? files
    )
    {
        if (module.Manifest is not { } manifest)
        {
            return new BootstrapModule
            {
                Id = module.Id,
                DisplayName = module.DisplayName,
                CanOperate = false,
                Css = [],
                Nav = [],
                Routes = [],
                ObjectKinds = FrozenDictionary<string, JsonElement>.Empty,
                Commands = [],
                Events = [],
                Refusal = module.Refusal,
            };
        }

        var moduleBase = $"{prefix}/m/{module.Id}/";

        // A module's entry and route chunks may keep fixed names across builds (a library build names its entries
        // without a hash), yet every module file is cached as immutable, so each URL carries its content version.
        string? url(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var version = files?.Find(path)?.Version;

            return version is null ? moduleBase + path : $"{moduleBase}{path}?v={version}";
        }

        return new BootstrapModule
        {
            Id = module.Id,
            DisplayName = module.DisplayName,
            CanOperate = canOperate,
            Entry = url(manifest.Entry),
            Css = [.. manifest.Css.Select(url).OfType<string>()],
            Nav = manifest.Nav,
            Routes =
            [
                .. manifest.Routes.Select(route => new BootstrapRoute { Path = route.Path, Chunk = url(route.Chunk) }),
            ],
            ObjectKinds = manifest.ObjectKinds,
            Commands = manifest.Commands,
            Events = manifest.Events,
            Refusal = null,
        };
    }
}

/// <summary>The session as the browser sees it.</summary>
internal sealed record BootstrapSession
{
    public required bool Authenticated { get; init; }

    public string? Name { get; init; }
}

/// <summary>
/// One module the session may view: its static manifest with absolute URLs, or a refusal entry naming both SDK
/// versions.
/// </summary>
internal sealed record BootstrapModule
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required bool CanOperate { get; init; }

    /// <summary>The entry chunk's absolute URL; <see langword="null"/> for a refused module.</summary>
    public string? Entry { get; init; }

    public required IReadOnlyList<string> Css { get; init; }

    public required IReadOnlyList<ModuleManifestNavEntry> Nav { get; init; }

    public required IReadOnlyList<BootstrapRoute> Routes { get; init; }

    public required IReadOnlyDictionary<string, JsonElement> ObjectKinds { get; init; }

    /// <summary>The module's client commands. Server commands are read per object, not inlined.</summary>
    public required IReadOnlyList<ModuleManifestCommand> Commands { get; init; }

    public required IReadOnlyList<string> Events { get; init; }

    /// <summary>Why the shell refused the module; <see langword="null"/> when it loads.</summary>
    public ModuleRefusal? Refusal { get; init; }
}

/// <summary>A module route: its client path and its chunk's absolute URL.</summary>
internal sealed record BootstrapRoute
{
    public required string Path { get; init; }

    public string? Chunk { get; init; }
}

/// <summary>
/// The shell's own JSON options: the document's inline JSON is written with these, never the host's, so
/// <see cref="JavaScriptEncoder.Default"/> escapes <c>&lt;</c>, <c>&gt;</c>, and <c>&amp;</c> and a value such as
/// <c>&lt;/script&gt;</c> cannot end the block it sits in.
/// </summary>
internal static class ShellJson
{
    private static readonly JsonSerializerOptions _Options = _CreateOptions();

    public static JsonTypeInfo<BootstrapState> BootstrapStateInfo { get; } = _TypeInfo<BootstrapState>();

    public static JsonTypeInfo<ImportMapDocument> ImportMapInfo { get; } = _TypeInfo<ImportMapDocument>();

    public static JsonTypeInfo<ShellBuildManifest> ShellBuildInfo { get; } = _TypeInfo<ShellBuildManifest>();

    private static JsonSerializerOptions _CreateOptions()
    {
        // Set explicitly, not left to the default, so a later change to the context's options cannot relax it.
        var options = new JsonSerializerOptions(ShellJsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.Default,
        };
        options.MakeReadOnly();

        return options;
    }

    private static JsonTypeInfo<T> _TypeInfo<T>() => (JsonTypeInfo<T>)_Options.GetTypeInfo(typeof(T));
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true
)]
[JsonSerializable(typeof(BootstrapState))]
[JsonSerializable(typeof(ImportMapDocument))]
[JsonSerializable(typeof(ShellBuildManifest))]
internal sealed partial class ShellJsonContext : JsonSerializerContext;
