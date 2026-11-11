// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Metadata;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// The rows of one table that one execution of a query could return: those whose listed columns all hold the listed
/// values. The query may have filtered further; this is never narrower than the query.
/// </summary>
/// <param name="entityType">The entity type the query read the table as.</param>
/// <param name="conditions">A position in the type's <see cref="LiveRowLayout" /> and the value it must hold.</param>
internal sealed class LiveRowFilter(IEntityType entityType, (int Index, object Value)[] conditions)
{
    /// <summary>Returns whether <paramref name="row" /> could be one the query returns.</summary>
    public bool Matches(object row)
    {
        // A row saved through another model of the same table says nothing in this model's terms, so it may be any
        // row.
        if (row is not LiveRow known || !ReferenceEquals(known.EntityType, entityType))
        {
            return true;
        }

        foreach (var (index, value) in conditions)
        {
            var actual = known[index];

            if (!ReferenceEquals(actual, LiveRow.Unknown) && !value.Equals(actual))
            {
                return false;
            }
        }

        return true;
    }
}
