CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                                                       "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
    );

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20250118001438_InitialMigration') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'features') THEN
CREATE SCHEMA features;
END IF;
END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20250118001438_InitialMigration') THEN
CREATE TABLE features."FeatureDefinitions" (
                                               "Id" uuid NOT NULL,
                                               "GroupName" character varying(128) NOT NULL,
                                               "Name" character varying(128) NOT NULL,
                                               "DisplayName" character varying(256) NOT NULL,
                                               "ParentName" character varying(128),
                                               "Description" character varying(256),
                                               "DefaultValue" character varying(256),
                                               "IsVisibleToClients" boolean NOT NULL,
                                               "IsAvailableToHost" boolean NOT NULL,
                                               "Providers" character varying(256),
                                               "ExtraProperties" text NOT NULL,
                                               CONSTRAINT "PK_FeatureDefinitions" PRIMARY KEY ("Id")
);
END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20250118001438_InitialMigration') THEN
CREATE TABLE features."FeatureGroupDefinitions" (
                                                    "Id" uuid NOT NULL,
                                                    "Name" character varying(128) NOT NULL,
                                                    "DisplayName" character varying(256) NOT NULL,
                                                    "ExtraProperties" text NOT NULL,
                                                    CONSTRAINT "PK_FeatureGroupDefinitions" PRIMARY KEY ("Id")
);
END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20250118001438_InitialMigration') THEN
CREATE TABLE features."FeatureValues" (
                                          "Id" uuid NOT NULL,
                                          "Name" character varying(128) NOT NULL,
                                          "Value" character varying(128) NOT NULL,
                                          "ProviderName" character varying(64) NOT NULL,
                                          "ProviderKey" character varying(64),
                                          CONSTRAINT "PK_FeatureValues" PRIMARY KEY ("Id")
);
END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20250118001438_InitialMigration') THEN
CREATE INDEX "IX_FeatureDefinitions_GroupName" ON features."FeatureDefinitions" ("GroupName");
END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20250118001438_InitialMigration') THEN
CREATE UNIQUE INDEX "IX_FeatureDefinitions_Name" ON features."FeatureDefinitions" ("Name");
END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20250118001438_InitialMigration') THEN
CREATE UNIQUE INDEX "IX_FeatureGroupDefinitions_Name" ON features."FeatureGroupDefinitions" ("Name");
END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20250118001438_InitialMigration') THEN
CREATE UNIQUE INDEX "IX_FeatureValues_Name_ProviderName_ProviderKey" ON features."FeatureValues" ("Name", "ProviderName", "ProviderKey");
END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20250118001438_InitialMigration') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20250118001438_InitialMigration', '9.0.1');
END IF;
END $EF$;
COMMIT;