// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.Idempotency.Sqlite;

/// <summary>Connection and command options for the SQLite idempotency provider.</summary>
/// <remarks>
/// <see cref="RelationalIdempotencyOptions.ConnectionString" /> is the SQLite connection string of the database that
/// holds the records. The schema that holds the record table is shared by every idempotency provider and configured
/// through <see cref="IdempotencyStorageOptions" />.
/// </remarks>
[PublicAPI]
public sealed class SqliteIdempotencyOptions : RelationalIdempotencyOptions;

internal sealed class SqliteIdempotencyOptionsValidator()
    : RelationalIdempotencyOptionsValidator<SqliteIdempotencyOptions>("SQLite");
