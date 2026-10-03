// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Models;

namespace Headless.Jobs;

/// <summary>Continuation for execute middleware.</summary>
public delegate Task JobExecuteNext(CancellationToken cancellationToken = default);
