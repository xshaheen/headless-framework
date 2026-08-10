// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;

namespace Headless.Jobs.Models;

/// <summary>Definition edit plus its optimistic schedule fence and optional replacement occurrence.</summary>
/// <typeparam name="TCronJob">The application's concrete cron job entity type.</typeparam>
/// <param name="Definition">The requested definition state.</param>
/// <param name="ExpectedScheduleRevision">The revision observed by the caller.</param>
/// <param name="NextOccurrenceFactory">
/// Factory for the replacement occurrence of an active schedule-changing edit. The provider supplies its clock
/// inside the fenced transition; <see langword="null"/> for metadata/recovery-only or paused-definition edits.
/// </param>
[PublicAPI]
public sealed record CronJobAtomicUpdate<TCronJob>(
    TCronJob Definition,
    long ExpectedScheduleRevision,
    Func<DateTime, CronJobOccurrenceEntity<TCronJob>?>? NextOccurrenceFactory
)
    where TCronJob : CronJobEntity, new();
