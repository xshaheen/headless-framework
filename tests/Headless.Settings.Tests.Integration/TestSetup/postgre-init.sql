CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260929000000_InitialMigration') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'headless') THEN
            CREATE SCHEMA headless;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260929000000_InitialMigration') THEN
    CREATE TABLE headless.setting_definitions (
        id uuid NOT NULL,
        name character varying(128) NOT NULL,
        display_name character varying(256) NOT NULL,
        description character varying(512),
        default_value character varying(2000),
        is_visible_to_clients boolean NOT NULL,
        is_inherited boolean NOT NULL,
        is_encrypted boolean NOT NULL,
        providers character varying(1024),
        extra_properties text NOT NULL,
        CONSTRAINT pk_setting_definitions PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260929000000_InitialMigration') THEN
    CREATE TABLE headless.setting_values (
        id uuid NOT NULL,
        name character varying(128) NOT NULL,
        value character varying(2000) NOT NULL,
        provider_name character varying(64) NOT NULL,
        provider_key character varying(64),
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_setting_values PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260929000000_InitialMigration') THEN
    CREATE UNIQUE INDEX ix_setting_definitions_name ON headless.setting_definitions (name);
    CREATE UNIQUE INDEX ix_setting_values_name_provider_name_provider_key ON headless.setting_values (name, provider_name, provider_key) WHERE "provider_key" IS NOT NULL;
    CREATE UNIQUE INDEX ix_setting_values_name_provider_name_null_provider_key ON headless.setting_values (name, provider_name) WHERE "provider_key" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260929000000_InitialMigration') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260929000000_InitialMigration', '10.0.12');
    END IF;
END $EF$;
COMMIT;
