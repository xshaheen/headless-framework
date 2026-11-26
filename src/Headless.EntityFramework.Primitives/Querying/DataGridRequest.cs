// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.EntityFramework;

/// <summary>Base implementation of <see cref="IDataGridRequest"/> with init-only page and order properties.</summary>
[PublicAPI]
public abstract class DataGridRequest : IDataGridRequest
{
    /// <summary>The page to return. A data-grid query always pages.</summary>
    public required IndexPageRequest Page { get; init; }

    /// <summary>Optional ordered list of sort columns.</summary>
    public List<OrderBy>? Orders { get; init; }
}

/// <summary>
/// Contract for data-grid query requests that carry a page descriptor and an optional ordered list
/// of sort columns.
/// </summary>
[PublicAPI]
public interface IDataGridRequest : IHasMultiOrderByRequest
{
    /// <summary>The page to return. A data-grid query always pages, so a grid never reads a whole table.</summary>
    IndexPageRequest Page { get; }
}
