// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Tests.Migrations;

internal sealed class SqlServerCancellationMigrationDbContext(
    DbContextOptions<SqlServerCancellationMigrationDbContext> options
) : DbContext(options);
