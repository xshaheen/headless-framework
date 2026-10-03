// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Entities.BaseEntity;
using Headless.Jobs.Models;

namespace Headless.Jobs;

/// <summary>Continuation for schedule middleware.</summary>
public delegate Task JobScheduleNext(CancellationToken cancellationToken = default);
