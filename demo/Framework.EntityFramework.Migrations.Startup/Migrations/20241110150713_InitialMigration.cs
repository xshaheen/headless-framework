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
            schema: "permissions",
            constraints: table =>
            {
                table.PrimaryKey("PK_PermissionDefinitions", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "PermissionGrants",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ProviderName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ProviderKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                TenantId = table.Column<string>(type: "character varying(41)", maxLength: 41, nullable: true),
            },
            schema: "permissions",
            constraints: table =>
            {
                table.PrimaryKey("PK_PermissionGrants", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "PermissionGroupDefinitions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ExtraProperties = table.Column<string>(type: "text", nullable: false),
            },
            schema: "permissions",
            constraints: table =>
            {
                table.PrimaryKey("PK_PermissionGroupDefinitions", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_PermissionDefinitions_GroupName",
            table: "PermissionDefinitions",
            column: "GroupName"
,
            schema: "permissions");

        migrationBuilder.CreateIndex(
            name: "IX_PermissionDefinitions_Name",
            table: "PermissionDefinitions",
            column: "Name",
            schema: "permissions",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_PermissionGrants_TenantId_Name_ProviderName_ProviderKey",
            table: "PermissionGrants",
            columns: ["TenantId", "Name", "ProviderName", "ProviderKey"],
            schema: "permissions",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_PermissionGroupDefinitions_Name",
            table: "PermissionGroupDefinitions",
            column: "Name",
            schema: "permissions",
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
