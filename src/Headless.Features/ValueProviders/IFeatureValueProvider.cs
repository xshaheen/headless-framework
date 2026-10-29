// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Features;

/// <summary>Read-write contract for a feature value provider; extends <see cref="IFeatureValueReadProvider"/> with mutation operations.</summary>
/// <remarks>
/// New members arrive as default interface members. <see cref="SetAllEnlistedAsync" /> and
/// <see cref="DeleteAllEnlistedAsync" /> default to throwing <see cref="NotSupportedException" />, so
/// <c>unit.Features</c> refuses a provider that cannot write inside a unit of work;
/// <see cref="StoreFeatureValueProvider" /> implements them.
/// </remarks>
public interface IFeatureValueProvider : IFeatureValueReadProvider
{
    /// <summary>Persists <paramref name="value"/> for <paramref name="feature"/> under <paramref name="providerKey"/>.</summary>
    /// <param name="feature">The feature definition whose value is being set.</param>
    /// <param name="value">The new value to store.</param>
    /// <param name="providerKey">An optional key that qualifies the scope (e.g. tenant or edition identifier).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SetAsync(
        FeatureDefinition feature,
        string value,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Removes the stored value of <paramref name="feature"/> for <paramref name="providerKey"/>, reverting to the fallback value.</summary>
    /// <param name="feature">The feature definition whose value should be cleared.</param>
    /// <param name="providerKey">An optional key that qualifies the scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ClearAsync(FeatureDefinition feature, string? providerKey, CancellationToken cancellationToken = default);

    /// <summary>Stores or clears several values for <paramref name="providerKey"/> as one write.</summary>
    /// <remarks>
    /// <c>FeatureManager.SetAsync</c> routes every write through this member. The default implementation calls
    /// <see cref="SetAsync"/> and <see cref="ClearAsync"/> once per entry, so a failure part-way leaves the earlier
    /// entries written; override it when the backing source can apply the whole batch atomically, as
    /// <see cref="StoreFeatureValueProvider"/> does.
    /// </remarks>
    /// <param name="values">The values to write, each paired with its definition. A <see langword="null"/> value clears the feature.</param>
    /// <param name="providerKey">An optional key that qualifies the scope (e.g. tenant or edition identifier).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    async Task SetAllAsync(
        IReadOnlyList<KeyValuePair<FeatureDefinition, string?>> values,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        foreach (var (feature, value) in values)
        {
            if (value is null)
            {
                await ClearAsync(feature, providerKey, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SetAsync(feature, value, providerKey, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Persists <paramref name="values"/> under <paramref name="providerKey"/> inside <paramref name="unitOfWork" />'s
    /// transaction; a <see langword="null"/> value clears the feature.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction holds the writes.</param>
    /// <param name="values">The feature definitions and the values to store.</param>
    /// <param name="providerKey">An optional key that qualifies the scope (e.g. tenant or edition identifier).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The unit cannot host the writes.</exception>
    /// <exception cref="NotSupportedException">This provider cannot write inside a unit of work.</exception>
    Task SetAllEnlistedAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyList<KeyValuePair<FeatureDefinition, string?>> values,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        throw _NotEnlistable(Name);
    }

    /// <summary>
    /// Deletes every value this provider stores under <paramref name="providerKey"/> inside
    /// <paramref name="unitOfWork" />'s transaction.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction holds the delete.</param>
    /// <param name="providerKey">An optional key that qualifies the scope (e.g. tenant or edition identifier).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The names whose values were deleted; empty when the scope stored none.</returns>
    /// <exception cref="InvalidOperationException">The unit cannot host the delete.</exception>
    /// <exception cref="NotSupportedException">This provider cannot write inside a unit of work.</exception>
    Task<IReadOnlyList<string>> DeleteAllEnlistedAsync(
        IUnitOfWork unitOfWork,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        throw _NotEnlistable(Name);
    }

    private static NotSupportedException _NotEnlistable(string name)
    {
        return new NotSupportedException(
            $"The feature value provider '{name}' cannot write inside a unit of work. Write through the injected "
                + "IFeatureManager, or implement SetAllEnlistedAsync and DeleteAllEnlistedAsync."
        );
    }
}

/// <summary>Read-only contract for a feature value provider that can resolve and contextually scope feature values.</summary>
public interface IFeatureValueReadProvider
{
    /// <summary>Gets the unique name that identifies this provider (e.g. <c>"Tenant"</c>, <c>"Edition"</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Applies provider-specific context (e.g. switches the active tenant) for the duration of the returned
    /// <see cref="IAsyncDisposable"/>, then restores the original context on disposal.
    /// </summary>
    /// <param name="providerName">The name of the provider for which context should be applied.</param>
    /// <param name="providerKey">An optional key that qualifies the context (e.g. a tenant identifier).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An <see cref="IAsyncDisposable"/> that reverts the context change when disposed.</returns>
    Task<IAsyncDisposable> HandleContextAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns the raw string value of <paramref name="feature"/> for the given <paramref name="providerKey"/>, or <see langword="null"/> if not set.</summary>
    /// <param name="feature">The feature definition to look up.</param>
    /// <param name="providerKey">An optional key that qualifies the scope (e.g. tenant or edition identifier).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The stored string value, or <see langword="null"/> if this provider has no value for the feature.</returns>
    Task<string?> GetOrDefaultAsync(
        FeatureDefinition feature,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns the stored values for all <paramref name="features"/>.</summary>
    /// <remarks>
    /// Must agree with <see cref="GetOrDefaultAsync"/>: a feature resolvable through the single-value read must
    /// resolve identically through the batch, and vice versa. <c>FeatureManager</c> routes every multi-name read
    /// through this member, so a stubbed implementation silently drops those features from bulk results.
    /// </remarks>
    /// <param name="features">The feature definitions to look up.</param>
    /// <param name="providerKey">An optional key that qualifies the scope (e.g. tenant or edition identifier).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A list of <see cref="FeatureValue"/> entries; a <see langword="null"/> <c>Value</c> indicates no stored entry.</returns>
    Task<List<FeatureValue>> GetAllAsync(
        FeatureDefinition[] features,
        string? providerKey = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the stored value for <paramref name="feature"/> as seen inside <paramref name="unitOfWork" />'s
    /// transaction. <c>FeatureManager</c> uses it for the inherited-value comparison of an enlisted write, so a value
    /// the unit already wrote is seen.
    /// </summary>
    /// <remarks>
    /// The default is <see cref="GetOrDefaultAsync"/>, which is right for a provider whose values do not live in the
    /// unit's database (definition defaults). A provider that reads that database overrides it.
    /// </remarks>
    /// <param name="unitOfWork">The unit whose transaction runs the read.</param>
    /// <param name="feature">The feature definition to look up.</param>
    /// <param name="providerKey">An optional key that qualifies the scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The stored value, or <see langword="null"/> if not found.</returns>
    Task<string?> GetOrDefaultEnlistedAsync(
        IUnitOfWork unitOfWork,
        FeatureDefinition feature,
        string? providerKey,
        CancellationToken cancellationToken
    )
    {
        return GetOrDefaultAsync(feature, providerKey, cancellationToken);
    }
}
