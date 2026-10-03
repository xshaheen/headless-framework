// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Sql;

namespace Headless.AuditLog;

/// <summary>
/// Binds an instant as a parameter typed like the <c>CreatedAt</c> column, so a comparison never converts the column
/// and keeps its index usable.
/// </summary>
internal delegate void AuditLogCreatedAtBinder(DbCommand command, string parameter, DateTimeOffset value);
