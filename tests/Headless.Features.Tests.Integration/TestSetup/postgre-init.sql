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
    CREATE TABLE headless.feature_definitions (
        id uuid NOT NULL,
        group_name character varying(128) NOT NULL,
        name character varying(128) NOT NULL,
        display_name character varying(256) NOT NULL,
        parent_name character varying(128),
        description character varying(256),
        default_value character varying(256),
        is_visible_to_clients boolean NOT NULL,
        is_available_to_host boolean NOT NULL,
        providers character varying(256),
        extra_properties text NOT NULL,
        CONSTRAINT pk_feature_definitions PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260929000000_InitialMigration') THEN
    CREATE TABLE headless.feature_group_definitions (
        id uuid NOT NULL,
        name character varying(128) NOT NULL,
        display_name character varying(256) NOT NULL,
        extra_properties text NOT NULL,
        CONSTRAINT pk_feature_group_definitions PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260929000000_InitialMigration') THEN
    CREATE TABLE headless.feature_values (
        id uuid NOT NULL,
        name character varying(128) NOT NULL,
        value character varying(128) NOT NULL,
        provider_name character varying(64) NOT NULL,
        provider_key character varying(64),
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_feature_values PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260929000000_InitialMigration') THEN
    CREATE INDEX ix_feature_definitions_group_name ON headless.feature_definitions (group_name);
    CREATE UNIQUE INDEX ix_feature_definitions_name ON headless.feature_definitions (name);
    CREATE UNIQUE INDEX ix_feature_group_definitions_name ON headless.feature_group_definitions (name);
    CREATE UNIQUE INDEX ix_feature_values_name_provider_name_provider_key ON headless.feature_values (name, provider_name, provider_key) WHERE "provider_key" IS NOT NULL;
    CREATE UNIQUE INDEX ix_feature_values_name_provider_name_null_provider_key ON headless.feature_values (name, provider_name) WHERE "provider_key" IS NULL;
    CREATE INDEX ix_feature_values_provider_name_provider_key ON headless.feature_values (provider_name, provider_key);
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
