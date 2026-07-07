// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.EntityFramework;

/// <summary>
/// Contract for data-grid query requests that carry an optional page descriptor and an ordered list
/// of sort columns.
/// </summary>
public interface IDataGridRequest : IHasMultiOrderByRequest
{
    /// <summary>Optional page descriptor. When <see langword="null"/> the full result set is returned.</summary>
    IndexPageRequest? Page { get; }
}

/// <summary>Base implementation of <see cref="IDataGridRequest"/> with init-only page and order properties.</summary>
public abstract class DataGridRequest : IDataGridRequest
{
    /// <summary>Optional page descriptor.</summary>
    public IndexPageRequest? Page { get; init; }

    /// <summary>Optional ordered list of sort columns.</summary>
    public List<OrderBy>? Orders { get; init; }
}
