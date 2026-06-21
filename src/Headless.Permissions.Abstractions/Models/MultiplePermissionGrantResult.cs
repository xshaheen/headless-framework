// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Permissions.Models;

/// <summary>
/// Maps each requested permission name to whether it is granted. Keys are compared ordinally. Used by the batch
/// <c>IsGrantedAsync</c> checks.
/// </summary>
public sealed class MultiplePermissionGrantResult() : Dictionary<string, bool>(StringComparer.Ordinal)
{
    /// <summary>Whether every entry is granted. An empty result is considered all-granted (vacuously true).</summary>
    public bool AllGranted => Values.All(isGranted => isGranted);

    /// <summary>Whether every entry is not granted. An empty result is considered all-prohibited (vacuously true).</summary>
    public bool AllProhibited => Values.All(isGranted => !isGranted);

    /// <summary>Creates a result pre-seeded with the given names, each set to <paramref name="isGranted"/>.</summary>
    public MultiplePermissionGrantResult(IReadOnlyList<string> names, bool isGranted = false)
        : this()
    {
        Argument.IsNotNull(names);

        foreach (var name in names)
        {
            Add(name, isGranted);
        }
    }
}
