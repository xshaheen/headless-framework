// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork.Internal;

/// <summary>A resource that checks whether it can commit before the commit call starts.</summary>
internal interface IUnitOfWorkCommitPreparation
{
    ValueTask PrepareCommitAsync(CancellationToken cancellationToken);
}
