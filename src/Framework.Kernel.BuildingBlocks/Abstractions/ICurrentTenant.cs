// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Helpers.System;

namespace Framework.Kernel.BuildingBlocks.Abstractions;

public interface ICurrentTenant
{
    bool IsAvailable { get; }

    Guid? Id { get; }

    string? Name { get; }

    IDisposable Change(Guid? id, string? name = null);
}

public sealed class NullCurrentTenant : ICurrentTenant
{
    public bool IsAvailable => false;

    public Guid? Id => null;

    public string? Name => null;

    public IDisposable Change(Guid? id, string? name = null) => NullDisposable.Instance;
}
