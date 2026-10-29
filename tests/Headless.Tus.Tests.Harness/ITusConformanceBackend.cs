// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using tusdotnet.Interfaces;

namespace Tests;

/// <summary>
/// Provider-neutral contract a tus store backend implements to run the conformance suite. Each leaf fixture already
/// derives from its provider's container fixture, so the shared part is an interface rather than an abstract base.
/// </summary>
/// <typeparam name="TStore">
/// The store under test. It implements every tus extension the conformance scenarios exercise, so the scenarios call
/// the store through the tusdotnet interfaces only.
/// </typeparam>
public interface ITusConformanceBackend<TStore>
    where TStore : class,
        ITusStore,
        ITusPipelineStore,
        ITusCreationStore,
        ITusCreationDeferLengthStore,
        ITusTerminationStore,
        ITusExpirationStore,
        ITusReadableStore,
        ITusConcatenationStore
{
    /// <summary>
    /// Creates a store over storage that is provisioned and ready to accept uploads. Stores created by one backend
    /// share their storage, so a scenario must not assume it is the only writer.
    /// </summary>
    /// <param name="loggerFactory">The running test's logger factory, so store logs reach the test output.</param>
    /// <param name="cancellationToken">Cancels provisioning.</param>
    Task<TStore> CreateStoreAsync(ILoggerFactory loggerFactory, CancellationToken cancellationToken);
}
