// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>A cron definition's persisted schedule position: the reconciled-through watermark and the next due instant.</summary>
public readonly record struct CronSchedulePosition(DateTime ReconciledThroughUtc, DateTime NextDueUtc);
