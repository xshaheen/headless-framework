using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Framework.EntityFramework.Migrations.Startup.Migrations;

/// <inheritdoc />
internal sealed partial class InitialMigration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "permissions");

        migrationBuilder.CreateTable(
            name: "PermissionDefinitions",
            schema: "permissions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                GroupName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                ParentName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Providers = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                ExtraProperties = table.Column<string>(type: "text", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PermissionDefinitions", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "PermissionGrants",
            schema: "permissions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ProviderName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ProviderKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                TenantId = table.Column<string>(type: "character varying(41)", maxLength: 41, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PermissionGrants", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "PermissionGroupDefinitions",
            schema: "permissions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ExtraProperties = table.Column<string>(type: "text", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PermissionGroupDefinitions", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_PermissionDefinitions_GroupName",
            schema: "permissions",
            table: "PermissionDefinitions",
            column: "GroupName"
        );

        migrationBuilder.CreateIndex(
            name: "IX_PermissionDefinitions_Name",
            schema: "permissions",
            table: "PermissionDefinitions",
            column: "Name",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_PermissionGrants_TenantId_Name_ProviderName_ProviderKey",
            schema: "permissions",
            table: "PermissionGrants",
            columns: new[] { "TenantId", "Name", "ProviderName", "ProviderKey" },
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_PermissionGroupDefinitions_Name",
            schema: "permissions",
            table: "PermissionGroupDefinitions",
            column: "Name",
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PermissionDefinitions", schema: "permissions");

        migrationBuilder.DropTable(name: "PermissionGrants", schema: "permissions");

        migrationBuilder.DropTable(name: "PermissionGroupDefinitions", schema: "permissions");
    }
}
