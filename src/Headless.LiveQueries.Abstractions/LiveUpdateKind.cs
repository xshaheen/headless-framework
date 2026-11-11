// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries;

/// <summary>What one run of a live query produced, as a transport sees it.</summary>
[PublicAPI]
public enum LiveUpdateKind
{
    /// <summary>The first result of the subscription.</summary>
    Initial,

    /// <summary>A later run produced a result that differs from the last one sent.</summary>
    Changed,

    /// <summary>The run did not succeed. The subscription ends after this update.</summary>
    Failed,
}
