// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Tests.Migrations;

[DbContext(typeof(SqlServerCancellationMigrationDbContext))]
[Migration(Id)]
internal sealed class AddCronOccurrenceDisposition : Migration
{
    public const string Id = "20260815000100_AddCronOccurrenceDisposition";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing rows backfill to Accounted — the ordinary "this row stands for its instant" value — which is
        // exactly the behaviour they had before the column existed, for every producer except the seeding migration.
        migrationBuilder.AddColumn<string>(
            name: "Disposition",
            schema: "jobs",
            table: "CronJobOccurrences",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Accounted"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately NOT the watermark migration's guard shape (that one predicates on schedule and recovery
        // fields, which say nothing about this column). Dropping Disposition collapses every value to the implicit
        // Accounted, so a ReplacementOwed row — an occurrence whose fire is still owed — would come back permanently
        // suppressed. Refuse while ANY non-ordinary value exists.
        migrationBuilder.Sql(
            """
            IF EXISTS (
                SELECT 1 FROM [jobs].[CronJobOccurrences] WHERE [Disposition] <> N'Accounted'
            )
                THROW 51000, 'Cannot downgrade cron occurrence disposition migration while non-ordinary occurrence dispositions exist.', 1;
            """
        );

        migrationBuilder.DropColumn(name: "Disposition", schema: "jobs", table: "CronJobOccurrences");
    }
}
