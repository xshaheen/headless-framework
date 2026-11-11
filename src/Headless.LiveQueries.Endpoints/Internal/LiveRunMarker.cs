// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries.Internal;

/// <summary>
/// A request feature present only on the synthetic request of a run, which tells the live filter to let the
/// handler execute instead of opening a stream.
/// </summary>
internal sealed class LiveRunMarker
{
    public static readonly LiveRunMarker Instance = new();

    private LiveRunMarker() { }
}
