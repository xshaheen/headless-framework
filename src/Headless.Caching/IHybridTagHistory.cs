// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

internal interface IHybridTagHistory
{
    void SetDurableMarkerReader(IDurableTagMarkerReader? reader);

    void TrackPublication(bool starting);
}
