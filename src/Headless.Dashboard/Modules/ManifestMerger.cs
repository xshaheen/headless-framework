// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// Builds the dashboard's manifest from every registered module: reads each module's embedded <c>manifest.json</c>,
/// refuses a module built against an SDK the shell cannot load, and applies the host's contribution overrides. The
/// user's permissions are applied later, when a page is rendered.
/// </summary>
internal static class ManifestMerger
{
    /// <summary>
    /// The version of the dashboard SDK this shell serves. A module built against another major, or a newer version,
    /// may import an export this shell lacks, so it is refused.
    /// </summary>
    public static readonly Version ShellSdkVersion = new(1, 0, 0);

    /// <summary>Merges the modules' embedded manifests, refusing modules built against an incompatible SDK.</summary>
    /// <exception cref="InvalidOperationException">
    /// A module has no readable manifest, or its manifest names another id than the module.
    /// </exception>
    public static MergedManifest Merge(
        IReadOnlyList<IDashboardModule> modules,
        Version shellSdkVersion,
        ContributionOverrides overrides
    )
    {
        var disabled = overrides.DisabledIds.ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<IReadOnlyList<string>> orders = overrides
            .OrderGroups.Select(static group => group.ToArray())
            .ToArray();
        var shell = _Normalize(shellSdkVersion);
        var knownIds = new HashSet<string>(StringComparer.Ordinal);
        var mergedModules = new List<MergedModule>(modules.Count);
        var relatedGroupIds = new List<string>();

        foreach (var module in modules)
        {
            var manifest = _ReadManifest(module);
            IReadOnlyList<DashboardCommand> serverCommands = module is IDashboardCommandSource commandSource
                ? commandSource.Commands
                : [];
            IReadOnlyList<string> moduleRelatedIds = module is IDashboardRelatedSource relatedSource
                ? relatedSource.GroupIds
                : [];

            // A refused module's ids still count as known, so disabling one of them never fails the start just
            // because the module's version was refused.
            knownIds.UnionWith(
                manifest.Sidebar.SelectMany(static section => section.Entries).Select(static entry => entry.Id)
            );
            knownIds.UnionWith(manifest.Commands.Select(static command => command.Id));
            knownIds.UnionWith(serverCommands.Select(static command => command.Id));
            knownIds.UnionWith(moduleRelatedIds);

            var refusal = _CheckSdkVersion(module, manifest.SdkVersion, shell);

            if (refusal is not null)
            {
                mergedModules.Add(
                    new MergedModule
                    {
                        Id = module.Id,
                        DisplayName = module.DisplayName,
                        Module = module,
                        // A refused module keeps its product's tab when it names a known one, so the tab can say why
                        // the module is missing; an older manifest without one joins no product.
                        Product = DashboardProducts.Find(manifest.Product),
                        Manifest = null,
                        ServerCommands = [],
                        Refusal = refusal,
                    }
                );

                continue;
            }

            var product = _ReadProduct(module, manifest.Product);
            var filtered = manifest with
            {
                Sidebar =
                [
                    .. manifest
                        .Sidebar.Select(section =>
                            section with
                            {
                                Entries = _Apply(section.Entries, static entry => entry.Id, disabled, orders),
                            }
                        )
                        // A section whose every entry the host disabled would draw a heading over nothing.
                        .Where(static section => section.Entries.Count > 0),
                ],
                Commands = _Apply(manifest.Commands, static command => command.Id, disabled, orders),
            };

            mergedModules.Add(
                new MergedModule
                {
                    Id = module.Id,
                    DisplayName = module.DisplayName,
                    Module = module,
                    Product = product,
                    Manifest = filtered,
                    ServerCommands = _Apply(serverCommands, static command => command.Id, disabled, orders),
                    Refusal = null,
                }
            );

            relatedGroupIds.AddRange(moduleRelatedIds);
        }

        return new MergedManifest
        {
            ShellSdkVersion = shell,
            Modules = mergedModules,
            RelatedGroupIds = _Apply(relatedGroupIds, static id => id, disabled, orders),
            KnownContributionIds = knownIds,
            DisabledIds = disabled,
        };
    }

    private static ModuleManifest _ReadManifest(IDashboardModule module)
    {
        var file = module.Assets.Files.GetFileInfo(ModuleManifest.FileName);

        if (!file.Exists || file.IsDirectory)
        {
            throw new InvalidOperationException(
                $"Dashboard module '{module.Id}' has no {ModuleManifest.FileName} at the root of its assets. Build "
                    + "the module's UI with the dashboard's Vite plugin and embed its output."
            );
        }

        ModuleManifest manifest;

        try
        {
            using var stream = file.CreateReadStream();
            manifest =
                JsonSerializer.Deserialize(stream, ModuleManifestJsonContext.Default.ModuleManifest)
                ?? throw new InvalidOperationException(
                    $"Dashboard module '{module.Id}' has an empty {ModuleManifest.FileName}."
                );
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException(
                $"Dashboard module '{module.Id}' has an invalid {ModuleManifest.FileName}: {e.Message}",
                e
            );
        }

        if (!string.Equals(manifest.Id, module.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Dashboard module '{module.Id}' ships a {ModuleManifest.FileName} for module '{manifest.Id}'. The id "
                    + "in defineModule must match the module's registered id."
            );
        }

        return manifest;
    }

