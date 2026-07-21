// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;

namespace Headless.Jobs.Models;

/// <summary>Definition edit plus its optimistic schedule fence and optional replacement occurrence.</summary>
/// <typeparam name="TCronJob">The application's concrete cron job entity type.</typeparam>
/// <param name="Definition">The requested definition state.</param>
/// <param name="ExpectedScheduleRevision">The revision observed by the caller.</param>
/// <param name="NextOccurrence">The replacement occurrence for an active schedule-changing edit.</param>
[PublicAPI]
public sealed record CronJobAtomicUpdate<TCronJob>(
    TCronJob Definition,
    long ExpectedScheduleRevision,
    CronJobOccurrenceEntity<TCronJob>? NextOccurrence
)
    where TCronJob : CronJobEntity, new();
