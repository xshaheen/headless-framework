// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Context passed to commit coordination callbacks.
/// </summary>
[PublicAPI]
public sealed class CommitContext
{
    private readonly IReadOnlyDictionary<Type, ICommitCapability> _capabilities;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommitContext" /> class.
    /// </summary>
    public CommitContext()
        : this(new Dictionary<Type, ICommitCapability>()) { }

    internal CommitContext(IReadOnlyDictionary<Type, ICommitCapability> capabilities)
    {
        _capabilities = capabilities;
    }

    /// <summary>
    /// Gets the service provider captured by the scope owner.
    /// </summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>
    /// Gets the terminal outcome being drained.
    /// </summary>
    public required CommitOutcome Outcome { get; init; }

    /// <summary>
    /// Attempts to get a provider capability attached by the scope owner.
    /// </summary>
    /// <typeparam name="TCapability">The capability type.</typeparam>
    /// <param name="capability">The capability when available.</param>
    /// <returns><see langword="true" /> when the capability exists.</returns>
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
