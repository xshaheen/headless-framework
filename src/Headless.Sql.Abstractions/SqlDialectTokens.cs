// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Runtime.InteropServices;

namespace Headless.Sql;

/// <summary>Placeholders a store writes in statement fragments, replaced by each dialect.</summary>
[PublicAPI]
public static class SqlDialectTokens
{
    /// <summary>
    /// The database clock, read once per statement. See <see cref="ISqlDialect" /> for which shapes read it after their
    /// row locks are held.
    /// </summary>
    public const string Now = "{now}";

    /// <summary>
    /// The row already stored under the key, in <see cref="SqlUpsert" />'s <c>Set</c> and <c>Guard</c>: write
    /// <c>{stored}.column</c> on the right of an assignment and in the guard. An assignment's target stays unqualified.
    /// </summary>
    public const string Stored = "{stored}";
}
