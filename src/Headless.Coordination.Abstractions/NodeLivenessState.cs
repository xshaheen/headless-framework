// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Store-classified liveness state for a current node incarnation.</summary>
[PublicAPI]
public enum NodeLivenessState
{
    Alive = 0,
    Suspected = 1,
    Dead = 2,
}
