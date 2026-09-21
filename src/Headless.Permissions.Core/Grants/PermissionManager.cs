// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Exceptions;
using Headless.Messaging;
using Headless.Permissions.Definitions;
using Headless.Permissions.Models;
using Headless.Permissions.Repositories;
using Headless.Permissions.Resources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Permissions.Grants;

/// <summary>
/// Default <see cref="IPermissionManager"/> implementation. Delegates resolution to the registered
/// <see cref="GrantProviders.IPermissionGrantProvider"/> chain using AWS IAM-style rules: an explicit <c>Prohibited</c>
/// from any provider overrides all grants; the default is deny.
/// </summary>
public sealed class PermissionManager(
    IPermissionDefinitionManager definitionManager,
    IPermissionGrantProviderManager grantProviderManager,
    IPermissionGrantRepository repository,
    IPermissionErrorsDescriptor errorsDescriptor,
    IHostIdentityAccessor hostIdentity,
    IBus? bus = null,
    ILogger<PermissionManager>? logger = null
) : IPermissionManager
{
    private readonly ILogger _logger = logger ?? NullLogger<PermissionManager>.Instance;

    public async Task<GrantedPermissionResult> GetAsync(
        string permissionName,
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    )
    {
        var permission = await definitionManager.FindAsync(permissionName, cancellationToken).ConfigureAwait(false);

        if (permission?.IsEnabled != true)
        {
            return new(permissionName, isGranted: false);
        }

        var result = await _CoreGetOrDefaultAsync([permission], currentUser, providerName, cancellationToken)
            .ConfigureAwait(false);

        return result[0];
    }

    public async Task<IReadOnlyList<GrantedPermissionResult>> GetAllAsync(
        IReadOnlyCollection<string> permissionNames,
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(permissionNames);
        Argument.IsNotNull(currentUser);

        if (permissionNames.Count == 0)
        {
            return [];
        }

        var existPermissions = new List<PermissionDefinition>();
        var undefinedPermissions = new List<string>();

        foreach (var permissionName in permissionNames)
        {
            var permission = await definitionManager.FindAsync(permissionName, cancellationToken).ConfigureAwait(false);

            if (permission is not null)
            {
                existPermissions.Add(permission);
            }
            else
            {
                undefinedPermissions.Add(permissionName);
            }
        }

        if (existPermissions.Count == 0)
        {
            return undefinedPermissions.ConvertAll(name => new GrantedPermissionResult(name, isGranted: false));
        }

        var result = await _CoreGetOrDefaultAsync(existPermissions, currentUser, providerName, cancellationToken)
            .ConfigureAwait(false);

        result.AddRange(undefinedPermissions.Select(name => new GrantedPermissionResult(name, isGranted: false)));

        return result;
    }

    public async Task<IReadOnlyList<GrantedPermissionResult>> GetAllAsync(
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    )
    {
        var allDefinitions = await definitionManager.GetPermissionsAsync(cancellationToken).ConfigureAwait(false);
        var result = await _CoreGetOrDefaultAsync(allDefinitions, currentUser, providerName, cancellationToken)
            .ConfigureAwait(false);

        return result;
    }

    public async Task SetAsync(
        string permissionName,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    )
    {
        var permission =
            await definitionManager.FindAsync(permissionName, cancellationToken).ConfigureAwait(false)
            ?? throw new ConflictException(errorsDescriptor.PermissionIsNotDefined(permissionName));

        if (!permission.IsEnabled)
        {
            throw new ConflictException(errorsDescriptor.PermissionDisabled(permission.Name));
        }

        if (permission.Providers.Count != 0 && !permission.Providers.Contains(providerName, StringComparer.Ordinal))
        {
            throw new ConflictException(errorsDescriptor.PermissionProviderNotDefined(permission.Name, providerName));
        }

        var provider =
            grantProviderManager.ValueProviders.FirstOrDefault(m =>
                string.Equals(m.Name, providerName, StringComparison.Ordinal)
            ) ?? throw new ConflictException(errorsDescriptor.PermissionsProviderNotFound(providerName));

        await provider.SetAsync(permission, providerKey, isGranted, cancellationToken).ConfigureAwait(false);
        await _PublishChangedAsync([permissionName], providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetAsync(
        IReadOnlyCollection<string> permissionNames,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    )
    {
        var allDefinitions = await definitionManager.GetPermissionsAsync(cancellationToken).ConfigureAwait(false);

        var definedPermissions = allDefinitions
            .Where(x => permissionNames.Contains(x.Name, StringComparer.Ordinal))
            .ToList();

        var undefinedPermissions = permissionNames
            .Except(definedPermissions.Select(x => x.Name), StringComparer.Ordinal)
            .ToList();

        if (undefinedPermissions.Count != 0)
        {
            // Maybe they removed from dynamic permission definition store
            throw new ConflictException(errorsDescriptor.SomePermissionsAreNotDefined(undefinedPermissions));
        }

        var disabledPermissions = definedPermissions.Where(x => !x.IsEnabled).Select(x => x.Name).ToList();

        if (disabledPermissions.Count != 0)
        {
            throw new ConflictException(errorsDescriptor.SomePermissionsAreDisabled(disabledPermissions));
        }

        // Check if all permissions are granted
        var notDefinedProviderPermissions = definedPermissions
            .Where(x => x.Providers.Count != 0 && !x.Providers.Contains(providerName, StringComparer.Ordinal))
            .Select(x => x.Name)
            .ToList();

        if (notDefinedProviderPermissions.Count != 0)
        {
            throw new ConflictException(
                errorsDescriptor.ProviderNotDefinedForSomePermissions(notDefinedProviderPermissions, providerName)
            );
        }

        var provider =
            grantProviderManager.ValueProviders.FirstOrDefault(m =>
                string.Equals(m.Name, providerName, StringComparison.Ordinal)
            ) ?? throw new ConflictException(errorsDescriptor.PermissionsProviderNotFound(providerName));

        await provider.SetAsync(definedPermissions, providerKey, isGranted, cancellationToken).ConfigureAwait(false);

        var changedNames = definedPermissions.Select(x => x.Name).ToArray();
        await _PublishChangedAsync(changedNames, providerName, providerKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var permissionGrants = await repository
            .GetListAsync(providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        await repository.DeleteManyAsync(permissionGrants, cancellationToken).ConfigureAwait(false);

        if (permissionGrants.Count != 0)
        {
            // Every removed name rather than a wildcard: a receiver should be able to match on the names it
            // holds without knowing what else this provider scope contained.
            var removedNames = permissionGrants.Select(x => x.Name).ToArray();

            await _PublishChangedAsync(removedNames, providerName, providerKey, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Announces a completed write so every instance holding a copy of the grant, this one included, can re-read it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Published after the write, never before: a receiver that re-read on an announcement of a write that then
    /// failed would cache the old grant and believe it fresh. The injected <c>IBus</c> never enlists in a
    /// transaction, so when the caller runs <c>SetAsync</c> inside its own unit of work the announcement still goes
    /// out before that unit commits; a peer that re-reads in that window sees the old grant. The consumer contract
    /// in <c>docs/llms/permissions.md</c> names this window.
    /// </para>
    /// <para>
    /// Best-effort by design — messaging is optional, and a failed announcement must not fail the write that
    /// already succeeded, so publish failures are logged and swallowed. The cost of a lost message is that a peer
    /// keeps a stale copy until its own refresh, which is the behavior of a deployment with no bus at all.
    /// </para>
    /// </remarks>
    private async Task _PublishChangedAsync(
        string[] permissionNames,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken
    )
    {
        if (bus is null || permissionNames.Length == 0)
        {
            return;
        }

        var message = new PermissionGrantChangedMessage
        {
            PermissionNames = permissionNames,
            ProviderName = providerName,
            ProviderKey = providerKey,
            OriginInstanceId = hostIdentity.InstanceId,
        };

        try
        {
            await bus.PublishAsync(message, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogFailedToPublishPermissionGrantChanged(ex, providerName, providerKey, permissionNames.Length);
        }
    }

    #region Helpers

    private async Task<List<GrantedPermissionResult>> _CoreGetOrDefaultAsync(
        IReadOnlyCollection<PermissionDefinition> permissions,
        ICurrentUser currentUser,
        string? providerName,
        CancellationToken cancellationToken = default
    )
    {
        if (permissions.Count == 0)
        {
            return [];
        }

        // Assume all permissions are not granted
        var result = permissions.Select(x => new GrantedPermissionResult(x.Name, isGranted: false)).ToList();

        var checkNeededPermissions = permissions
            .Where(x =>
                x.IsEnabled
                && (
                    providerName is null
                    || x.Providers.Count == 0
                    || x.Providers.Contains(providerName, StringComparer.Ordinal)
                )
            )
            .ToList();

        if (checkNeededPermissions.Count == 0)
        {
            return result;
        }

        // Evaluate each matching provider exactly once - CheckAsync typically fans out to the distributed
        // grant cache (per role for the role provider), so the denial and grant passes below share these
        // results instead of doubling those round-trips per authorization check.
        var providerGrantsList = new List<(string ProviderName, MultiplePermissionGrantStatusResult Grants)>();

        foreach (var provider in grantProviderManager.ValueProviders)
        {
            if (providerName is not null && !string.Equals(provider.Name, providerName, StringComparison.Ordinal))
            {
                continue;
            }

            var providerGrants = await provider
                .CheckAsync(checkNeededPermissions, currentUser, cancellationToken)
                .ConfigureAwait(false);

            providerGrantsList.Add((provider.Name, providerGrants));
        }

        // First pass: check for explicit denials (Prohibited)
        // AWS IAM-style: explicit deny overrides all grants
        var explicitDenials = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, providerGrants) in providerGrantsList)
        {
            foreach (var (permissionName, providerResult) in providerGrants.Statuses)
            {
                if (providerResult.Status is PermissionGrantStatus.Prohibited)
                {
                    explicitDenials.Add(permissionName);
                }
            }
        }

        // Second pass: apply grants only if not explicitly denied. Index results by name once instead of a
        // linear First() scan per granted permission (quadratic for large permission batches).
        var resultsByName = result.ToDictionary(x => x.Name, StringComparer.Ordinal);

        foreach (var (grantProviderName, providerGrants) in providerGrantsList)
        {
            foreach (var (permissionName, providerResult) in providerGrants.Statuses)
            {
                if (providerResult.Status is not PermissionGrantStatus.Granted)
                {
                    continue;
                }

                // Explicit deny overrides grant
                if (explicitDenials.Contains(permissionName))
                {
                    continue;
                }

                var grant = resultsByName[permissionName];

                grant.IsGranted = true;
                grant.AddProvider(new GrantPermissionProvider(grantProviderName, providerResult.ProviderKeys));
            }
        }

        return result;
    }

    #endregion
}

/// <summary>Structured log helpers for <see cref="PermissionManager"/>.</summary>
internal static partial class PermissionManagerLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToPublishPermissionGrantChanged",
        Level = LogLevel.Warning,
        Message = "Failed to announce a permission grant change for provider {ProviderName} (key={ProviderKey}, names={NameCount}); the write succeeded and peers keep their copies until they re-read"
    )]
    public static partial void LogFailedToPublishPermissionGrantChanged(
        this ILogger logger,
        Exception exception,
        string providerName,
        string providerKey,
        int nameCount
    );
}
