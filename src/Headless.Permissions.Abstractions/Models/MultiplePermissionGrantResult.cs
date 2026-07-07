// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Permissions.Models;

/// <summary>
/// Maps each requested permission name to whether it is granted. Keys are compared ordinally. Used by the batch
/// <c>IsGrantedAsync</c> checks. The result is read-only to consumers; the framework populates it internally.
/// </summary>
public sealed class MultiplePermissionGrantResult
{
    private readonly Dictionary<string, bool> _grants = new(StringComparer.Ordinal);

    /// <summary>Creates an empty result. Population is internal to the framework.</summary>
    internal MultiplePermissionGrantResult() { }

    /// <summary>Creates a result pre-seeded with the given names, each set to <paramref name="isGranted"/>.</summary>
    /// <param name="names">Permission names to seed. Must not be <see langword="null"/>.</param>
    /// <param name="isGranted">The uniform decision to assign to every name.</param>
    public MultiplePermissionGrantResult(IReadOnlyList<string> names, bool isGranted = false)
    {
        Argument.IsNotNull(names);

        foreach (var name in names)
        {
            _grants.Add(name, isGranted);
        }
    }

    /// <summary>
    /// The requested permission names mapped to whether each is granted. Keys are compared ordinally. Enumerate
    /// this to inspect every decision, or use the <see cref="this[string]"/> indexer for a single lookup.
    /// </summary>
    public IReadOnlyDictionary<string, bool> Grants => _grants;

    /// <summary>Whether every entry is granted. An empty result is considered all-granted (vacuously true).</summary>
    public bool AllGranted => _grants.Values.All(isGranted => isGranted);

    /// <summary>Whether every entry is not granted. An empty result is considered all-prohibited (vacuously true).</summary>
    public bool AllProhibited => _grants.Values.All(isGranted => !isGranted);

    /// <summary>Gets whether the permission named <paramref name="permissionName"/> is granted.</summary>
    /// <param name="permissionName">The permission name to look up. Must be present in the result.</param>
    public bool this[string permissionName] => _grants[permissionName];

    /// <summary>Adds a single name-to-decision entry. Internal population path used by the framework.</summary>
    internal void Add(string name, bool isGranted) => _grants.Add(name, isGranted);
}
