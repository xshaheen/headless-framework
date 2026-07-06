// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Headless.Checks;

namespace Headless.Permissions.Models;

/// <summary>
/// Maps each requested permission name to whether it is granted. Keys are compared ordinally. Used by the batch
/// <c>IsGrantedAsync</c> checks. The result is read-only to consumers; the framework populates it internally.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1710:IdentifiersShouldHaveCorrectSuffix",
    Justification = "Public API contract name; a domain-specific read-only grant map, not a general-purpose dictionary."
)]
public sealed class MultiplePermissionGrantResult : IReadOnlyDictionary<string, bool>
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

    /// <summary>Whether every entry is granted. An empty result is considered all-granted (vacuously true).</summary>
    public bool AllGranted => _grants.Values.All(isGranted => isGranted);

    /// <summary>Whether every entry is not granted. An empty result is considered all-prohibited (vacuously true).</summary>
    public bool AllProhibited => _grants.Values.All(isGranted => !isGranted);

    /// <inheritdoc/>
    public bool this[string key] => _grants[key];

    /// <inheritdoc/>
    public IEnumerable<string> Keys => _grants.Keys;

    /// <inheritdoc/>
    public IEnumerable<bool> Values => _grants.Values;

    /// <inheritdoc/>
    public int Count => _grants.Count;

    /// <inheritdoc/>
    public bool ContainsKey(string key) => _grants.ContainsKey(key);

    /// <inheritdoc/>
    public bool TryGetValue(string key, out bool value) => _grants.TryGetValue(key, out value);

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, bool>> GetEnumerator() => _grants.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _grants.GetEnumerator();

    /// <summary>Adds a single name-to-decision entry. Internal population path used by the framework.</summary>
    internal void Add(string name, bool isGranted) => _grants.Add(name, isGranted);
}
