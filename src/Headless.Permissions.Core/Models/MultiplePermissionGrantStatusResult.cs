// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Permissions.Models;

/// <summary>
/// Maps each requested permission name to its <see cref="PermissionGrantResult"/>. Keys are compared ordinally.
/// Used by the batch permission evaluation paths.
/// </summary>
public sealed class MultiplePermissionGrantStatusResult()
    : Dictionary<string, PermissionGrantResult>(StringComparer.Ordinal)
{
    /// <summary>
    /// Whether every entry has <see cref="PermissionGrantStatus.Granted"/> status.
    /// An empty result is considered all-granted (vacuously true).
    /// </summary>
    public bool AllGranted => Values.All(x => x.Status is PermissionGrantStatus.Granted);

    /// <summary>
    /// Whether every entry has <see cref="PermissionGrantStatus.Prohibited"/> status.
    /// An empty result is considered all-prohibited (vacuously true).
    /// </summary>
    public bool AllProhibited => Values.All(x => x.Status is PermissionGrantStatus.Prohibited);

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
        : this()
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
            Add(name, info);
        }
    }
}
