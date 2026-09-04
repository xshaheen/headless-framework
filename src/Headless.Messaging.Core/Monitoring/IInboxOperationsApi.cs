// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Messaging.Monitoring;

/// <summary>Provider-neutral, payload-free inbox administration surface.</summary>
[PublicAPI]
public interface IInboxOperationsApi
{
    /// <summary>Queries retained inbox generations without loading message payloads or arbitrary headers.</summary>
    ValueTask<IndexPage<InboxGenerationView>> QueryAsync(
        InboxGenerationQuery query,
        InboxAuthorizationContext authorization,
        CancellationToken cancellationToken = default
    );

    /// <summary>Adds a retention hold to an expected terminal generation.</summary>
    ValueTask<InboxOperationResult> HoldAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Releases the retention hold from an expected terminal generation.</summary>
    ValueTask<InboxOperationResult> ReleaseHoldAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Atomically creates one independently deduplicated child generation.</summary>
    ValueTask<InboxOperationResult> ForceReprocessAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Deletes an expected terminal, unheld generation while preserving its audit and receipt.</summary>
    ValueTask<InboxOperationResult> PurgeAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    );
}
