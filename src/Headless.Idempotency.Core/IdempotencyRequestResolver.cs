// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.Extensions.Options;

namespace Headless.Idempotency;

/// <summary>
/// Validates a call's arguments and turns them into the record key and durations every entry point uses, so the
/// autonomous and enlisted surfaces cannot drift apart on what a valid admission is.
/// </summary>
internal sealed class IdempotencyRequestResolver(
    ICurrentTenant currentTenant,
    IOptionsMonitor<IdempotentOperationsOptions> options
)
{
    /// <summary>Validates an admission's arguments and the current tenant, and returns the record key.</summary>
    /// <exception cref="ArgumentException">The key, fingerprint, contract, or current tenant id is invalid.</exception>
    public IdempotencyRecordKey ResolveAdmission(
        string key,
        IdempotencyFingerprint fingerprint,
        string? expectedContract
    )
    {
        _ValidateKey(key, nameof(key));
        ValidateFingerprint(fingerprint);

        if (expectedContract is not null)
        {
            ValidateContract(expectedContract, nameof(expectedContract));
        }

        // Read on every call, never cached: the same singleton serves every tenant, and a caller may change tenant
        // between two calls.
        var tenantId = _NormalizeTenantId(currentTenant.Id, "The current tenant id", "tenantId");

        return new IdempotencyRecordKey(tenantId, key);
    }

    /// <summary>
    /// Validates an admission handed back by the caller and returns its record key. The admission's own tenant is
    /// used, not the current one, because the record belongs to the identity it was admitted under.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="admission" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">The admission is not admitted, or its identity is invalid.</exception>
    public static IdempotencyRecordKey ResolveAdmitted(IdempotentAdmission admission)
    {
        Argument.IsNotNull(admission);

        if (!admission.IsAdmitted)
        {
            throw new ArgumentException(
                "Only an admitted operation can be completed, released, renewed, or fenced; this admission is "
                    + $"{admission.Disposition}.",
                nameof(admission)
            );
        }

        _ValidateKey(admission.Key.Key, nameof(admission));
        var tenantId = _NormalizeTenantId(admission.Key.TenantId, "The admission's tenant id", nameof(admission));

        return new IdempotencyRecordKey(tenantId, admission.Key.Key);
    }

    /// <summary>Validates a peek's key and the current tenant, and returns the record key.</summary>
    /// <exception cref="ArgumentException">The key or current tenant id is invalid.</exception>
    public IdempotencyRecordKey ResolvePeek(string key)
    {
        _ValidateKey(key, nameof(key));

        // Read on every call, never cached: the same singleton serves every tenant, and a caller may change tenant
        // between two calls.
        var tenantId = _NormalizeTenantId(currentTenant.Id, "The current tenant id", "tenantId");

        return new IdempotencyRecordKey(tenantId, key);
    }

    /// <summary>Refuses a fingerprint whose algorithm this version cannot compare or whose digest cannot be stored.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="fingerprint" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">The fingerprint's algorithm is unknown or its digest is too long.</exception>
    public static void ValidateFingerprint(IdempotencyFingerprint fingerprint)
    {
        Argument.IsNotNull(fingerprint);

        if (!IdempotencyFingerprint.IsKnownAlgorithm(fingerprint.Algorithm))
        {
            throw new ArgumentException(
                $"Fingerprint algorithm '{fingerprint.Algorithm}' is not one this version can compare; compute the "
                    + "fingerprint with IdempotencyFingerprint.Compute.",
                nameof(fingerprint)
            );
        }

        if (fingerprint.Hash.Length > IdempotencyFieldLimits.FingerprintMaxLength)
        {
            throw new ArgumentException(
                $"A fingerprint digest is at most {IdempotencyFieldLimits.FingerprintMaxLength} bytes.",
                nameof(fingerprint)
            );
        }
    }

    /// <summary>Validates a result contract tag.</summary>
    /// <exception cref="ArgumentException"><paramref name="contract" /> is invalid.</exception>
    public static void ValidateContract(string contract, string paramName)
    {
        Argument.IsNotNullOrWhiteSpace(contract, paramName: paramName);
        Argument.HasMaxLength(contract, IdempotencyFieldLimits.ContractMaxLength, paramName: paramName);
        _EnsureNoSurroundingWhitespace(contract, "contract", paramName);
    }

    /// <summary>Returns the admitted attempt's lease duration: the caller's, or the configured default.</summary>
    /// <remarks>The fencing layer checks it against its own bounds.</remarks>
    public TimeSpan LeaseDuration(TimeSpan? leaseDuration)
    {
        return leaseDuration ?? options.CurrentValue.DefaultLeaseDuration;
    }

    /// <summary>Returns the retention to apply: the caller's, or the configured default.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retention" /> is not positive.</exception>
    public TimeSpan Retention(TimeSpan? retention)
    {
        // Read on every call so a default changed through options reload applies to the next admission.
        var value = retention ?? options.CurrentValue.DefaultRetention;
        Argument.IsPositive(value, paramName: nameof(retention));

        return value;
    }

    private static void _ValidateKey(string key, string paramName)
    {
        Argument.IsNotNullOrWhiteSpace(key, paramName: paramName);
        Argument.HasMaxLength(key, IdempotencyFieldLimits.KeyMaxLength, paramName: paramName);
        _EnsureNoSurroundingWhitespace(key, "key", paramName);
    }

    private static string _NormalizeTenantId(string? tenantId, string what, string paramName)
    {
        if (tenantId is null)
        {
            // The host scope is its own key namespace; single-tenant applications run here.
            return string.Empty;
        }

        // Empty is refused along with whitespace because the empty string is the stored host-scope key: accepting it
        // would merge a tenant's records into the host's.
        Argument.IsNotNullOrWhiteSpace(
            tenantId,
            $"{what} is empty or whitespace-only, so no idempotency record can be keyed by it. Use a real tenant "
                + "id, or null for the host scope.",
            paramName
        );

        if (tenantId.Length > IdempotencyFieldLimits.TenantIdMaxLength)
        {
            throw new ArgumentException(
                $"{what} is {tenantId.Length} characters long; an idempotency key allows at most "
                    + $"{IdempotencyFieldLimits.TenantIdMaxLength}.",
                paramName
            );
        }

        _EnsureNoSurroundingWhitespace(tenantId, "tenant id", paramName);

        return tenantId;
    }

    private static void _EnsureNoSurroundingWhitespace(string value, string what, string paramName)
    {
        // SQL Server pads nvarchar values with trailing spaces before comparing them, under every collation and in
        // primary-key uniqueness, so "a" and "a " would share one record there while PostgreSQL keeps them apart.
        // Refusing surrounding whitespace keeps key parts ordinal on both providers.
        if (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
        {
            throw new ArgumentException(
                $"An idempotency {what} must not start or end with whitespace: some providers ignore trailing spaces "
                    + "when comparing keys, which would merge two records.",
                paramName
            );
        }
    }
}
