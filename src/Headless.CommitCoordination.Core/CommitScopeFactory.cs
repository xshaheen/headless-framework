// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.CommitCoordination;

/// <summary>
/// Opens ambient commit coordination scopes. Core-internal infrastructure: signal sources reach it via
/// <c>InternalsVisibleTo</c>; consumers interact only through <see cref="ICurrentCommitCoordinator" /> and the
/// provider enlistment extensions.
/// </summary>
internal sealed class CommitScopeFactory(CommitScopeStack stack, ILogger<CommitCoordinator>? logger = null)
    : ICommitScopeFactory
{
    private readonly ILogger _logger = logger ?? NullLogger<CommitCoordinator>.Instance;

    /// <summary>
    /// Opens a scope, joining the current root coordinator when one exists.
    /// </summary>
    /// <param name="services">The service provider captured for callback drain.</param>
    /// <param name="capabilities">Capabilities attached when a new root is opened.</param>
    /// <returns>The opened scope.</returns>
    public ICommitScope Begin(IServiceProvider services, IEnumerable<ICommitCapability>? capabilities = null)
    {
        Argument.IsNotNull(services);

        var coordinator = stack.CurrentCore is { } current
            ? current.CreateChild()
            : new CommitCoordinator(capabilities, _logger);

        return _CreateScope(coordinator, services);
    }

    /// <summary>
    /// Opens an independent root coordinator even when an ambient coordinator exists.
    /// </summary>
    /// <param name="services">The service provider captured for callback drain.</param>
    /// <param name="capabilities">Capabilities attached to the new root.</param>
    /// <returns>The opened scope.</returns>
    public ICommitScope BeginNew(IServiceProvider services, IEnumerable<ICommitCapability>? capabilities = null)
    {
        Argument.IsNotNull(services);

        var coordinator = new CommitCoordinator(capabilities, _logger);

        return _CreateScope(coordinator, services);
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Ownership of the ambient pop handle is transferred to CommitScope."
    )]
    private ICommitScope _CreateScope(CommitCoordinator coordinator, IServiceProvider services)
    {
        IDisposable? ambientHandle = null;

        try
        {
            ambientHandle = stack.Push(coordinator);

            return new CommitScope(coordinator, services, ambientHandle);
        }
        catch
        {
            ambientHandle?.Dispose();
            coordinator.DisposePromotedRegistrations();

            throw;
        }
    }
}
