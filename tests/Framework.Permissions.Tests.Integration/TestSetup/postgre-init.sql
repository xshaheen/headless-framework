CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                                                       "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
    );

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20241110150713_InitialMigration') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'permissions') THEN
CREATE SCHEMA permissions;
END IF;
END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20241110150713_InitialMigration') THEN
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
END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20241110150713_InitialMigration') THEN
CREATE TABLE permissions."PermissionGrants" (
                                                "Id" uuid NOT NULL,
                                                "Name" character varying(128) NOT NULL,
                                                "ProviderName" character varying(64) NOT NULL,
                                                "ProviderKey" character varying(64) NOT NULL,
                                                "TenantId" character varying(41),
                                                CONSTRAINT "PK_PermissionGrants" PRIMARY KEY ("Id")
);
END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20241110150713_InitialMigration') THEN
CREATE TABLE permissions."PermissionGroupDefinitions" (
                                                          "Id" uuid NOT NULL,
                                                          "Name" character varying(128) NOT NULL,
                                                          "DisplayName" character varying(256) NOT NULL,
                                                          "ExtraProperties" text NOT NULL,
                                                          CONSTRAINT "PK_PermissionGroupDefinitions" PRIMARY KEY ("Id")
);
END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20241110150713_InitialMigration') THEN
CREATE INDEX "IX_PermissionDefinitions_GroupName" ON permissions."PermissionDefinitions" ("GroupName");
END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20241110150713_InitialMigration') THEN
CREATE UNIQUE INDEX "IX_PermissionDefinitions_Name" ON permissions."PermissionDefinitions" ("Name");
END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20241110150713_InitialMigration') THEN
CREATE UNIQUE INDEX "IX_PermissionGrants_TenantId_Name_ProviderName_ProviderKey" ON permissions."PermissionGrants" ("TenantId", "Name", "ProviderName", "ProviderKey");
END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20241110150713_InitialMigration') THEN
CREATE UNIQUE INDEX "IX_PermissionGroupDefinitions_Name" ON permissions."PermissionGroupDefinitions" ("Name");
END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20241110150713_InitialMigration') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20241110150713_InitialMigration', '9.0.1');
END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115000000_AddIsGrantedColumn') THEN
        ALTER TABLE permissions."PermissionGrants" ADD "IsGranted" boolean NOT NULL DEFAULT true;
END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260115000000_AddIsGrantedColumn') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260115000000_AddIsGrantedColumn', '9.0.1');
END IF;
END $EF$;
COMMIT;

