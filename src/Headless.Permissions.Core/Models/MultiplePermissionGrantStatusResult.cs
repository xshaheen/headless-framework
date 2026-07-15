// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Permissions.Models;

/// <summary>
/// Maps each requested permission name to its <see cref="PermissionGrantResult"/>. Keys are compared ordinally.
/// Used by the batch permission evaluation paths. The result is read-only to consumers; grant providers populate
/// it internally.
/// </summary>
public sealed class MultiplePermissionGrantStatusResult
{
    private readonly Dictionary<string, PermissionGrantResult> _statuses = new(StringComparer.Ordinal);

    /// <summary>Creates an empty result. Population is internal to the framework's grant providers.</summary>
    internal MultiplePermissionGrantStatusResult() { }

    /// <summary>
    /// Creates a result pre-seeded with every name in <paramref name="names"/> set to the same
    /// <paramref name="grantStatus"/>, each associated with the given <paramref name="providerKeys"/>.
    /// </summary>
    /// <param name="names">Permission names to seed. Must not be <see langword="null"/>.</param>
    /// <param name="providerKeys">Provider keys to associate with each entry.</param>
    /// <param name="grantStatus">The uniform status to assign to every name.</param>
    public MultiplePermissionGrantStatusResult(
        IReadOnlyList<string> names,
        IReadOnlyCollection<string> providerKeys,
        PermissionGrantStatus grantStatus
    )
    {
        Argument.IsNotNull(names);
        Argument.IsInEnum(grantStatus);

        var info = grantStatus switch
        {
            PermissionGrantStatus.Granted => PermissionGrantResult.Granted(providerKeys),
            PermissionGrantStatus.Prohibited => PermissionGrantResult.Prohibited(providerKeys),
            _ => PermissionGrantResult.Undefined(providerKeys),
        };

        foreach (var name in names)
        {
            _statuses.Add(name, info);
        }
    }

    /// <summary>
    /// The requested permission names mapped to their resolved grant results. Keys are compared ordinally.
    /// Enumerate this to inspect every result, or use the <see cref="this[string]"/> indexer for a single lookup.
    /// </summary>
    public IReadOnlyDictionary<string, PermissionGrantResult> Statuses => _statuses;

    /// <summary>
    /// Whether every entry has <see cref="PermissionGrantStatus.Granted"/> status.
    /// An empty result is considered all-granted (vacuously true).
    /// </summary>
    public bool AllGranted => _statuses.Values.All(x => x.Status is PermissionGrantStatus.Granted);

    /// <summary>
    /// Whether every entry has <see cref="PermissionGrantStatus.Prohibited"/> status.
    /// An empty result is considered all-prohibited (vacuously true).
    /// </summary>
    public bool AllProhibited => _statuses.Values.All(x => x.Status is PermissionGrantStatus.Prohibited);

    /// <summary>Gets the resolved grant result for the permission named <paramref name="permissionName"/>.</summary>
    /// <param name="permissionName">The permission name to look up. Must be present in the result.</param>
    public PermissionGrantResult this[string permissionName]
    {
        get => _statuses[permissionName];
        internal set => _statuses[permissionName] = value;
    }

    /// <summary>Adds a single name-to-result entry. Internal population path used by grant providers.</summary>
    internal void Add(string name, PermissionGrantResult result)
    {
        _statuses.Add(name, result);
    }
}
