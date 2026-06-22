// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Marker interface for scope-local work buffers owned by a commit coordinator and retrieved via
/// <see cref="ICommitCoordinator.GetOrAdd{TBuffer}" />.
/// </summary>
/// <remarks>
/// A work buffer accumulates items (e.g. outbox messages, cache invalidation keys) during the transaction and
/// drains or discards them after the terminal outcome. Implementations that implement
/// <see cref="IAsyncDisposable" /> or <see cref="IDisposable" /> have their disposal called by the drain
/// infrastructure after all callbacks have run, regardless of the outcome.
/// </remarks>
[PublicAPI]
public interface ICommitWorkBuffer;
