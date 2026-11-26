// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

internal interface IDurableTagMarkerReader
{
    /// <summary>Gets the resolution shared by this store's tag markers and framed value birth timestamps.</summary>
    long TagTimestampResolutionTicks { get; }

    /// <summary>Reads authoritative UTC markers and raises the reader's local view before returning them.</summary>
    ValueTask<IReadOnlyDictionary<string, DateTime>> ReadDurableTagMarkersAsync(
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken
    );
}
