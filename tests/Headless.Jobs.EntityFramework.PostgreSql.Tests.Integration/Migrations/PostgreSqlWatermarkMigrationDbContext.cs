// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Tests.Migrations.PostgreSql;

internal sealed class PostgreSqlWatermarkMigrationDbContext(
    DbContextOptions<PostgreSqlWatermarkMigrationDbContext> options
) : DbContext(options);
