// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork.Internal;

/// <summary>Releases an owned provider transaction after a successful commit.</summary>
internal interface IUnitOfWorkCommitCleanup
{
    ValueTask ReleaseAfterCommitAsync();
}
