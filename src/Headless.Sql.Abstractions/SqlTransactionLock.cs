// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Sql;

/// <summary>
/// Takes an exclusive lock on an application-defined resource name until the transaction ends, waiting while another
/// transaction holds it, so callers that share a name serialize even when no row exists to lock yet.
/// </summary>
/// <remarks>
/// The wait is bounded by the command timeout. The name is hashed on PostgreSQL (an advisory lock), and used as is on
/// SQL Server (an application lock), where it may hold at most 255 characters.
/// </remarks>
/// <param name="ResourceParameter">The text parameter that holds the resource name.</param>
[PublicAPI]
public sealed record SqlTransactionLock(string ResourceParameter);
