// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Nito.Disposables;

namespace Framework.Testing.Helpers;

public sealed class TestCurrentTenant : ICurrentTenant
{
    public bool IsAvailable { get; set; }

    public string? Id { get; set; }

    public string? Name { get; set; }

    public IDisposable Change(string? id, string? name = null)
    {
        Id = id;
        Name = name;
        IsAvailable = true;

        return new Disposable(() =>
        {
            Id = null;
            Name = null;
            IsAvailable = false;
        });
    }
}
