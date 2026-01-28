// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Abstractions;

public interface ICurrentTenantAccessor
{
    /// <summary>
    /// <para>A null <see cref="Current"/> indicates that we haven't set it explicitly.</para>
    /// <para>A null <see cref="Current"/>.<see cref="TenantInformation.TenantId"/> indicates that we have set null tenant id value explicitly.</para>
    /// <para>A non-null <see cref="Current"/>.<see cref="TenantInformation.TenantId"/> indicates that we have set a tenant id value explicitly.</para>
    /// </summary>
    TenantInformation? Current { get; set; }
}

public sealed class AsyncLocalCurrentTenantAccessor : ICurrentTenantAccessor
{
    public static AsyncLocalCurrentTenantAccessor Instance { get; } = new();

    private readonly AsyncLocal<TenantInformation?> _currentScope;

    private AsyncLocalCurrentTenantAccessor()
    {
        _currentScope = new();
    }

    public TenantInformation? Current
    {
        get => _currentScope.Value;
        set => _currentScope.Value = value;
    }
}
