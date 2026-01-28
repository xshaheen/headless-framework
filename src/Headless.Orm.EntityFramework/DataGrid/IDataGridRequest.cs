// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Orm.EntityFramework.DataGrid;

public interface IDataGridRequest : IHasMultiOrderByRequest
{
    IndexPageRequest? Page { get; }
}

public abstract class DataGridRequest : IDataGridRequest
{
    public IndexPageRequest? Page { get; init; }

    public List<OrderBy>? Orders { get; init; }
}
