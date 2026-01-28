// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Nito.Disposables;

namespace Headless.Testing.Helpers;

public sealed class TestCurrentTenant : ICurrentTenant
{
    public bool IsAvailable => Id != null;

    public string? Id { get; set; }

    public string? Name { get; set; }

    public IDisposable Change(string? id, string? name = null)
    {
        Id = id;
        Name = name;

        return new Disposable(() =>
        {
            Id = null;
            Name = null;
        });
    }
}
