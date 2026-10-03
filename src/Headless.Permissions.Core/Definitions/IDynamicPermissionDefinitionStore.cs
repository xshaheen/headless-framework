// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;
using Headless.Abstractions;
using Headless.Caching;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Permissions.Entities;
using Headless.Permissions.Events;
using Headless.Permissions.Models;
using Headless.Permissions.Repositories;
using Headless.Serializer.Modifiers;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace Headless.Permissions.Definitions;

/// <summary>
/// DB-backed store for permission definitions that are managed at runtime rather than compiled into code.
/// All read methods return empty/null when <see cref="PermissionManagementOptions.IsDynamicPermissionStoreEnabled"/>
/// is <see langword="false"/>.
/// </summary>
public interface IDynamicPermissionDefinitionStore
{
    /// <summary>Finds a single permission definition by name, or <see langword="null"/> if it does not exist or
    /// the dynamic store is disabled.</summary>
    Task<PermissionDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Returns all dynamically-defined permissions, or an empty list when the dynamic store is disabled.</summary>
    Task<IReadOnlyList<PermissionDefinition>> GetPermissionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all dynamically-defined permission groups, or an empty list when the dynamic store is disabled.</summary>
    Task<IReadOnlyList<PermissionGroupDefinition>> GetGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the current application's static permission definitions to the database so that other application
    /// instances can read them via the dynamic store. Uses a distributed lock scoped to this application name to
    /// prevent concurrent writes; if another instance holds the lock, the call returns without doing work.
    /// Computes a SHA-256 hash of the serialized definitions and skips the write if nothing has changed since the last
    /// save. When groups or permissions change, publishes a <see cref="DynamicPermissionDefinitionsChanged"/> event
    /// and updates the cross-application distributed-cache stamp so other instances invalidate their in-memory caches.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the cross-application common distributed lock cannot
    /// be acquired during the write phase.</exception>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
