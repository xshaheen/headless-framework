// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.Fencing.Sqlite;

/// <summary>Connection and command options for the SQLite fencing provider.</summary>
/// <remarks>
/// <see cref="RelationalFencingOptions.ConnectionString" /> is the SQLite connection string of the database that holds the leases. The schema that holds the lease table is
/// shared by every fencing provider and configured through <see cref="FencingStorageOptions" />.
/// </remarks>
[PublicAPI]
public sealed class SqliteFencingOptions : RelationalFencingOptions;

internal sealed class SqliteFencingOptionsValidator()
    : RelationalFencingOptionsValidator<SqliteFencingOptions>("SQLite");
