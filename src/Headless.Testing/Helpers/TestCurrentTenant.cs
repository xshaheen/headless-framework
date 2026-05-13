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
        // Capture the previous (Id, Name) BEFORE overwriting so the returned disposable restores the
        // outer scope's tenant rather than nulling it out. Matters for nested Change(...) usage in
        // tests: a child scope must not leak as a tenant clear when it disposes.
        var previousId = Id;
        var previousName = Name;

        Id = id;
        Name = name;

        return new Disposable(() =>
        {
            Id = previousId;
            Name = previousName;
        });
    }
}
