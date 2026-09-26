// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

#pragma warning disable EF1001 // EF exposes no public seam for rewriting migrations per context instance; MigrationsAssembly is the provider-agnostic one, and the tenant migration integration test catches drift across EF versions.

namespace Headless.EntityFramework.Contexts.Runtime;

/// <summary>
/// Lets migrations scaffolded once against a routed context's own default schema apply into each tenant's schema.
/// Every schema reference equal to that default (including a <see langword="null"/> default) moves to the pinned
/// tenant schema: in each migration's up and down operations, in its target model (which EF generates SQL against,
/// so seed data and column alterations find their tables), and in the model snapshot (so EF's pending-model-changes
/// check compares like with like). Explicit other schemas stay where they are. Raw SQL is not rewritten.
/// </summary>
internal sealed class HeadlessTenantMigrationsAssembly(
    ICurrentDbContext currentContext,
    IDbContextOptions options,
    IMigrationsIdGenerator idGenerator,
    IDiagnosticsLogger<DbLoggerCategory.Migrations> logger
) : MigrationsAssembly(currentContext, options, idGenerator, logger)
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _SchemaProperties = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _NestedProperties = new();

    private static readonly string[] _SchemaPropertyNames = ["Schema", "PrincipalSchema", "NewSchema"];

    private readonly HeadlessRoutedPlacement? _placement = (
        currentContext.Context as HeadlessDbContext
    )?.RoutedPlacement;

    private ModelSnapshot? _snapshot;
    private bool _snapshotRewritten;

    private bool IsTenantSchemaPlaced =>
        _placement is { IsSchemaPlaced: true }
        && !string.Equals(_placement.HostSchema, _placement.EffectiveSchema, StringComparison.Ordinal);

    public override ModelSnapshot? ModelSnapshot
    {
        get
        {
            if (_snapshotRewritten)
            {
                return _snapshot;
            }

            _snapshot = base.ModelSnapshot;

            if (_snapshot is not null && IsTenantSchemaPlaced)
            {
                _RewriteModel((IMutableModel)_snapshot.Model);
            }

            _snapshotRewritten = true;

            return _snapshot;
        }
    }

    public override Migration CreateMigration(TypeInfo migrationClass, string activeProvider)
    {
        var migration = base.CreateMigration(migrationClass, activeProvider);

        if (!IsTenantSchemaPlaced)
        {
            return migration;
        }

        // Each property builds its value once and caches it on this migration instance, so rewriting the built
        // values in place is what EF later reads.
        foreach (var operation in migration.UpOperations.Concat(migration.DownOperations))
        {
            _RewriteOperation(operation);
        }

        if (migration.TargetModel is IMutableModel targetModel)
        {
            _RewriteModel(targetModel);
        }

        return migration;
    }

    private void _RewriteModel(IMutableModel model)
    {
        var host = _placement!.HostSchema;
        var tenant = _placement.EffectiveSchema;

        if (string.Equals(model.GetDefaultSchema(), host, StringComparison.Ordinal))
        {
            model.SetDefaultSchema(tenant);
        }

        foreach (var entityType in model.GetEntityTypes())
        {
            if (
                entityType.GetTableName() is not null
                && string.Equals(entityType.GetSchema(), host, StringComparison.Ordinal)
            )
            {
                entityType.SetSchema(tenant);
            }

            if (
                entityType.GetViewName() is not null
                && string.Equals(entityType.GetViewSchema(), host, StringComparison.Ordinal)
            )
            {
                entityType.SetViewSchema(tenant);
            }
        }
    }

    private void _RewriteOperation(MigrationOperation operation)
    {
        var host = _placement!.HostSchema;
        var tenant = _placement.EffectiveSchema;

        switch (operation)
        {
            case EnsureSchemaOperation ensure when string.Equals(ensure.Name, host, StringComparison.Ordinal):
                ensure.Name = tenant!;
                break;
            case DropSchemaOperation drop when string.Equals(drop.Name, host, StringComparison.Ordinal):
                drop.Name = tenant!;
                break;
        }

        foreach (var property in _SchemaProperties.GetOrAdd(operation.GetType(), _FindSchemaProperties))
        {
            if (string.Equals((string?)property.GetValue(operation), host, StringComparison.Ordinal))
            {
                property.SetValue(operation, tenant);
            }
        }

        // Table creation nests its columns, keys, and constraints as operations of their own.
        foreach (var property in _NestedProperties.GetOrAdd(operation.GetType(), _FindNestedProperties))
        {
            switch (property.GetValue(operation))
            {
                case MigrationOperation nested:
                    _RewriteOperation(nested);
                    break;
                case IEnumerable nestedOperations:
                    foreach (var nested in nestedOperations.OfType<MigrationOperation>())
                    {
                        _RewriteOperation(nested);
                    }

                    break;
            }
        }
    }

    private static PropertyInfo[] _FindSchemaProperties(Type type) =>
        [
            .. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p =>
                    p.PropertyType == typeof(string)
                    && p is { CanRead: true, CanWrite: true }
                    && _SchemaPropertyNames.Contains(p.Name, StringComparer.Ordinal)
                ),
        ];

    private static PropertyInfo[] _FindNestedProperties(Type type) =>
        [
            .. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p =>
                    p.CanRead
                    && p.GetIndexParameters().Length == 0
                    && (
                        typeof(MigrationOperation).IsAssignableFrom(p.PropertyType)
                        || (
                            p.PropertyType != typeof(string)
                            && typeof(IEnumerable).IsAssignableFrom(p.PropertyType)
                            && p.PropertyType.GetGenericArguments() is [var item]
                            && typeof(MigrationOperation).IsAssignableFrom(item)
                        )
                    )
                ),
        ];
}
