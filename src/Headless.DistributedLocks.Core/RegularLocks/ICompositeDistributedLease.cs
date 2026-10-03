// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks;

internal interface ICompositeDistributedLease
{
    IReadOnlyList<IDistributedLease> Children { get; }
}
