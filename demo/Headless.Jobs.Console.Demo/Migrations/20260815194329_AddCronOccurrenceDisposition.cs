// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable IDE0161 // EF migrations are generated with block-scoped namespaces.

namespace Headless.Jobs.Console.Demo.Migrations
{
    /// <summary>Adds the typed cron occurrence disposition that carries the occupied-instant accounting rule.</summary>
    public partial class AddCronOccurrenceDisposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows backfill to Accounted — the ordinary "this row stands for its instant" value — which is
            // exactly the behaviour they had before the column existed, for every producer except the seeding
            // migration. Only rows that migration retires from here on carry ReplacementOwed and re-fire.
            migrationBuilder.AddColumn<string>(
                name: "Disposition",
                schema: "jobs",
                table: "CronJobOccurrences",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Accounted"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately NOT the watermark migration's guard shape. That one predicates on schedule and recovery
            // fields, which say nothing about this column: dropping Disposition collapses every value to the
            // implicit Accounted, so a ReplacementOwed row — an occurrence whose fire is still owed — would come
            // back permanently suppressed. Refuse while ANY non-ordinary value exists. Superseded is included even
            // though it happens to suppress like Accounted does: it is provenance a downgrade would destroy, and a
            // destructive operation should be cleared deliberately rather than by a predicate that happens to be
            // behaviour-equivalent today.
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM jobs."CronJobOccurrences" WHERE "Disposition" <> 'Accounted'
                    ) THEN
                        RAISE EXCEPTION 'Cannot downgrade cron occurrence disposition migration while non-ordinary occurrence dispositions exist.';
                    END IF;
                END $migration$;
                """
            );

            migrationBuilder.DropColumn(name: "Disposition", schema: "jobs", table: "CronJobOccurrences");
        }
    }
}
