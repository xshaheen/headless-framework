// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.EntityFramework;

/// <summary>Base implementation of <see cref="IDataGridRequest"/> with init-only page and order properties.</summary>
[PublicAPI]
public abstract class DataGridRequest : IDataGridRequest
{
    /// <summary>Optional page descriptor.</summary>
    public IndexPageRequest? Page { get; init; }

    /// <summary>Optional ordered list of sort columns.</summary>
    public List<OrderBy>? Orders { get; init; }
}
