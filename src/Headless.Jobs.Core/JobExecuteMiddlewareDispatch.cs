// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Models;

namespace Headless.Jobs;

/// <summary>Generated callback for one execute middleware declaration.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public delegate Task JobExecuteMiddlewareDispatch(
    JobExecuteContext context,
    JobExecuteNext next,
    CancellationToken cancellationToken
);
