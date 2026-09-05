// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Models;

/// <summary>A durable, ordinal business key. Keys are never trimmed, case-folded, or released.</summary>
[PublicAPI]
public sealed record JobKey
{
    public const int MaxLength = 200;

    public JobKey(string value) => Value = JobContract.ValidateName(value);

    public string Value { get; }
}

/// <summary>The tenant and logical contract name owning a key; schema version is intentionally excluded.</summary>
[PublicAPI]
public sealed record JobKeyScope
{
    public JobKeyScope(string function, string? tenantId = null)
    {
        Function = JobContract.ValidateName(function);
        TenantId = tenantId is null ? null : JobContract.ValidateName(tenantId);
    }

    public string Function { get; }

    public string? TenantId { get; }
}
