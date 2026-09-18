// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.MultiTenancy;

/// <summary>
/// The closed set of states one <see cref="ITenantIdentifierSource"/> can report for a request:
/// no identifier present, an identifier found, or an input too ambiguous to interpret.
/// </summary>
[PublicAPI]
public enum TenantIdentifierSourceResultKind
{
    /// <summary>
    /// This source found no identifier on the request (absent, empty, or whitespace-only input).
    /// Resolution continues with the next registered source.
    /// </summary>
    None = 0,

    /// <summary>This source found a raw identifier; resolution stops here and the catalog owns it.</summary>
    Found = 1,

    /// <summary>
    /// The input is present but ambiguous — for example a tenant header repeated with two different
    /// values. The request is rejected with the catalog's invalid-identifier outcome before any store
    /// call; later sources never run.
    /// </summary>
    Invalid = 2,
}

/// <summary>
/// The three-state result of consulting one <see cref="ITenantIdentifierSource"/>: no identifier,
/// a raw identifier, or an ambiguous input that must reject the request. Exactly one
/// <see cref="Kind"/> applies per consult; <see cref="Identifier"/> is populated only for
/// <see cref="TenantIdentifierSourceResultKind.Found"/>.
/// </summary>
/// <remarks>
/// A value type so the per-request source loop allocates nothing, mirroring
/// <c>TenantResolutionOutcome</c>'s read-result envelope shape. <see langword="default"/> is
/// <see cref="None"/> — <see cref="TenantIdentifierSourceResultKind.None"/> is the zero value and the
/// parameterless default leaves <see cref="Identifier"/> null, so an uninitialized result never
/// masquerades as a found identifier.
/// </remarks>
[PublicAPI]
public readonly record struct TenantIdentifierSourceResult
{
    private TenantIdentifierSourceResult(TenantIdentifierSourceResultKind kind, string? identifier)
    {
        Kind = kind;
        Identifier = identifier;
    }

    /// <summary>The consult outcome category.</summary>
    public TenantIdentifierSourceResultKind Kind { get; }

    /// <summary>
    /// The raw, caller-supplied identifier, carried unchanged. Non-null only when
    /// <see cref="Kind"/> is <see cref="TenantIdentifierSourceResultKind.Found"/> — sources never
    /// trim, lowercase, or shape-validate; <c>ITenantCatalogService</c> owns normalization.
    /// </summary>
    public string? Identifier { get; }

    /// <summary>This source found no identifier; resolution continues with the next registered source.</summary>
    public static TenantIdentifierSourceResult None { get; } =
        new(TenantIdentifierSourceResultKind.None, identifier: null);

    /// <summary>
    /// The input is present but ambiguous. <c>TenantCatalogResolutionMiddleware</c> rejects the request
    /// with the catalog's invalid-identifier outcome (<c>g:tenant_identifier_invalid</c>, 400) before
    /// any store call; later sources never run.
    /// </summary>
    public static TenantIdentifierSourceResult Invalid { get; } =
        new(TenantIdentifierSourceResultKind.Invalid, identifier: null);

    /// <summary>This source found a raw identifier.</summary>
    /// <param name="identifier">
    /// The raw identifier, carried unchanged. A <see langword="null"/> or whitespace-only value is
    /// normalized to <see cref="None"/> at construction, so no consumer needs a separate
    /// "blank found" rule.
    /// </param>
    public static TenantIdentifierSourceResult Found(string? identifier)
    {
        return string.IsNullOrWhiteSpace(identifier)
            ? None
            : new TenantIdentifierSourceResult(TenantIdentifierSourceResultKind.Found, identifier);
    }
}
