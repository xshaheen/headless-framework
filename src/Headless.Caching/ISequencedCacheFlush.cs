// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

internal interface ISequencedCacheFlush
{
    ValueTask WriteSequencedRemoveMarkerAsync(
        DateTimeOffset invalidatedAt,
        string origin,
        long sequence,
        CancellationToken cancellationToken
    );
}
