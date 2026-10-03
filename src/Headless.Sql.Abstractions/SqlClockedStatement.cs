// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Sql;

/// <summary>
/// One statement that reads <see cref="SqlDialectTokens.Now" /> without locking anything first: a plain read, a
/// delete, or an update whose clock may be taken when the statement starts.
/// </summary>
/// <remarks>
/// Use it for statements that do not wait on a row another writer holds before they decide. A statement that must
/// read the clock after a lock wait belongs in a <see cref="SqlFencedTransition" />.
/// </remarks>
/// <param name="Sql">
/// Exactly one statement, which does not itself start with <c>WITH</c>, over <see cref="SqlDialectTokens.Now" />.
/// </param>
[PublicAPI]
public sealed record SqlClockedStatement(string Sql);
