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
    CREATE TABLE headless.permission_definitions (
        id uuid NOT NULL,
        group_name character varying(128) NOT NULL,
        name character varying(128) NOT NULL,
        display_name character varying(256) NOT NULL,
        is_enabled boolean NOT NULL,
        parent_name character varying(128),
        providers character varying(128),
        extra_properties text NOT NULL,
        CONSTRAINT pk_permission_definitions PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260929000000_InitialMigration') THEN
    CREATE TABLE headless.permission_grants (
        id uuid NOT NULL,
        name character varying(128) NOT NULL,
        provider_name character varying(64) NOT NULL,
        provider_key character varying(64) NOT NULL,
        tenant_id character varying(41),
        is_granted boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone,
        CONSTRAINT pk_permission_grants PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260929000000_InitialMigration') THEN
    CREATE TABLE headless.permission_group_definitions (
        id uuid NOT NULL,
        name character varying(128) NOT NULL,
        display_name character varying(256) NOT NULL,
        extra_properties text NOT NULL,
        CONSTRAINT pk_permission_group_definitions PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260929000000_InitialMigration') THEN
    CREATE INDEX ix_permission_definitions_group_name ON headless.permission_definitions (group_name);
    CREATE UNIQUE INDEX ix_permission_definitions_name ON headless.permission_definitions (name);
    CREATE UNIQUE INDEX ix_permission_grants_tenant_id_name_provider_name_provider_key ON headless.permission_grants (tenant_id, name, provider_name, provider_key) WHERE "tenant_id" IS NOT NULL;
    CREATE UNIQUE INDEX ix_permission_grants_name_provider_name_provider_key_no_tenant ON headless.permission_grants (name, provider_name, provider_key) WHERE "tenant_id" IS NULL;
    CREATE UNIQUE INDEX ix_permission_group_definitions_name ON headless.permission_group_definitions (name);
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
