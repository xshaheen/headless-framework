// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Headless.Checks;

namespace Headless.Permissions.Models;

/// <summary>
/// Maps each requested permission name to its <see cref="PermissionGrantResult"/>. Keys are compared ordinally.
/// Used by the batch permission evaluation paths. The result is read-only to consumers; grant providers populate
/// it internally.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1710:IdentifiersShouldHaveCorrectSuffix",
    Justification = "Public API contract name; a domain-specific read-only grant-status map, not a general-purpose dictionary."
)]
public sealed class MultiplePermissionGrantStatusResult : IReadOnlyDictionary<string, PermissionGrantResult>
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
    /// Whether every entry has <see cref="PermissionGrantStatus.Granted"/> status.
    /// An empty result is considered all-granted (vacuously true).
    /// </summary>
    public bool AllGranted => _statuses.Values.All(x => x.Status is PermissionGrantStatus.Granted);

    /// <summary>
    /// Whether every entry has <see cref="PermissionGrantStatus.Prohibited"/> status.
    /// An empty result is considered all-prohibited (vacuously true).
    /// </summary>
    public bool AllProhibited => _statuses.Values.All(x => x.Status is PermissionGrantStatus.Prohibited);

    /// <inheritdoc cref="IReadOnlyDictionary{TKey,TValue}.this"/>
    public PermissionGrantResult this[string key]
    {
        get => _statuses[key];
        internal set => _statuses[key] = value;
    }

    /// <inheritdoc/>
    public IEnumerable<string> Keys => _statuses.Keys;

    /// <inheritdoc/>
    public IEnumerable<PermissionGrantResult> Values => _statuses.Values;

    /// <inheritdoc/>
    public int Count => _statuses.Count;

    /// <inheritdoc/>
    public bool ContainsKey(string key) => _statuses.ContainsKey(key);

    /// <inheritdoc/>
    public bool TryGetValue(string key, out PermissionGrantResult value) => _statuses.TryGetValue(key, out value!);

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, PermissionGrantResult>> GetEnumerator() => _statuses.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _statuses.GetEnumerator();

    /// <summary>Adds a single name-to-result entry. Internal population path used by grant providers.</summary>
    internal void Add(string name, PermissionGrantResult result) => _statuses.Add(name, result);
}
