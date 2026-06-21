// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Marker interface for provider-specific capabilities that can be attached to a commit coordinator scope and
/// queried by callbacks and work buffers via <see cref="ICommitCoordinator.TryGetCapability{TCapability}" /> or
/// <see cref="CommitContext.TryGetCapability{TCapability}" />.
/// </summary>
/// <remarks>
/// Capabilities carry provider handles that registered callbacks need — for example,
/// <see cref="IRelationalCommitContext" /> exposes the live <c>DbConnection</c> and <c>DbTransaction</c> so
/// durable work buffers can write rows inside the physical transaction before commit.
/// </remarks>
[PublicAPI]
public interface ICommitCapability;
