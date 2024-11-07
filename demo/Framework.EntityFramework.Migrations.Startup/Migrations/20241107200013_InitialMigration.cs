using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Framework.EntityFramework.Migrations.Startup.Migrations;

/// <inheritdoc />
public partial class InitialMigration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "settings");

        migrationBuilder.CreateTable(
            name: "SettingDefinitions",
            schema: "settings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                DefaultValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                IsVisibleToClients = table.Column<bool>(type: "boolean", nullable: false),
                IsInherited = table.Column<bool>(type: "boolean", nullable: false),
                IsEncrypted = table.Column<bool>(type: "boolean", nullable: false),
                Providers = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                ExtraProperties = table.Column<string>(type: "text", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SettingDefinitions", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "SettingValues",
            schema: "settings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                ProviderName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ProviderKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SettingValues", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_SettingDefinitions_Name",
            schema: "settings",
            table: "SettingDefinitions",
            column: "Name",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_SettingValues_Name_ProviderName_ProviderKey",
            schema: "settings",
            table: "SettingValues",
            columns: new[] { "Name", "ProviderName", "ProviderKey" },
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SettingDefinitions", schema: "settings");

        migrationBuilder.DropTable(name: "SettingValues", schema: "settings");
    }
}