    private static DashboardProduct _ReadProduct(IDashboardModule module, string? productId)
    {
        return DashboardProducts.Find(productId)
            ?? throw new InvalidOperationException(
                $"Dashboard module '{module.Id}' names the product '{productId}' in its {ModuleManifest.FileName}, "
                    + "which is not one of "
                    + string.Join(", ", DashboardProducts.All.Select(static product => product.Id))
                    + ". Set product in defineModule to one of them."
            );
    }

    private static ModuleRefusal? _CheckSdkVersion(IDashboardModule module, string? builtAgainst, Version shell)
    {
        var shellText = shell.ToString(3);

        if (!_TryParseSdkVersion(builtAgainst, out var built))
        {
            return new ModuleRefusal
            {
                BuiltAgainst = builtAgainst,
                ShellSdkVersion = shellText,
                Message =
                    $"Module '{module.DisplayName}' does not say which dashboard SDK it was built against, so the "
                    + $"shell (SDK {shellText}) cannot load it.",
            };
        }

        var reason =
            built.Major != shell.Major ? "a different major version"
            : built > shell ? "a newer version"
            : null;

        if (reason is null)
        {
            return null;
        }

        return new ModuleRefusal
        {
            BuiltAgainst = builtAgainst,
            ShellSdkVersion = shellText,
            Message =
                $"Module '{module.DisplayName}' was built against dashboard SDK {builtAgainst}, {reason} than this "
                + $"shell's SDK {shellText}, so it is not loaded.",
        };
    }

    private static bool _TryParseSdkVersion(string? text, [NotNullWhen(true)] out Version? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // A prerelease or build suffix (1.2.0-beta.1, 1.2.0+sha) does not change which exports the build may use.
        var end = text.IndexOfAny(['-', '+']);
        var core = end < 0 ? text : text[..end];

        if (!Version.TryParse(core, out var parsed))
        {
            return false;
        }

        version = _Normalize(parsed);

        return true;
    }

    // Version treats a missing build part as -1, which would sort 1.4 below 1.4.0, so every version is compared as
    // major.minor.build.
    private static Version _Normalize(Version version)
    {
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    /// <summary>
    /// Drops the disabled items, then applies each ordering group: the items a group names swap into the places those
    /// items already hold, in the group's order, and every other item keeps its place.
    /// </summary>
    private static List<T> _Apply<T>(
        IReadOnlyList<T> items,
        Func<T, string> idOf,
        HashSet<string> disabled,
        IReadOnlyList<IReadOnlyList<string>> orders
    )
    {
        var list = items.Where(item => !disabled.Contains(idOf(item))).ToList();

        foreach (var group in orders)
        {
            var rank = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var i = 0; i < group.Count; i++)
            {
                rank.TryAdd(group[i], i);
            }

            var slots = new List<int>();

            for (var i = 0; i < list.Count; i++)
            {
                if (rank.ContainsKey(idOf(list[i])))
                {
                    slots.Add(i);
                }
            }

            var ordered = slots.Select(slot => list[slot]).OrderBy(item => rank[idOf(item)]).ToArray();

            for (var i = 0; i < slots.Count; i++)
            {
                list[slots[i]] = ordered[i];
            }
        }

        return list;
    }
}

/// <summary>The dashboard's manifest after the merge, before the user's permissions are applied.</summary>
internal sealed class MergedManifest
{
    public required Version ShellSdkVersion { get; init; }

    /// <summary>Every registered module in registration order, a refused one as a refusal entry.</summary>
    public required IReadOnlyList<MergedModule> Modules { get; init; }

    /// <summary>The related-object group ids of the loaded modules, disabled ones dropped, in the host's order.</summary>
    public required IReadOnlyList<string> RelatedGroupIds { get; init; }

    /// <summary>Every contribution id the registered modules declare, refused modules included.</summary>
    public required IReadOnlySet<string> KnownContributionIds { get; init; }

    public required IReadOnlySet<string> DisabledIds { get; init; }

    /// <summary>Whether the host disabled the contribution, such as a server command that must answer 404.</summary>
    public bool IsDisabled(string contributionId) => DisabledIds.Contains(contributionId);
}

/// <summary>One module in the merged manifest.</summary>
internal sealed class MergedModule
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required IDashboardModule Module { get; init; }

    /// <summary>
    /// The product whose tab the module joins; <see langword="null"/> only for a refused module whose manifest names
    /// no known product.
    /// </summary>
    public required DashboardProduct? Product { get; init; }

    /// <summary>The manifest with the host's overrides applied; <see langword="null"/> for a refused module.</summary>
    public required ModuleManifest? Manifest { get; init; }

    /// <summary>The module's server commands with the host's overrides applied; empty for a refused module.</summary>
    public required IReadOnlyList<DashboardCommand> ServerCommands { get; init; }

    /// <summary>Why the shell refused the module, or <see langword="null"/> when it loads.</summary>
    public required ModuleRefusal? Refusal { get; init; }

    public bool IsRefused => Refusal is not null;
}

/// <summary>Why a module is not loaded: both SDK versions, and the message the shell shows in its rail group.</summary>
internal sealed record ModuleRefusal
{
    public required string? BuiltAgainst { get; init; }

    public required string ShellSdkVersion { get; init; }

    public required string Message { get; init; }
}
