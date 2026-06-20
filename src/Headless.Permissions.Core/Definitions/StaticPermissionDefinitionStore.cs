// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Permissions.Definitions;

/// <summary>
/// Read-only access to permission definitions declared in code via registered
/// <see cref="IPermissionDefinitionProvider"/> implementations.
/// </summary>
public interface IStaticPermissionDefinitionStore
{
    /// <summary>
    /// Finds a permission by name in the code-defined static store.
    /// Returns <see langword="null"/> if no such permission exists.
    /// </summary>
    Task<PermissionDefinition?> GetOrDefaultPermissionAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Returns all statically-defined permissions, flattened across every group and nested child.</summary>
    Task<IReadOnlyCollection<PermissionDefinition>> GetAllPermissionsAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns all statically-defined permission groups.</summary>
    Task<IReadOnlyCollection<PermissionGroupDefinition>> GetGroupsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of <see cref="IStaticPermissionDefinitionStore"/>. Definitions are built once on
/// first access via a thread-safe <see cref="Lazy{T}"/> from all registered
/// <see cref="IPermissionDefinitionProvider"/> implementations. The resulting flat permission dictionary is
/// keyed by name using ordinal comparison.
/// </summary>
/// <exception cref="InvalidOperationException">Thrown (on first access) when two providers define a permission
/// with the same name — duplicate names are not allowed in the static store.</exception>
public sealed class StaticPermissionDefinitionStore : IStaticPermissionDefinitionStore
{
    private readonly Lazy<Dictionary<string, PermissionGroupDefinition>> _lazyPermissionGroupDefinitions;
    private readonly Lazy<Dictionary<string, PermissionDefinition>> _lazyPermissionDefinitions;
    private readonly PermissionManagementProvidersOptions _options;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public StaticPermissionDefinitionStore(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<PermissionManagementProvidersOptions> optionsAccessor
    )
    {
        _serviceScopeFactory = serviceScopeFactory;
        _options = optionsAccessor.Value;

        _lazyPermissionDefinitions = new Lazy<Dictionary<string, PermissionDefinition>>(
            _CreatePermissionDefinitions,
            isThreadSafe: true
        );

        _lazyPermissionGroupDefinitions = new Lazy<Dictionary<string, PermissionGroupDefinition>>(
            _CreatePermissionGroupDefinitions,
            isThreadSafe: true
        );
    }

    public Task<PermissionDefinition?> GetOrDefaultPermissionAsync(
        string name,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_lazyPermissionDefinitions.Value.GetOrDefault(name));
    }

    public Task<IReadOnlyCollection<PermissionDefinition>> GetAllPermissionsAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyCollection<PermissionDefinition>>(_lazyPermissionDefinitions.Value.Values);
    }

    public Task<IReadOnlyCollection<PermissionGroupDefinition>> GetGroupsAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyCollection<PermissionGroupDefinition>>(
            _lazyPermissionGroupDefinitions.Value.Values
        );
    }

    #region Helpers

    private Dictionary<string, PermissionDefinition> _CreatePermissionDefinitions()
    {
        var permissions = new Dictionary<string, PermissionDefinition>(StringComparer.Ordinal);

        foreach (var groupDefinition in _lazyPermissionGroupDefinitions.Value.Values)
        {
            foreach (var permission in groupDefinition.Permissions)
            {
                addPermissionToDictionaryRecursively(permissions, permission);
            }
        }

        return permissions;

        static void addPermissionToDictionaryRecursively(
            Dictionary<string, PermissionDefinition> permissions,
            PermissionDefinition permission
        )
        {
            if (!permissions.TryAdd(permission.Name, permission))
            {
                throw new InvalidOperationException("Duplicate permission name: " + permission.Name);
            }

            foreach (var child in permission.Children)
            {
                addPermissionToDictionaryRecursively(permissions, child);
            }
        }
    }

    private Dictionary<string, PermissionGroupDefinition> _CreatePermissionGroupDefinitions()
    {
        var context = new PermissionDefinitionContext();
        using var scope = _serviceScopeFactory.CreateScope();

        foreach (var providerType in _options.DefinitionProviders)
        {
            var provider = (IPermissionDefinitionProvider)scope.ServiceProvider.GetRequiredService(providerType);
            provider.Define(context);
        }

        return context.Groups;
    }

    #endregion
}
