CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'permissions') THEN
        CREATE SCHEMA permissions;
    END IF;
END $EF$;

CREATE TABLE permissions."PermissionDefinitions" (
    "Id" uuid NOT NULL,
    "GroupName" character varying(128) NOT NULL,
    "Name" character varying(128) NOT NULL,
    "DisplayName" character varying(256) NOT NULL,
    "IsEnabled" boolean NOT NULL,
    "ParentName" character varying(128),
    "Providers" character varying(128),
    "ExtraProperties" text NOT NULL,
    CONSTRAINT "PK_PermissionDefinitions" PRIMARY KEY ("Id")
);

CREATE TABLE permissions."PermissionGrants" (
    "Id" uuid NOT NULL,
    "Name" character varying(128) NOT NULL,
    "ProviderName" character varying(64) NOT NULL,
    "ProviderKey" character varying(64) NOT NULL,
    "TenantId" character varying(41),
    CONSTRAINT "PK_PermissionGrants" PRIMARY KEY ("Id")
);

CREATE TABLE permissions."PermissionGroupDefinitions" (
    "Id" uuid NOT NULL,
    "Name" character varying(128) NOT NULL,
    "DisplayName" character varying(256) NOT NULL,
    "ExtraProperties" text NOT NULL,
    CONSTRAINT "PK_PermissionGroupDefinitions" PRIMARY KEY ("Id")
);

CREATE INDEX "IX_PermissionDefinitions_GroupName" ON permissions."PermissionDefinitions" ("GroupName");

CREATE UNIQUE INDEX "IX_PermissionDefinitions_Name" ON permissions."PermissionDefinitions" ("Name");

CREATE UNIQUE INDEX "IX_PermissionGrants_TenantId_Name_ProviderName_ProviderKey" ON permissions."PermissionGrants" ("TenantId", "Name", "ProviderName", "ProviderKey");

CREATE UNIQUE INDEX "IX_PermissionGroupDefinitions_Name" ON permissions."PermissionGroupDefinitions" ("Name");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20241110150713_InitialMigration', '8.0.10');

COMMIT;

