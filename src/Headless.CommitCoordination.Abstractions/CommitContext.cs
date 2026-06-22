// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Snapshot of the commit coordination scope passed to every registered callback when the physical unit of work
/// reaches a terminal outcome.
/// </summary>
/// <remarks>
/// An instance is created by the drain infrastructure immediately before invoking callbacks; it is read-only
/// and is shared across all callbacks in a single drain pass. Callbacks should not cache or escape this instance
/// beyond their own invocation.
/// </remarks>
[PublicAPI]
public sealed class CommitContext
{
    private readonly IReadOnlyDictionary<Type, ICommitCapability> _capabilities;

    /// <summary>
    /// Initializes a new <see cref="CommitContext" /> with no capabilities, for use in tests.
    /// </summary>
    public CommitContext()
        : this(new Dictionary<Type, ICommitCapability>()) { }

    internal CommitContext(IReadOnlyDictionary<Type, ICommitCapability> capabilities)
    {
        _capabilities = capabilities;
    }

    /// <summary>
    /// Gets the service provider captured by the scope owner at the time the scope was opened.
    /// </summary>
    /// <remarks>
    /// This is the DI scope of the enlistment caller (e.g. the request scope). It may have been disposed by the
    /// time a background drain runs; implementations that defer work beyond the drain should resolve dependencies
    /// from a freshly-created scope rather than retaining this reference.
    /// </remarks>
    public required IServiceProvider Services { get; init; }

    /// <summary>
    /// Gets the terminal outcome that triggered the drain: <see cref="CommitOutcome.Committed" /> or
    /// <see cref="CommitOutcome.RolledBack" />.
    /// </summary>
    public required CommitOutcome Outcome { get; init; }

    /// <summary>
    /// Attempts to retrieve a provider capability attached when the enclosing scope was opened.
    /// </summary>
    /// <typeparam name="TCapability">The capability interface to query, deriving from <see cref="ICommitCapability" />.</typeparam>
    /// <param name="capability">
    /// When this method returns <see langword="true" />, contains the attached capability; otherwise
    /// <see langword="null" />.
    /// </param>
    /// <returns><see langword="true" /> when the requested capability is attached; otherwise <see langword="false" />.</returns>
    public bool TryGetCapability<TCapability>([NotNullWhen(true)] out TCapability? capability)
        where TCapability : class, ICommitCapability
    {
        if (_capabilities.TryGetValue(typeof(TCapability), out var value) && value is TCapability typed)
        {
            capability = typed;
            return true;
        }

        capability = null;
        return false;
    }
}
