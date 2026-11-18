// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Metadata;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// The values of one stored row that cannot change while the row exists: its key and the columns fixed at insert.
/// They are the same before and after an update, so one instance describes both sides of a change.
/// </summary>
/// <param name="entityType">The entity type the row was read or saved as.</param>
/// <param name="values">One value for each property of the type's <see cref="LiveRowLayout" />, in its order.</param>
internal sealed class LiveRow(IEntityType entityType, object?[] values)
{
    /// <summary>
    /// Stands for a value the write did not reveal, such as the owner column of a row deleted by key alone. A
    /// filter treats it as equal to anything.
    /// </summary>
    public static readonly object Unknown = new();

    public IEntityType EntityType => entityType;

    public object? this[int index] => values[index];
}
