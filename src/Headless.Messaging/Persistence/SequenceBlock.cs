// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Persistence;

/// <summary>
/// An earlier unfinished received row of the same consumer and key that a sequential consumer must wait for, as
/// <see cref="IDataStorage.GetSequenceBlockAsync"/> reports it.
/// </summary>
/// <param name="DueAt">
/// When the blocking row is next due for retry pickup, or <see langword="null"/> when it is queued or running and has
/// no due time to wait for.
/// </param>
public readonly record struct SequenceBlock(DateTimeOffset? DueAt);
