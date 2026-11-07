// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>
/// Maximum lengths of the three lease-key parts, of a renewal's progress, and of what a grant stores. Call validation and every provider's
/// table definition read these, so a value that passes validation always fits its column.
/// </summary>
/// <remarks>
/// Together they total 448 characters, 896 bytes as <c>nvarchar</c>, which keeps the SQL Server clustered key under
/// its 900-byte limit. Past that limit SQL Server creates the table with only a warning and then fails the insert at
/// runtime, after validation has already accepted the key.
/// </remarks>
[PublicAPI]
public static class FencingFieldLimits
{
    /// <summary>The maximum length of a tenant id.</summary>
    public const int TenantIdMaxLength = 128;

    /// <summary>The maximum length of a lease kind.</summary>
    public const int KindMaxLength = 64;

    /// <summary>The maximum length of a leased resource.</summary>
    public const int ResourceMaxLength = 256;

    /// <summary>
    /// The maximum size, in bytes, of a renewal's progress payload: 64 KiB. Progress is a resume cursor written on
    /// every renewal that carries it, not a result store, so the bound keeps a heartbeat a small write.
    /// </summary>
    public const int ProgressMaxBytes = 64 * 1024;

    /// <summary>The maximum length of a progress contract tag.</summary>
    public const int ProgressContractMaxLength = 256;

    /// <summary>
    /// The maximum length of a grant's holder: an owner tag such as <c>cluster/node@incarnation</c>, which names the
    /// process that owns the attempt rather than carrying data about it.
    /// </summary>
    public const int HolderMaxLength = 200;

    /// <summary>
    /// The maximum size, in bytes, of a grant's attributes payload: 4 KiB. Attributes come back with every current
    /// read, often once per request, so the bound keeps that read a small one; put scope claims there, not documents.
    /// </summary>
    public const int AttributesMaxBytes = 4 * 1024;

    /// <summary>The maximum length of an attributes contract tag.</summary>
    public const int AttributesContractMaxLength = 256;
}
